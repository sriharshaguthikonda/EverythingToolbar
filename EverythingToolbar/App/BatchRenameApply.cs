using System;
using System.Collections.Generic;
using System.IO;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.App
{
    /// <summary>
    /// Pure seam between <see cref="BatchRenameDialog"/> and the BatchRenameEngine plan: builds
    /// the summary line and the effective (executable) change list from a plan plus a map from
    /// original file name to the full path it came from. No WPF, no I/O, no shell — unit tested.
    /// </summary>
    public static class BatchRenameApply
    {
        /// <summary>
        /// Formats the dialog's summary line: N items will actually change, out of M planned,
        /// K skipped (no-change items plus per-item errors).
        /// </summary>
        public static string BuildSummary(int renamedCount, int totalCount, int skippedCount) =>
            $"{renamedCount} of {totalCount} will be renamed ({skippedCount} skipped)";

        /// <summary>
        /// Filters a plan down to the renames that would actually execute: items whose new name
        /// differs from the original and carry no error, mapped back to full paths via
        /// <paramref name="fullPathByOriginalName"/> (keys are directory-stripped file names).
        /// Items missing from the map are skipped defensively. Order follows the plan.
        /// </summary>
        public static IReadOnlyList<(string Path, string NewName)> SelectEffectiveChanges(
            BatchRenamePlan plan,
            IReadOnlyDictionary<string, string> fullPathByOriginalName
        )
        {
            var changes = new List<(string Path, string NewName)>();
            foreach (var item in plan.Items)
            {
                if (item.Error is not null)
                    continue;
                if (string.Equals(item.Original, item.New, StringComparison.Ordinal))
                    continue;
                if (!fullPathByOriginalName.TryGetValue(item.Original, out var fullPath))
                    continue;

                var directory = Path.GetDirectoryName(fullPath) ?? "";
                changes.Add((Path.Combine(directory, item.New), item.New));
            }

            return changes;
        }
    }
}
