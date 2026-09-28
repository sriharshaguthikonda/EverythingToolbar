using System;
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
        /// <summary>Terms shorter than this are never corrected (too few letters to disambiguate).</summary>
        public const int MinCorrectableLength = 4;

        // Whitespace outside double quotes splits terms.
        private static readonly Regex TermSplitRegex = new("\"[^\"]*\"|\\S+", RegexOptions.Compiled);

        // Plain filename token: starts with a letter, then letters, digits, underscores or hyphens.
        // Everything operators (: * ? < > |), property syntax, paths and regex characters all fall
        // outside this shape and are therefore never correctable.
        private static readonly Regex PlainLiteralRegex = new("^\\p{L}[\\p{L}\\p{Nd}_-]*$", RegexOptions.Compiled);

        private static readonly HashSet<string> ReservedKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "AND",
            "OR",
            "NOT",
        };

        public IReadOnlyList<SearchTerm> Classify(string query)
        {
            var terms = new List<SearchTerm>();
            if (string.IsNullOrWhiteSpace(query))
            {
                return terms;
            }

            foreach (Match match in TermSplitRegex.Matches(query))
            {
                var kind = IsPlainLiteral(match.Value)
                    ? SearchTermKind.PlainLiteral
                    : SearchTermKind.UnsafeOrStructural;
                terms.Add(new SearchTerm(match.Value, match.Index, kind));
            }

            return terms;
        }

        /// <summary>True when the term is a plain literal long enough to be spelling-corrected.</summary>
        public static bool IsCorrectable(SearchTerm term)
        {
            return term.Kind == SearchTermKind.PlainLiteral && term.Text.Length >= MinCorrectableLength;
        }

        private static bool IsPlainLiteral(string term)
        {
            return !term.Contains('"')
                && !term.StartsWith('-')
                && !term.StartsWith('!')
                && !char.IsDigit(term[0])
                && !ReservedKeywords.Contains(term)
                && PlainLiteralRegex.IsMatch(term);
        }
    }
}
