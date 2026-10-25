using System;
using System.Collections.Generic;
using System.Globalization;

namespace EverythingToolbar.Core.Search
{
    /// <summary>
    /// Raw user input for the Advanced Search dialog. Every field is optional; null and
    /// empty/whitespace-only strings are treated the same (the field contributes nothing).
    /// Sizes are plain BYTES at this boundary (v1 decision: emitted as size:&gt;=N /
    /// size:&lt;=N with no unit suffix — unit selection kb/mb/gb is a later concern). Dates
    /// are dates only (time-of-day is ignored, formatted as invariant yyyy-MM-dd).
    /// </summary>
    public sealed record AdvancedSearchFields(
        string? Filename = null,
        string? PathContains = null,
        string? Extension = null,
        long? SizeMinBytes = null,
        long? SizeMaxBytes = null,
        DateTime? DateModifiedFrom = null,
        DateTime? DateModifiedTo = null,
        DateTime? DateCreatedFrom = null,
        DateTime? DateCreatedTo = null,
        string? Attributes = null
    );

    /// <summary>
    /// Pure composer for Everything 1.5 advanced-search query strings (see
    /// https://www.voidtools.com/support/everything/searching/). It shapes tokens only and
    /// never validates content: whatever text the user typed is passed through verbatim
    /// (after trimming), except that a single leading dot is stripped from the extension
    /// (Everything's ext: syntax takes the extension without the dot). Terms are
    /// space-joined, which Everything treats as AND. Returns null when no field is set —
    /// callers must not search in that case.
    /// </summary>
    public static class AdvancedSearchQuery
    {
        public static string? Compose(AdvancedSearchFields fields)
        {
            var filename = TrimOrNull(fields.Filename);
            var path = TrimOrNull(fields.PathContains);
            var extension = TrimOrNull(fields.Extension);
            var attributes = TrimOrNull(fields.Attributes);

            var terms = new List<string>(10);

            if (filename is not null)
                terms.Add(QuoteIfSpaced(filename));

            if (path is not null)
                terms.Add("path:" + Quote(path));

            if (extension is not null)
                terms.Add("ext:" + extension.TrimStart('.'));

            if (fields.SizeMinBytes is { } minBytes)
                terms.Add(FormattableString.Invariant($"size:>={minBytes}"));

            if (fields.SizeMaxBytes is { } maxBytes)
                terms.Add(FormattableString.Invariant($"size:<={maxBytes}"));

            AddDateRange(terms, "dm", fields.DateModifiedFrom, fields.DateModifiedTo);
            AddDateRange(terms, "dc", fields.DateCreatedFrom, fields.DateCreatedTo);

            if (attributes is not null)
                terms.Add("attrib:" + attributes);

            return terms.Count == 0 ? null : string.Join(" ", terms);
        }

        private static void AddDateRange(List<string> terms, string prefix, DateTime? from, DateTime? to)
        {
            if (from is { } fromValue && to is { } toValue)
                terms.Add(FormattableString.Invariant($"{prefix}:{Format(fromValue)}..{Format(toValue)}"));
            else if (from is { } fromOnly)
                terms.Add(FormattableString.Invariant($"{prefix}:>={Format(fromOnly)}"));
            else if (to is { } toOnly)
                terms.Add(FormattableString.Invariant($"{prefix}:<={Format(toOnly)}"));
        }

        private static string Format(DateTime date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string QuoteIfSpaced(string token)
        {
            return token.Contains(' ') ? Quote(token) : token;
        }

        private static string Quote(string token)
        {
            // Everything escapes embedded quotes inside quoted tokens by doubling them.
            return "\"" + token.Replace("\"", "\"\"") + "\"";
        }

        private static string? TrimOrNull(string? value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}
