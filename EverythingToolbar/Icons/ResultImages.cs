using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.Icons
{
    public sealed class ResultImages : ObservableObject
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".bmp",
            ".tiff",
            ".ico",
            ".webp",
        };

        private static readonly ISettings Settings = Ioc.Default.GetRequiredService<ISettings>();

        private const int IconSize = 16;
        private const int PreviewIconSize = 64;
        private const int PreviewThumbnailSize = 380;

        private static readonly PropertyChangedEventArgs IconChangedArgs = new(nameof(Icon));
        private static readonly PropertyChangedEventArgs PreviewImageChangedArgs = new(nameof(PreviewImage));

        private readonly SearchResult _result;
        private ImageSource? _icon;
        private ImageSource? _previewImage;
        private ImageSource? _thumbnail;
        private bool _iconLoadStarted;
        private bool _previewLoadStarted;
        private bool _thumbnailLoadStarted;
        private bool _useThumbnail;

        internal ResultImages(SearchResult result)
        {
            _result = result;
        }

        public ImageSource? Icon
        {
            get => _icon;
            private set
            {
                _icon = value;
                OnPropertyChanged(IconChangedArgs);
            }
        }

        public ImageSource? PreviewImage
        {
            get => _previewImage;
            private set
            {
                _previewImage = value;
                OnPropertyChanged(PreviewImageChangedArgs);
            }
        }

        private static readonly PropertyChangedEventArgs ThumbnailChangedArgs = new(nameof(Thumbnail));

        /// <summary>
        /// Large image for the Thumbnails view (Details view keeps the small Icon). Null until
        /// <see cref="EnsureThumbnailLoading"/> completes; falls back to the file icon when the
        /// shell has no thumbnail for the item.
        /// </summary>
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            private set
            {
                _thumbnail = value;
                OnPropertyChanged(ThumbnailChangedArgs);
            }
        }

        /// <summary>
        /// Kicks off the one-shot async thumbnail load (48 logical px) for the Thumbnails view.
        /// Runs on the thread pool via the shell thumbnail service and falls back to the cached
        /// extension icon when no thumbnail exists; the PropertyChanged raised from the worker
        /// thread is fine because every published ImageSource is frozen.
        /// </summary>
        public void EnsureThumbnailLoading()
        {
            if (_thumbnailLoadStarted)
                return;
            _thumbnailLoadStarted = true;

            const int ThumbnailPixelSize = 48;
            var path = _result.FullPathAndFileName;

            Task.Run(async () =>
            {
                ImageSource? image = await Platform
                    .Shell.ThumbnailProvider.Instance.GetThumbnailAsync(path, ThumbnailPixelSize)
                    .ConfigureAwait(false);

                // No thumbnail (or extraction failed): reuse the details-view icon pipeline so
                // the row still shows a meaningful image.
                image ??= IconProvider.GetImage(path, _result.IsFile, ThumbnailPixelSize);

                if (image != null)
                    Thumbnail = image;
            });
        }

        public void EnsureIconLoading()
        {
            if (_iconLoadStarted)
                return;
            _iconLoadStarted = true;

            _useThumbnail = Settings.IsThumbnailsEnabled && IsImageFile;

            if (ResultImageCache.TryGetRefinedIcon(_result.FullPathAndFileName, _useThumbnail, out var refinedIcon))
            {
                Icon = refinedIcon;
                return;
            }

            Icon = IconProvider.GetImage(_result.FullPathAndFileName, _result.IsFile, _useThumbnail ? IconSize : 32);
            IconLoader.Enqueue(this, _useThumbnail);
        }

        public void EnsurePreviewLoading()
        {
            if (_previewLoadStarted)
                return;
            _previewLoadStarted = true;

            // Load the regular icon first, then upgrade to a thumbnail if one is needed.
            Task.Run(() =>
            {
                var path = _result.FullPathAndFileName;
                var requiresThumbnail = IsImageFile && File.Exists(path);

                ImageSource? image = IconProvider.GetImage(path, _result.IsFile, PreviewIconSize);
                if (image != null && _previewImage == null)
                    PreviewImage = image;

                if (requiresThumbnail)
                {
                    ImageSource? thumbnail = ThumbnailProvider.GetImage(
                        path,
                        PreviewThumbnailSize,
                        allowUpscaling: false
                    );
                    if (thumbnail != null)
                        PreviewImage = thumbnail;
                }
                else
                {
                    ImageSource? exactIcon = IconProvider.GetExactImage(path, PreviewIconSize);
                    if (exactIcon != null)
                        PreviewImage = exactIcon;
                }
            });
        }

        public void SetFixedIcon(ImageSource icon)
        {
            _iconLoadStarted = true;
            Icon = icon;
        }

        // Runs on an IconLoader worker thread; may block on the file system.
        internal ImageSource? LoadRefinedIcon(bool useThumbnail)
        {
            if (useThumbnail)
            {
                return File.Exists(_result.FullPathAndFileName)
                    ? ThumbnailProvider.GetImage(_result.FullPathAndFileName, IconSize)
                    : null;
            }

            return IconProvider.GetExactImage(_result.FullPathAndFileName, 32);
        }

        // Applied by IconLoader on the dispatcher once the refined icon is ready.
        internal void ApplyRefinedIcon(ImageSource icon)
        {
            ResultImageCache.SetRefinedIcon(_result.FullPathAndFileName, _useThumbnail, icon);
            Icon = icon;
        }

        private bool IsImageFile => ImageExtensions.Contains(Path.GetExtension(_result.FullPathAndFileName));
    }

    internal static class ResultImageCache
    {
        private const int MaxRefinedIcons = 4096;

        private static readonly ConditionalWeakTable<SearchResult, ResultImages> Table = new();

        private static readonly ConcurrentDictionary<(string Path, bool UseThumbnail), ImageSource> RefinedIcons =
            new();

        public static ResultImages Get(SearchResult result) => Table.GetValue(result, static r => new ResultImages(r));

        public static bool TryGetRefinedIcon(
            string path,
            bool useThumbnail,
            [NotNullWhen(true)] out ImageSource? icon
        ) => RefinedIcons.TryGetValue((path, useThumbnail), out icon);

        public static void SetRefinedIcon(string path, bool useThumbnail, ImageSource icon)
        {
            if (RefinedIcons.Count >= MaxRefinedIcons)
                RefinedIcons.Clear();

            RefinedIcons[(path, useThumbnail)] = icon;
        }
    }
}
