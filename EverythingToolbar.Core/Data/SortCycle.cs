namespace EverythingToolbar.Core.Data
{
    /// <summary>
    /// Pure helper that computes the next sort state when a results column header is clicked.
    /// The cycle per column is: not sorted → ascending → descending → not sorted (reset to default).
    /// </summary>
    public static class SortCycle
    {
        /// <summary>
        /// Returns the sort state to apply after clicking <paramref name="clicked"/> while currently
        /// sorted by <paramref name="current"/> (<paramref name="descending"/> indicates direction).
        /// Returns null when the cycle completes and sorting should reset to the default.
        /// </summary>
        public static (SortBy SortBy, bool Descending)? Next(SortBy current, SortBy clicked, bool descending)
        {
            if (current != clicked)
                return (clicked, false);

            return descending ? null : (clicked, true);
        }
    }
}
