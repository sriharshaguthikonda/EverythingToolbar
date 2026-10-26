using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Config.Net;
using EverythingToolbar.App;
using EverythingToolbar.Platform.Shell;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Pure parts of the Thumbnails view: bounded cache eviction, the SIIGBF_THUMBNAILONLY
    /// fallback-flag decision, and the IsThumbnailsView settings round-trip. The P/Invoke itself
    /// (SHCreateItemFromParsingName / IShellItemImageFactory::GetImage) is not unit-testable.
    /// </summary>
    public sealed class ThumbnailProviderTests
    {
        private static BitmapSource FrozenImage()
        {
            var image = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 4 * 4);
            image.Freeze();
            return image;
        }

        [Fact]
        public void Cache_CapacityIsRespected_ByEvictingArbitraryEntries()
        {
            var cache = new ThumbnailCache(capacity: 3);

            for (var i = 0; i < 10; i++)
                cache.Add($"C:\\file{i}.png", 48, FrozenImage());

            Assert.Equal(3, cache.Count);
        }

        [Fact]
        public void Cache_RoundTripsPerPathAndSize()
        {
            var cache = new ThumbnailCache();
            var image = FrozenImage();

            cache.Add("C:\\a.png", 48, image);

            Assert.True(cache.TryGet("C:\\a.png", 48, out var hit));
            Assert.Same(image, hit);
            Assert.False(cache.TryGet("C:\\a.png", 96, out _));
            Assert.False(cache.TryGet("C:\\missing.png", 48, out _));
        }

        [Fact]
        public void Cache_OverwritingSameKey_DoesNotEvict()
        {
            var cache = new ThumbnailCache(capacity: 2);

            cache.Add("C:\\a.png", 48, FrozenImage());
            cache.Add("C:\\a.png", 48, FrozenImage());

            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void Cache_RejectsInvalidInput()
        {
            var cache = new ThumbnailCache();

            cache.Add("", 48, FrozenImage());
            cache.Add("C:\\a.png", 0, FrozenImage());
            cache.Add("C:\\a.png", 48, null!);

            Assert.Equal(0, cache.Count);
            Assert.False(cache.TryGet("", 48, out _));
        }

        [Theory]
        [InlineData(unchecked((int)0x80004005))] // E_FAIL: SIIGBF_THUMBNAILONLY fails for extensionless items
        [InlineData(unchecked((int)0x80070002))] // HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND)
        public void FallbackPolicy_FailedThumbnailOnlyAttempt_IsRetriedWithoutTheFlag(int hResult)
        {
            Assert.True(ThumbnailFallbackPolicy.ShouldRetryWithoutThumbnailOnly(hResult));
        }

        [Theory]
        [InlineData(0)] // S_OK
        [InlineData(1)] // positive HRESULTs are success codes
        public void FallbackPolicy_SucceededThumbnailOnlyAttempt_IsNotRetried(int hResult)
        {
            Assert.False(ThumbnailFallbackPolicy.ShouldRetryWithoutThumbnailOnly(hResult));
        }

        [Fact]
        public void IsThumbnailsView_DefaultsToFalse_AndRoundTrips()
        {
            var path = Path.Combine(Path.GetTempPath(), "etb-thumbview-" + Guid.NewGuid().ToString("N") + ".ini");
            try
            {
                var defaults = SettingsProxy.Create(
                    new ConfigurationBuilder<IToolbarSettings>().UseIniFile(path).Build()
                );
                Assert.False(defaults.IsThumbnailsView);

                defaults.IsThumbnailsView = true;

                var reloaded = SettingsProxy.Create(
                    new ConfigurationBuilder<IToolbarSettings>().UseIniFile(path).Build()
                );
                Assert.True(reloaded.IsThumbnailsView);
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException) { }
            }
        }
    }
}
