using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.App.Search
{
    /// <summary>One imported Everything 1.5 window: the named-session snapshot plus its tab count.</summary>
    public sealed record ImportedEverythingSession(SearchSessionSnapshot Snapshot, int TabCount);

    /// <summary>
    /// Result of parsing an Everything 1.5 Session JSON file: the windows that mapped cleanly
    /// (windows without tabs are skipped and counted in <see cref="SkippedWindows"/>, but their
    /// array position still counts toward the "Everything &lt;n&gt;" numbering of later windows).
    /// </summary>
    public sealed record EverythingSessionImportResult(
        IReadOnlyList<ImportedEverythingSession> Sessions,
        int SkippedWindows
    );

    /// <summary>
    /// Pure importer for Everything 1.5's own files (no disk access — callers read the file text
    /// and hand it in): Session-1.5a.json (an array of window objects, each with a "tabs" array
    /// and a "tab_cursel" active-tab index) maps one window to one
    /// <see cref="SearchSessionSnapshot"/> named "Everything &lt;n&gt;", and the search history
    /// CSV (header "Search,Search Count,Last Search Date") maps to plain search strings in file
    /// order (the count/date columns are ignored). Tolerant by design: every field may be absent
    /// (defaults: empty search, all match flags off, Name sort ascending, default filter),
    /// malformed JSON reports an error with an empty result, and malformed CSV rows are skipped.
    /// </summary>
    public static class EverythingImporter
    {
        /// <summary>
        /// Parses an Everything 1.5 Session JSON text into named-session snapshots. Returns false
        /// (with empty <paramref name="result"/> and a non-empty <paramref name="error"/>) when
        /// the text is not valid JSON or its root is not an array of window objects.
        /// </summary>
        /// <param name="filterProvider">
        /// Used to resolve imported filter names to our <see cref="Filter"/> instances; null (as
        /// in tests) leaves tab filters unresolved (null), mirroring SearchSessionStore's load
        /// pattern with a missing provider.
        /// </param>
        public static bool TryParseSessions(
            string? json,
            FilterProvider? filterProvider,
            out EverythingSessionImportResult result,
            out string error
        )
        {
            var sessions = new List<ImportedEverythingSession>();
            result = new EverythingSessionImportResult(sessions, 0);
            error = "";

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json ?? string.Empty);
            }
            catch (JsonException e)
            {
                error = $"Not valid JSON: {e.Message}";
                return false;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    error = "Expected an array of Everything window objects.";
                    return false;
                }

                var skipped = 0;
                var windowIndex = 0;
                foreach (var window in document.RootElement.EnumerateArray())
                {
                    windowIndex++;
                    if (window.ValueKind != JsonValueKind.Object)
                    {
                        skipped++;
                        continue;
                    }

                    var tabs = new List<SearchTab>();
                    if (
                        window.TryGetProperty("tabs", out var tabsElement)
                        && tabsElement.ValueKind == JsonValueKind.Array
                    )
                    {
                        foreach (var tabElement in tabsElement.EnumerateArray())
                        {
                            if (tabElement.ValueKind == JsonValueKind.Object)
                                tabs.Add(ParseTab(tabElement, filterProvider));
                        }
                    }

                    if (tabs.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    var activeIndex = Clamp(GetInt(window, "tab_cursel", 0), 0, tabs.Count - 1);
                    var snapshot = new SearchSessionSnapshot($"Everything {windowIndex}", tabs, activeIndex);
                    sessions.Add(new ImportedEverythingSession(snapshot, tabs.Count));
                }

                result = new EverythingSessionImportResult(sessions, skipped);
                return true;
            }
        }

        /// <summary>
        /// Parses an Everything search history CSV (header "Search,Search Count,Last Search Date")
        /// into search strings in file order (file order is most-recent-first). The count and date
        /// columns are ignored; the first record is treated as the header and skipped; quoted
        /// fields (RFC 4180, including doubled quotes and embedded separators) are unwrapped;
        /// rows with an empty search string are skipped. Never fails.
        /// </summary>
        public static IReadOnlyList<string> ParseSearchHistory(string? csv)
        {
            var searches = new List<string>();
            if (string.IsNullOrEmpty(csv))
                return searches;

            var i = 0;
            var isFirstRecord = true;
            while (i < csv.Length)
            {
                var firstField = ParseRecord(csv, ref i);

                if (isFirstRecord)
                {
                    isFirstRecord = false;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(firstField))
                    searches.Add(firstField);
            }

            return searches;
        }

        private static SearchTab ParseTab(JsonElement tab, FilterProvider? filterProvider)
        {
            var ascending = GetInt(tab, "sort_ascending", 1) != 0;

            var secondarySorts = new List<(SortBy SortBy, bool Descending)>();
            TryAddSecondarySort(tab, "secondary_sort", "secondary_sort_ascending", secondarySorts);
            TryAddSecondarySort(tab, "tertiary_sort", "tertiary_sort_ascending", secondarySorts);

            return new SearchTab
            {
                SearchTerm = GetString(tab, "search") ?? "",
                Filter = ResolveFilter(GetString(tab, "filter"), filterProvider),
                SortBy = ParseSort(GetString(tab, "sort")),
                IsSortDescending = !ascending,
                SecondarySorts = secondarySorts.Count > 0 ? secondarySorts : null,
                IsMatchCase = GetInt(tab, "match_case", 0) != 0,
                IsMatchPath = GetInt(tab, "match_path", 0) != 0,
                IsMatchWholeWord = GetInt(tab, "match_whole_word", 0) != 0,
                IsMatchDiacritics = GetInt(tab, "match_diacritics", 0) != 0,
                IsMatchPrefix = GetInt(tab, "match_prefix", 0) != 0,
                IsMatchSuffix = GetInt(tab, "match_suffix", 0) != 0,
                IsIgnorePunctuation = GetInt(tab, "ignore_punctuation", 0) != 0,
                IsIgnoreWhitespace = GetInt(tab, "ignore_whitespace", 0) != 0,
                IsRegExEnabled = GetInt(tab, "match_regex", 0) != 0,
            };
        }

        private static void TryAddSecondarySort(
            JsonElement tab,
            string sortProperty,
            string ascendingProperty,
            List<(SortBy SortBy, bool Descending)> secondarySorts
        )
        {
            if (secondarySorts.Count >= SearchState.MaxSecondarySorts)
                return;

            var name = GetString(tab, sortProperty);
            if (TryMapSort(name, out var sortBy))
                secondarySorts.Add((sortBy, GetInt(tab, ascendingProperty, 1) == 0));
        }

        /// <summary>
        /// Maps an Everything sort display name via the SortMenuItems display names. Unknown or
        /// empty names fall back to Name ascending (an unknown name cannot carry a meaningful
        /// direction).
        /// </summary>
        private static SortBy ParseSort(string? name)
        {
            return TryMapSort(name, out var sortBy) ? sortBy : SortBy.Name;
        }

        private static bool TryMapSort(string? name, out SortBy sortBy)
        {
            sortBy = SortBy.Name;
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (var item in SortMenuItems.Items)
            {
                if (string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                {
                    sortBy = item.SortBy;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves an imported filter name the way SearchSessionStore does on load (exact name
        /// lookup in the provider), with Everything's own pseudo-filters mapped to our default
        /// (first) filter: Everything 1.5's built-in "EVERYTHING" filter and the empty/"All"
        /// names all mean "no filter".
        /// </summary>
        private static Filter? ResolveFilter(string? name, FilterProvider? filterProvider)
        {
            if (filterProvider is null)
                return null;

            var filters = filterProvider.Filters;
            if (filters.Count == 0)
                return null;

            if (
                string.IsNullOrEmpty(name)
                || string.Equals(name, "EVERYTHING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "All", StringComparison.OrdinalIgnoreCase)
            )
            {
                return filters[0];
            }

            return filters.FirstOrDefault(f => f.Name == name) ?? filters[0];
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }

            return null;
        }

        private static int GetInt(JsonElement element, string propertyName, int fallback)
        {
            if (
                element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out var value)
            )
            {
                return value;
            }

            return fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min
                : value > max ? max
                : value;
        }

        /// <summary>
        /// Reads one RFC 4180 record starting at <paramref name="index"/>, returning its first
        /// field and advancing the index past the record's terminating comma or newline.
        /// </summary>
        private static string ParseRecord(string csv, ref int index)
        {
            var firstField = ParseField(csv, ref index);

            if (index < csv.Length && csv[index] == ',')
            {
                index++;
                SkipRemainingFields(csv, ref index);
            }

            if (index < csv.Length && csv[index] == '\r')
                index++;
            if (index < csv.Length && csv[index] == '\n')
                index++;

            return firstField;
        }

        private static string ParseField(string csv, ref int index)
        {
            var field = new StringBuilder();

            if (index < csv.Length && csv[index] == '"')
            {
                index++;
                while (index < csv.Length)
                {
                    if (csv[index] == '"')
                    {
                        if (index + 1 < csv.Length && csv[index + 1] == '"')
                        {
                            field.Append('"');
                            index += 2;
                            continue;
                        }

                        index++;
                        break;
                    }

                    field.Append(csv[index]);
                    index++;
                }
            }
            else
            {
                while (index < csv.Length && csv[index] != ',' && csv[index] != '\n' && csv[index] != '\r')
                {
                    field.Append(csv[index]);
                    index++;
                }
            }

            return field.ToString();
        }

        private static void SkipRemainingFields(string csv, ref int index)
        {
            while (index < csv.Length && csv[index] != '\n' && csv[index] != '\r')
            {
                ParseField(csv, ref index);
                if (index < csv.Length && csv[index] == ',')
                    index++;
            }
        }
    }
}
