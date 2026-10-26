using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Icons;

namespace EverythingToolbar.Behaviors
{
    /// <summary>
    /// Thumbnails-view counterpart of <see cref="ResultIcon"/>: attaches to an Image, starts the
    /// per-result async thumbnail load and binds Image.Source to the lazy Thumbnail property of
    /// the shared ResultImages wrapper (same per-result instance the details view uses for its
    /// icon), so switching view modes never reloads or duplicates per-row state.
    /// </summary>
    public static class ResultThumbnail
    {
        public static readonly DependencyProperty ResultProperty = DependencyProperty.RegisterAttached(
            "Result",
            typeof(SearchResult),
            typeof(ResultThumbnail),
            new PropertyMetadata(null, OnResultChanged)
        );

        public static void SetResult(DependencyObject element, SearchResult? value) =>
            element.SetValue(ResultProperty, value);

        public static SearchResult? GetResult(DependencyObject element) =>
            (SearchResult?)element.GetValue(ResultProperty);

        private static void OnResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Image image)
                return;

            if (e.NewValue is not SearchResult result)
            {
                BindingOperations.ClearBinding(image, Image.SourceProperty);
                return;
            }

            var images = ResultImageCache.Get(result);
            images.EnsureThumbnailLoading();
            BindingOperations.SetBinding(
                image,
                Image.SourceProperty,
                new Binding(nameof(ResultImages.Thumbnail)) { Source = images }
            );
        }
    }
}
