using System;
using System.Collections.Generic;

namespace EverythingToolbar.Core.Data
{
    /// <summary>
    /// Pure helpers for starting a drag of the selected results out of the results table
    /// (Everything parity: "Items can also be dragged and dropped into other applications").
    /// EverythingToolbar.Core does not reference WPF, so the threshold logic works on raw
    /// coordinates instead of <c>System.Windows.Point</c> / <c>SystemParameters</c>, keeping it
    /// unit-testable.
    /// </summary>
    public static class DragSelectionBuilder
    {
        /// <summary>
        /// Builds the file paths of the given results, in order, skipping null entries and results
        /// whose <see cref="SearchResult.FullPathAndFileName"/> is null, empty or whitespace.
        /// </summary>
        public static string[] BuildFilePaths(IEnumerable<SearchResult?>? results)
        {
            if (results == null)
                return [];

            var paths = new List<string>();
            foreach (var result in results)
            {
                var path = result?.FullPathAndFileName;
                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
            }

            return [.. paths];
        }

        /// <summary>
        /// True when the pointer moved beyond WPF's standard drag thresholds (the caller passes
        /// <c>SystemParameters.MinimumHorizontalDragDistance</c> and
        /// <c>SystemParameters.MinimumVerticalDragDistance</c>). Exactly at the distance is not a
        /// drag yet; beyond it is.
        /// </summary>
        public static bool ShouldStartDrag(
            double startX,
            double startY,
            double currentX,
            double currentY,
            double horizontalThreshold,
            double verticalThreshold
        )
        {
            var dx = Math.Abs(currentX - startX);
            var dy = Math.Abs(currentY - startY);
            return dx > horizontalThreshold || dy > verticalThreshold;
        }
    }
}
