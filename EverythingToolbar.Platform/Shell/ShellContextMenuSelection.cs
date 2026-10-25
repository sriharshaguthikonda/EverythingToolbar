using System.Collections.Generic;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.Platform.Shell
{
    /// <summary>
    /// Pure decision helpers for the native shell context menu in the expanded search window.
    /// </summary>
    public static class ShellContextMenuSelection
    {
        /// <summary>
        /// Explorer behaviour: right-clicking an unselected item reselects just that item;
        /// right-clicking an item that is already part of the selection keeps the multi-selection.
        /// </summary>
        public static bool ShouldReselectOnRightClick(bool clickedItemIsSelected, int selectionCount) =>
            !clickedItemIsSelected;

        /// <summary>
        /// Builds the absolute path list handed to the shell context menu: order-preserving,
        /// skips empty results and blank/duplicate paths.
        /// </summary>
        public static IReadOnlyList<string> BuildPathList(IEnumerable<SearchResult?>? results)
        {
            if (results == null)
                return [];

            var paths = new List<string>();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var result in results)
            {
                var path = result?.FullPathAndFileName;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (seen.Add(path))
                    paths.Add(path);
            }

            return paths;
        }
    }
}
