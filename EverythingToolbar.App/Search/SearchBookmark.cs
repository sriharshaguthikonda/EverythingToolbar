using EverythingToolbar.Core.Data;

namespace EverythingToolbar.App.Search
{
    /// <summary>
    /// A single saved search bookmark: name, query text, optional filter name (null = no filter),
    /// and the sort that was effective when the bookmark was created. Immutable by design: the
    /// bookmarks menu rebinds whenever the store replaces collection contents, so INPC live-update
    /// support is unnecessary (replacing an instance refreshes the UI via the ObservableCollection).
    /// </summary>
    public sealed record SearchBookmark(
        string Name,
        string SearchTerm,
        string? FilterName,
        SortBy SortBy,
        bool IsSortDescending
    );
}
