using System;
using System.Collections.Generic;

namespace EverythingToolbar.Core.Data
{
    // Menu entries for the expanded search window's View > Sort By submenu (mirrors Everything's
    // View > Sort By). Display names are intentionally the user-facing labels (e.g. "Type", not
    // the CLI name "Type Name" used by SortByInfo).
    public static class SortMenuItems
    {
        public static IReadOnlyList<(SortBy SortBy, string DisplayName)> Items { get; } =
        [
            (SortBy.Name, "Name"),
            (SortBy.Path, "Path"),
            (SortBy.Size, "Size"),
            (SortBy.Extension, "Extension"),
            (SortBy.TypeName, "Type"),
            (SortBy.DateCreated, "Date Created"),
            (SortBy.DateModified, "Date Modified"),
            (SortBy.Attributes, "Attributes"),
            (SortBy.FileListFilename, "File List Filename"),
            (SortBy.RunCount, "Run Count"),
            (SortBy.DateRecentlyChanged, "Date Recently Changed"),
            (SortBy.DateAccessed, "Date Accessed"),
            (SortBy.DateRun, "Date Run"),
        ];

        public static string GetDisplayName(SortBy sortBy)
        {
            foreach (var item in Items)
            {
                if (item.SortBy == sortBy)
                    return item.DisplayName;
            }

            // Deliberately throws for enum values missing from Items so a future SortBy addition
            // surfaces immediately in the SortMenuItemsTests coverage test instead of silently
            // rendering an unnamed menu entry.
            throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, "No display name for SortBy value.");
        }
    }
}
