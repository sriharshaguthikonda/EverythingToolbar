using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EverythingToolbar.FuzzySearch
{
    /// <summary>
    /// Persisted spelling vocabulary cache: normalized token/compound frequencies and display
    /// casing only — never paths, never a second filesystem index. Atomic writes; missing,
    /// corrupt, schema-incompatible or instance-mismatched caches simply read as absent so the
    /// caller rebuilds from Everything.
    /// </summary>
    public static class SpellingVocabularyCache
    {
        public const int SchemaVersion = 1;
        public const int TokenizerVersion = 1;

        public sealed record Entry(string N, string D, long F);

        public sealed record VocabularySnapshot(
            int Schema,
            int TokenizerVersion,
            string Instance,
            List<Entry> Words,
            List<Entry> Compounds
        );

        public static string GetCachePath(string directory, string instance)
        {
            var safe = string.IsNullOrWhiteSpace(instance)
                ? "default"
                : string.Join(
                    "_",
                    instance.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)
                );
            return Path.Combine(directory, $"spelling-vocabulary-{safe}-v{SchemaVersion}.json");
        }

        /// <summary>Null when the cache is absent, unreadable, schema-incompatible, or for another instance.</summary>
        public static VocabularySnapshot? Load(string path, string expectedInstance)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var snapshot = JsonSerializer.Deserialize<VocabularySnapshot>(File.ReadAllText(path));
                if (
                    snapshot is null
                    || snapshot.Schema != SchemaVersion
                    || snapshot.TokenizerVersion != TokenizerVersion
                    || !string.Equals(snapshot.Instance, expectedInstance, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return null;
                }

                return snapshot;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static VocabularySnapshot CreateSnapshot(string instance, TokenVocabulary vocabulary)
        {
            return new VocabularySnapshot(
                SchemaVersion,
                TokenizerVersion,
                instance,
                vocabulary.WordsByFrequency().Select(e => new Entry(e.Normalized, e.Display, e.Frequency)).ToList(),
                vocabulary.CompoundsByFrequency().Select(e => new Entry(e.Normalized, e.Display, e.Frequency)).ToList()
            );
        }

        public static void Save(string path, VocabularySnapshot snapshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot));
                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp))
                    {
                        File.Delete(tmp);
                    }
                }
                catch (Exception)
                {
                    // Best-effort temp cleanup; the cache write itself has already resolved.
                }
            }
        }
    }
}
