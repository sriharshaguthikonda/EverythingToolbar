namespace EverythingToolbar.FuzzySearch
{
    public sealed record VocabularyEntry(string Normalized, string Display, long Frequency, bool IsCompound);
}
