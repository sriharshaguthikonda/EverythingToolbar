using System;
using System.Collections.Generic;
using System.Linq;

namespace EverythingToolbar.App.SettingsSearch
{
    /// <summary>
    /// One searchable settings entry. Text is provided as accessors instead of values so the
    /// localized title/help are resolved at query time and the catalog never snapshots strings at
    /// startup. <see cref="AnchorTitle"/> names the on-page control to scroll to; when null the
    /// entry navigates to its page only.
    /// </summary>
    public sealed record SettingsSearchEntry(
        Func<string> Title,
        Func<string>? HelpText,
        Func<string> PageName,
        Type PageType,
        Func<string>? AnchorTitle = null,
        IReadOnlyList<string>? Keywords = null
    );

    /// <summary>
    /// Case-insensitive, whitespace-tolerant token filter over a settings catalog: every query
    /// token must appear in the entry's title, help text, page name or keywords. Deterministic
    /// ordering (page, then title) keeps results stable for tests and users. Deliberately simple
    /// substring matching - settings search should never guess.
    /// </summary>
    public static class SettingsSearchFilter
    {
        public static IReadOnlyList<SettingsSearchEntry> Filter(IEnumerable<SettingsSearchEntry> catalog, string query)
        {
            var tokens = (query ?? string.Empty).Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (tokens.Length == 0)
                return Array.Empty<SettingsSearchEntry>();

            return catalog
                .Where(entry => Matches(entry, tokens))
                .OrderBy(entry => entry.PageType.FullName, StringComparer.Ordinal)
                .ThenBy(entry => entry.Title(), StringComparer.Ordinal)
                .ToList();
        }

        private static bool Matches(SettingsSearchEntry entry, IReadOnlyList<string> tokens)
        {
            var haystack =
                entry.Title()
                + "\n"
                + (entry.HelpText?.Invoke() ?? string.Empty)
                + "\n"
                + entry.PageName()
                + "\n"
                + string.Join("\n", entry.Keywords ?? (IReadOnlyList<string>)Array.Empty<string>());
            foreach (var token in tokens)
            {
                if (haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }
    }
}
