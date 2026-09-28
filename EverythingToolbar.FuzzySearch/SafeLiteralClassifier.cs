using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EverythingToolbar.FuzzySearch
{
    /// <summary>
    /// Decides which whitespace-separated query terms are plain filename/path literals that may be
    /// spelling-corrected. Everything structure (properties, functions, operators, wildcards, regex,
    /// quoted strings, paths) is never touched; uncertain terms are classified as unsafe.
    /// </summary>
    public sealed class SafeLiteralClassifier
    {
        // Whitespace outside double quotes splits terms.
        private static readonly Regex TermSplitRegex = new("\"[^\"]*\"|\\S+", RegexOptions.Compiled);

        public IReadOnlyList<SearchTerm> Classify(string query)
        {
            var terms = new List<SearchTerm>();
            if (string.IsNullOrWhiteSpace(query))
            {
                return terms;
            }

            foreach (Match match in TermSplitRegex.Matches(query))
            {
                // Harness stub (commit 2): conservative default — nothing is correctable yet.
                terms.Add(new SearchTerm(match.Value, match.Index, SearchTermKind.UnsafeOrStructural));
            }

            return terms;
        }
    }
}
