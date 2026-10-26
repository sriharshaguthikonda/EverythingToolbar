using System.Collections.Generic;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.Core.Search
{
    public sealed record SearchQuery(
        string SearchText,
        SortBy SortBy,
        bool SortDescending,
        bool MatchCase,
        bool MatchPath,
        bool MatchWholeWord,
        bool UseRegex,
        bool HideResultOmissions = false,
        // Everything 1.5 multi-sort: secondary/tertiary sorts applied after the primary
        // SortBy/SortDescending. Null when absent. List identity participates in record
        // equality (the list is compared by reference, not element-wise).
        IReadOnlyList<(SortBy SortBy, bool Descending)>? SecondarySorts = null
    );
}
