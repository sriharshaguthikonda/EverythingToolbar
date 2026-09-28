namespace EverythingToolbar.FuzzySearch
{
    /// <summary>
    /// Conservative distance limits by term length. Short terms are never corrected (too ambiguous,
    /// think form/from). Medium terms allow two edits: the required corpus case repomsp->repomaps
    /// needs two (missing 'a' + transposition) at length 7, so the textbook 4-7->1 rule fails its
    /// own target; minimum distance is enforced by SymSpell's closest-verbosity instead. Tune from
    /// corpus evidence only.
    /// </summary>
    public static class EditDistancePolicy
    {
        public const int MinCorrectableLength = SafeLiteralClassifier.MinCorrectableLength;

        public static int MaxDistanceFor(int termLength)
        {
            return termLength switch
            {
                <= 3 => 0,
                _ => 2,
            };
        }
    }
}
