namespace EverythingToolbar.FuzzySearch
{
    public enum SearchTermKind
    {
        PlainLiteral,
        UnsafeOrStructural,
    }

    public sealed record SearchTerm(string Text, int Index, SearchTermKind Kind);
}
