using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace EverythingToolbar.Platform.Shell
{
    /// <summary>
    /// Pure decision helper for IShellItemImageFactory::GetImage flag selection, kept free of
    /// P/Invoke so it is unit-testable. SIIGBF_THUMBNAILONLY fails for items that have no
    /// thumbnail — e.g. extensionless files — so any error HRESULT from the thumbnail-only
    /// attempt is retried WITHOUT the flag, which makes the shell return the item's icon instead.
    /// </summary>
    public static class ThumbnailFallbackPolicy
    {
        /// <summary>True when the thumbnail-only attempt failed and the icon-fallback retry should run.</summary>
        public static bool ShouldRetryWithoutThumbnailOnly(int thumbnailOnlyAttemptHResult) =>
            thumbnailOnlyAttemptHResult < 0;
    }

    /// <summary>
    /// Bounded thread-safe cache for frozen shell thumbnails. Not a strict LRU: entries carry no
    /// recency tracking; once the capacity is reached a single arbitrary entry is evicted per
    /// insertion so memory stays bounded (~500 small bitmaps). Callers pay at worst one extra
    /// shell extraction for an evicted-but-recently-used entry.
    /// </summary>
    public sealed class ThumbnailCache
    {
        private readonly ConcurrentDictionary<(string Path, int PixelSize), BitmapSource> _items = new();
        private readonly int _capacity;

        public ThumbnailCache(int capacity = 500)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Count => _items.Count;

        public bool TryGet(string path, int pixelSize, out BitmapSource? image)
        {
            if (string.IsNullOrEmpty(path) || pixelSize < 1)
            {
                image = null;
                return false;
            }

            return _items.TryGetValue((path, pixelSize), out image);
        }

        public void Add(string path, int pixelSize, BitmapSource image)
        {
            if (string.IsNullOrEmpty(path) || pixelSize < 1 || image == null)
                return;

            var key = (path, pixelSize);
            if (_items.Count >= _capacity && !_items.ContainsKey(key))
                EvictOne();

            _items[key] = image;
        }

        /// <summary>Evicts one arbitrary entry (first key observed) to make room under the cap.</summary>
        private void EvictOne()
        {
            foreach (var key in _items.Keys)
            {
                _items.TryRemove(key, out _);
                return;
            }
        }
    }

    /// <summary>
    /// Async shell thumbnail service built directly on the documented Win32 contract:
    /// SHCreateItemFromParsingName → IShellItemImageFactory::GetImage(SIIGBF_THUMBNAILONLY |
    /// SIIGBF_RESIZETOFIT) → HBITMAP → frozen BitmapSource (with a DeleteObject via SafeHandle).
    /// All extraction runs on the thread pool (GetImage must not run on the UI thread); results
    /// are frozen before crossing back to the UI and cached per (path, pixel size).
    /// </summary>
    public sealed class ThumbnailProvider
    {
        public static ThumbnailProvider Instance { get; } = new();

        private readonly ThumbnailCache _cache = new(500);

        public async Task<BitmapSource?> GetThumbnailAsync(
            string path,
            int pixelSize,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrEmpty(path) || pixelSize < 1)
                return null;

            if (_cache.TryGet(path, pixelSize, out var cached))
                return cached;

            return await Task.Run(() => GetThumbnail(path, pixelSize), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Blocking extraction; thread-pool only. Null on any failure (UI falls back to icons).</summary>
        public BitmapSource? GetThumbnail(string path, int pixelSize)
        {
            if (string.IsNullOrEmpty(path) || pixelSize < 1)
                return null;

            if (_cache.TryGet(path, pixelSize, out var cached))
                return cached;

            IShellItemImageFactory? factory = null;
            try
            {
                PInvoke.SHCreateItemFromParsingName(path, null, out factory);
                if (factory is null)
                    return null;

                var size = new SIZE(pixelSize, pixelSize);

                // First attempt: real thumbnails only, resized to fit the requested pixel size.
                var (ok, hResult, hBitmap) = TryGetImage(
                    factory,
                    size,
                    SIIGBF.SIIGBF_THUMBNAILONLY | SIIGBF.SIIGBF_RESIZETOFIT
                );

                // SIIGBF_THUMBNAILONLY fails for items without thumbnails (extensionless files,
                // some virtual items): retry WITHOUT the flag so the shell returns the item's
                // icon instead.
                if (!ok && ThumbnailFallbackPolicy.ShouldRetryWithoutThumbnailOnly(hResult))
                    (ok, hResult, hBitmap) = TryGetImage(factory, size, SIIGBF.SIIGBF_RESIZETOFIT);

                if (!ok || hBitmap == null)
                    return null;

                using (hBitmap)
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap.DangerousGetHandle(),
                        IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions()
                    );
                    // Frozen before caching/returning so it is safe to bind from any thread.
                    source.Freeze();
                    _cache.Add(path, pixelSize, source);
                    return source;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (factory != null && Marshal.IsComObject(factory))
                    Marshal.ReleaseComObject(factory);
            }
        }

        /// <summary>
        /// Runs IShellItemImageFactory::GetImage once. CsWin32's generated COM interop is not
        /// PreserveSig, so a failed HRESULT surfaces as an exception whose HResult is captured
        /// here and fed to ThumbnailFallbackPolicy by the caller. A "succeeding" call with an
        /// invalid handle is treated as E_FAIL. The handle (when valid) is always returned to
        /// the caller for disposal.
        /// </summary>
        private static (bool Ok, int HResult, Windows.Win32.DeleteObjectSafeHandle? Bitmap) TryGetImage(
            IShellItemImageFactory factory,
            SIZE size,
            SIIGBF flags
        )
        {
            try
            {
                factory.GetImage(size, flags, out var hBitmap);
                if (hBitmap is { IsInvalid: false })
                    return (true, 0, hBitmap);

                hBitmap.Dispose();
                return (false, unchecked((int)0x80004005), null); // E_FAIL
            }
            catch (Exception ex) when (ex.HResult != 0)
            {
                return (false, ex.HResult, null);
            }
        }
    }
}
