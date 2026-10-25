using System;

namespace EverythingToolbar.Core.Data
{
    /// <summary>
    /// Pure per-column visibility state used to emulate hiding a <c>GridViewColumn</c> by
    /// collapsing its Width to 0 (GridViewColumn has no Visibility property). While hidden, the
    /// column's real width is kept in <see cref="SavedWidth"/> so it can be restored verbatim.
    /// </summary>
    public sealed class ColumnVisibilityState
    {
        /// <summary>
        /// Minimum width used when restoring a column whose saved width is 0 (or otherwise below
        /// the minimum): a visible column must remain usable/grabbable for the user.
        /// </summary>
        public const double MinRestoredWidth = 40;

        public ColumnVisibilityState(string columnName, bool isVisible = true, double savedWidth = 0)
        {
            ColumnName = columnName;
            IsVisible = isVisible;
            SavedWidth = savedWidth;
        }

        public string ColumnName { get; }

        public bool IsVisible { get; private set; }

        /// <summary>The real width of the column, captured at the moment it was hidden.</summary>
        public double SavedWidth { get; private set; }

        /// <summary>Toggles visibility and returns the width to assign to the column.</summary>
        public double Toggle(double currentWidth)
        {
            return SetVisible(!IsVisible, currentWidth);
        }

        /// <summary>
        /// Sets visibility and returns the width to assign to the column. Hiding captures
        /// <paramref name="currentWidth"/> into <see cref="SavedWidth"/> and yields 0; showing
        /// yields the saved width (at least <see cref="MinRestoredWidth"/>). No-op states return
        /// the current width unchanged.
        /// </summary>
        public double SetVisible(bool isVisible, double currentWidth)
        {
            if (isVisible == IsVisible)
                return currentWidth;

            if (!isVisible)
            {
                SavedWidth = currentWidth;
                IsVisible = false;
                return 0;
            }

            IsVisible = true;
            return Apply(this);
        }

        /// <summary>
        /// Restores the designed default state: visible with the given default width. Returns the
        /// width to assign to the column.
        /// </summary>
        public double ResetTo(double defaultWidth)
        {
            IsVisible = true;
            SavedWidth = defaultWidth;
            return defaultWidth;
        }

        /// <summary>
        /// Computes the effective width for a column: 0 while hidden; otherwise the saved width,
        /// clamped up to <see cref="MinRestoredWidth"/> (a saved width of 0 restores to 40).
        /// </summary>
        public static double Apply(ColumnVisibilityState state)
        {
            if (!state.IsVisible)
                return 0;

            return Math.Max(state.SavedWidth, MinRestoredWidth);
        }
    }
}
