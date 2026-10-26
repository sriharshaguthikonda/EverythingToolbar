using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EverythingToolbar.Core.Data
{
    /// <summary>
    /// Options for <see cref="BatchRenameEngine.Plan"/>. Semantics follow Microsoft
    /// PowerToys PowerRename (MIT) — implemented from behaviour, not translated from C++.
    /// </summary>
    /// <param name="Search">Literal or regex search text. Empty search disables renaming
    /// unless the replace string contains counter patterns.</param>
    /// <param name="Replace">Replacement text. May contain <c>$1</c>/<c>$2</c> substitution
    /// references (regex mode only, standard .NET substitution syntax) and counter patterns
    /// of the form <c>${start=10,increment=5,padding=3}</c>.</param>
    /// <param name="UseRegex">Interpret Search as a .NET regular expression. Regex mode
    /// always replaces ALL matches; literal mode replaces only the first occurrence unless
    /// <paramref name="MatchAllOccurrences"/> is set (PowerRename-faithful behaviour).</param>
    /// <param name="CaseSensitive">Case-sensitive matching (ordinal comparison).</param>
    /// <param name="MatchAllOccurrences">Literal mode only: replace every occurrence
    /// instead of the first one.</param>
    /// <param name="IncludeExtension">When false (default) the search/replace applies to
    /// the file name without its extension; the extension is re-attached afterwards.
    /// When true the full name including the dot+extension is renamed.</param>
    /// <param name="CounterFormat">PowerRename parity toggle for counter ("enumerate
    /// items") support: when non-null and non-empty, <c>${...}</c> counter patterns in
    /// Replace are expanded. The pattern body itself controls start/increment/padding,
    /// so this string only acts as the on/off switch.</param>
    public sealed record BatchRenameOptions(
        string Search,
        string Replace,
        bool UseRegex,
        bool CaseSensitive,
        bool MatchAllOccurrences,
        bool IncludeExtension,
        string? CounterFormat
    )
    {
        public static BatchRenameOptions Default { get; } = new("", "", false, false, false, false, null);
    }

    /// <summary>One planned rename. <see cref="Error"/> is set when the new name is invalid.</summary>
    public sealed record BatchRenameItem(string Original, string New, string? Error);

    /// <summary>
    /// The in-memory result of a batch rename plan. No filesystem state is touched.
    /// </summary>
    public sealed record BatchRenamePlan(IReadOnlyList<BatchRenameItem> Items, bool IsNoOp, string? Error)
    {
        public bool HasError => Error is not null;
    }

    /// <summary>
    /// Pure, static batch-rename planning engine (PowerRename parity, v1 scope).
    ///
    /// Semantics follow Microsoft PowerToys PowerRename (MIT) — implemented from
    /// behaviour, not translated from C++. Reference sources consulted:
    ///   src/modules/powerrename/lib/PowerRenameRegEx.cpp  (Replace loop, counter
    ///     increment only when the item had a match, literal first-only vs all,
    ///     regex always-all, single enumIndex shared across the whole item set)
    ///   src/modules/powerrename/lib/Enumerating.h/.cpp     (counter defaults
    ///     start=0, increment=1, padding=0; value = start + index * increment;
    ///     pattern body parsed with lazy <c>\$\{.*?\}</c> and sub-regexes
    ///     start=(-?\d+), increment=(-?\d+), padding=(\d+))
    ///   src/modules/powerrename/lib/PowerRenameManager.cpp (one itemEnumIndex runs
    ///     across all items in the s_regexWorkerThread loop)
    ///
    /// v1 scope notes / documented behaviour:
    ///  - Regex mode replaces ALL matches (PowerRename does the same); literal mode
    ///    replaces the FIRST match only unless MatchAllOccurrences.
    ///  - Regex replacement uses .NET substitution syntax, so $1/$2 group references
    ///    work; $$ escapes a literal dollar.
    ///  - Counters use one shared index across the whole batch; it advances only for
    ///    items where the search matched (PowerRename's itemEnumIndex behaviour).
    ///    Multiple counter patterns in one Replace each receive the same per-item
    ///    counter value (formatted with their own start/increment/padding). Counters
    ///    never increment within a single name.
    ///  - Case-changing operations (uppercase/lowercase/title-case) are out of scope v1.
    ///  - No I/O, no randomness, no DateTime: CultureInvariant is used everywhere
    ///    except the hardcoded English error strings.
    /// </summary>
    public static class BatchRenameEngine
    {
        /// <summary>Plan-level error shown once when the regex cannot be compiled.</summary>
        public const string InvalidRegexError = "Invalid regular expression";

        /// <summary>Per-item error when the computed new name is empty.</summary>
        public const string EmptyNameError = "New file name cannot be empty";

        /// <summary>Per-item error for names Windows forbids (per WindowsPathValidator).</summary>
        public const string InvalidNameError = "New file name contains invalid characters";

        private static readonly Regex CounterPatternRegex = new(@"\$\{(.*?)\}", RegexOptions.CultureInvariant);

        private static readonly Regex CounterStartRegex = new(@"start=(-?\d+)", RegexOptions.CultureInvariant);
        private static readonly Regex CounterIncrementRegex = new(@"increment=(-?\d+)", RegexOptions.CultureInvariant);
        private static readonly Regex CounterPaddingRegex = new(@"padding=(\d+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Compute the rename plan for the given file names.
        /// <paramref name="fileNames"/> are full file names WITHOUT path — the caller is
        /// responsible for stripping directory parts. Items whose new name equals the
        /// original come back unchanged with no error (the caller filters no-ops).
        /// </summary>
        public static BatchRenamePlan Plan(IReadOnlyList<string> fileNames, BatchRenameOptions options)
        {
            ArgumentNullException.ThrowIfNull(fileNames);
            ArgumentNullException.ThrowIfNull(options);

            var search = options.Search ?? "";
            var replace = options.Replace ?? "";
            var countersEnabled = !string.IsNullOrEmpty(options.CounterFormat);

            var counters = countersEnabled
                ? CounterPatternRegex.Matches(replace).Select(ParseCounter).ToList()
                : new List<CounterSpec>();

            // A plan is a no-op when there is nothing to search for and no counter
            // pattern that could still alter the name.
            if (search.Length == 0 && counters.Count == 0)
            {
                return new BatchRenamePlan(
                    fileNames.Select(n => new BatchRenameItem(n, n, null)).ToList(),
                    IsNoOp: true,
                    Error: null
                );
            }

            Regex? regex = null;
            if (options.UseRegex)
            {
                try
                {
                    var regexOptions = RegexOptions.CultureInvariant;
                    if (!options.CaseSensitive)
                        regexOptions |= RegexOptions.IgnoreCase;
                    regex = new Regex(search, regexOptions);
                }
                catch (ArgumentException)
                {
                    // One plan-level error, surfaced once — not per item.
                    return new BatchRenamePlan(
                        fileNames.Select(n => new BatchRenameItem(n, n, null)).ToList(),
                        IsNoOp: false,
                        Error: InvalidRegexError
                    );
                }
            }

            var items = new List<BatchRenameItem>(fileNames.Count);
            var counterIndex = 0L;

            foreach (var original in fileNames)
            {
                var (target, suffix) = SplitExtension(original, options.IncludeExtension);
                var newTarget = target;
                var matched = false;

                // Counters are substituted into the replace template FIRST, per item,
                // exactly like PowerRename inserts the enumerator output into the
                // replace term before running the regex/literal replacement.
                var effectiveReplace =
                    counters.Count > 0 ? SubstituteCounters(replace, counters, counterIndex) : replace;

                if (regex is not null)
                {
                    matched = regex.IsMatch(target);
                    if (matched)
                        newTarget = regex.Replace(target, effectiveReplace);
                }
                else if (search.Length > 0)
                {
                    newTarget = ReplaceLiteral(
                        target,
                        search,
                        effectiveReplace,
                        options.CaseSensitive,
                        options.MatchAllOccurrences,
                        out matched
                    );
                }
                else if (counters.Count > 0)
                {
                    // No search text: counter patterns alone rewrite the whole target.
                    newTarget = effectiveReplace;
                    matched = true;
                }

                var newName = newTarget + suffix;

                // PowerRename advances the shared counter only for items that matched.
                if (matched)
                    counterIndex++;

                string? error = null;
                if (!WindowsPathValidator.IsValidFileName(newName))
                    error = newName.Length == 0 ? EmptyNameError : InvalidNameError;

                items.Add(new BatchRenameItem(original, newName, error));
            }

            return new BatchRenamePlan(items, IsNoOp: false, Error: null);
        }

        /// <summary>
        /// Splits a full file name into the rename target and the extension suffix.
        /// Default (IncludeExtension=false) renames the stem only; the dot+extension is
        /// re-attached verbatim afterwards. A leading dot (dotfile, e.g. ".gitignore")
        /// is treated as part of the stem — matching .NET path semantics. When
        /// IncludeExtension is true, the whole name is the target and the suffix is empty.
        /// </summary>
        private static (string Target, string Suffix) SplitExtension(string fileName, bool includeExtension)
        {
            if (includeExtension)
                return (fileName, "");

            var dot = fileName.LastIndexOf('.');
            if (dot <= 0)
                return (fileName, "");

            return (fileName[..dot], fileName[dot..]);
        }

        /// <summary>
        /// Literal replace. Ordinal comparison; case-insensitive when requested
        /// (PowerRename lowercases both sides with towlower, which is closest to
        /// ordinal-ignore-case for our purposes). Replaces the first occurrence only
        /// unless <paramref name="matchAll"/>. <paramref name="matched"/> reports whether
        /// anything was replaced (drives counter advancement).
        /// </summary>
        private static string ReplaceLiteral(
            string input,
            string search,
            string replace,
            bool caseSensitive,
            bool matchAll,
            out bool matched
        )
        {
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            matched = false;
            if (search.Length == 0 || input.Length == 0)
                return input;

            string result = input;
            int pos = 0;
            int index;
            while ((index = result.IndexOf(search, pos, comparison)) >= 0)
            {
                matched = true;
                result = result[..index] + replace + result[(index + search.Length)..];
                if (!matchAll)
                    break;
                pos = index + replace.Length;
            }

            return result;
        }

        /// <summary>
        /// Replace every <c>${...}</c> occurrence in the replace template with the
        /// formatted counter value for <paramref name="index"/>. Each occurrence keeps
        /// its own start/increment/padding; all occurrences receive the same index.
        /// Formatting matches PowerRename's Enumerator::printTo: %0{padding}d, i.e.
        /// decimal digits left-padded with '0' to the padding width (no truncation).
        /// </summary>
        private static string SubstituteCounters(string replace, IReadOnlyList<CounterSpec> counters, long index)
        {
            var matches = CounterPatternRegex.Matches(replace);
            if (matches.Count == 0)
                return replace;

            var sb = new System.Text.StringBuilder(replace.Length + 16);
            var cursor = 0;
            for (var i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                sb.Append(replace, cursor, m.Index - cursor);
                sb.Append(FormatCounter(counters[i], index));
                cursor = m.Index + m.Length;
            }

            sb.Append(replace, cursor, replace.Length - cursor);
            return sb.ToString();
        }

        private static string FormatCounter(CounterSpec spec, long index)
        {
            var value = spec.Start + spec.Increment * index;
            var digits = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return spec.Padding > digits.Length ? digits.PadLeft((int)spec.Padding, '0') : digits;
        }

        private static CounterSpec ParseCounter(Match match)
        {
            var body = match.Groups[1].Value;
            var start = MatchLong(body, CounterStartRegex) ?? 0;
            var increment = MatchLong(body, CounterIncrementRegex) ?? 1;
            var padding = MatchLong(body, CounterPaddingRegex) ?? 0;
            if (padding < 0)
                padding = 0;
            return new CounterSpec(start, increment, padding);
        }

        private static long? MatchLong(string body, Regex regex)
        {
            var m = regex.Match(body);
            if (!m.Success)
                return null;
            return long.TryParse(m.Groups[1].Value, out var v) ? v : (long?)null;
        }

        private readonly record struct CounterSpec(long Start, long Increment, long Padding);
    }
}
