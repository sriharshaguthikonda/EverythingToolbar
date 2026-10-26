namespace EverythingToolbar.App.Search
{
    /// <summary>
    /// Pure helpers for the expanded window's bookmark bar (Everything-1.5 parity quick-access
    /// row). Kept free of any WPF dependency so the visibility rule and the button tooltip text
    /// can be unit-tested directly.
    /// </summary>
    public static class BookmarkBarInfo
    {
        /// <summary>
        /// The bar is shown only when the user enabled it (View > Bookmark Bar) AND at least one
        /// bookmark exists — an empty bar would be dead chrome, so it collapses itself even when
        /// the setting is on.
        /// </summary>
        public static bool IsBarVisible(bool isBookmarkBarEnabled, int bookmarkCount) =>
            isBookmarkBarEnabled && bookmarkCount > 0;

        /// <summary>
        /// Tooltip text for a bookmark bar button: the query text, plus the filter name in
        /// brackets when the bookmark was saved with a filter (null/empty filter means "All").
        /// </summary>
        public static string BuildTooltip(SearchBookmark? bookmark)
        {
            if (bookmark is null)
                return "";

            return string.IsNullOrWhiteSpace(bookmark.FilterName)
                ? bookmark.SearchTerm
                : $"{bookmark.SearchTerm}  [{bookmark.FilterName}]";
        }
    }
}
