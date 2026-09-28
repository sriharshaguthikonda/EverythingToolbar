using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EverythingToolbar.FuzzySearch
{
    /// <summary>One preferred-spelling group: a canonical form plus the alternatives that map to it.</summary>
    public sealed record AliasGroup(string Canonical, IReadOnlyList<string> Aliases);

    /// <summary>
    /// Deterministic alias lookup: alias -> canonical. The canonical form typed exactly never
    /// expands (raw wins). Storage is a small inspectable JSON file, not a database.
    /// </summary>
    public sealed class AliasStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly Dictionary<string, string> _canonicalByAlias;

        public static AliasStore Empty { get; } = new(Array.Empty<AliasGroup>());

        public AliasStore(IEnumerable<AliasGroup> groups)
        {
            _canonicalByAlias = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var canonical = Normalize(group.Canonical);
                if (canonical.Length == 0)
                {
                    continue;
                }

                foreach (var alias in group.Aliases)
                {
                    var normalized = Normalize(alias);
                    if (normalized.Length > 0 && normalized != canonical)
                    {
                        _canonicalByAlias[normalized] = canonical;
                    }
                }
            }
        }

        /// <summary>Returns the canonical form when the term is a known alias; null otherwise.</summary>
        public string? TryGetCanonical(string term)
        {
            return _canonicalByAlias.TryGetValue(Normalize(term), out var canonical) ? canonical : null;
        }

        internal static string Normalize(string text)
        {
            return string.Join(
                ' ',
                text.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            );
        }

        public static AliasStore Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return Empty;
                }

                var dto = JsonSerializer.Deserialize<AliasFileDto>(File.ReadAllText(path));
                if (dto?.Groups is null)
                {
                    return Empty;
                }

                return new AliasStore(dto.Groups.Select(g => new AliasGroup(g.Canonical, g.Aliases)));
            }
            catch (Exception)
            {
                // A malformed alias file must never break searching.
                return Empty;
            }
        }

        public void Save(string path)
        {
            var dto = new AliasFileDto(
                _canonicalByAlias
                    .GroupBy(kvp => kvp.Value)
                    .Select(g => new AliasGroupDto(Canonical: g.Key, Aliases: g.Select(kvp => kvp.Key).ToList()))
                    .ToList()
            );
            File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
        }

        private sealed record AliasFileDto(List<AliasGroupDto> Groups);

        private sealed record AliasGroupDto(string Canonical, List<string> Aliases);
    }
}
