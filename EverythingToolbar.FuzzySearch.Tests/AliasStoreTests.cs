using System.IO;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    public class AliasStoreTests
    {
        [Fact]
        public void TryGetCanonical_ResolvesAlias()
        {
            var store = new AliasStore(new[] { new AliasGroup("repomaps", new[] { "repo-map", "repo_maps" }) });

            Assert.Equal("repomaps", store.TryGetCanonical("repo-map"));
            Assert.Equal("repomaps", store.TryGetCanonical("repo_maps"));
        }

        [Theory]
        [InlineData("REPO-MAP")]
        [InlineData("  repo-map  ")]
        [InlineData("Repo-Map")]
        public void TryGetCanonical_NormalizesCaseAndWhitespace(string alias)
        {
            var store = new AliasStore(new[] { new AliasGroup("repomaps", new[] { "repo-map" }) });

            Assert.Equal("repomaps", store.TryGetCanonical(alias));
        }

        [Fact]
        public void TryGetCanonical_CanonicalItself_ReturnsNull()
        {
            var store = new AliasStore(new[] { new AliasGroup("color", new[] { "colour" }) });

            Assert.Null(store.TryGetCanonical("color"));
        }

        [Fact]
        public void TryGetCanonical_UnknownTerm_ReturnsNull()
        {
            Assert.Null(AliasStore.Empty.TryGetCanonical("neurosicence"));
        }

        [Fact]
        public void TryGetCanonical_MultiWordAlias_NormalizesInnerWhitespace()
        {
            var store = new AliasStore(new[] { new AliasGroup("repomaps", new[] { "repo   map" }) });

            Assert.Equal("repomaps", store.TryGetCanonical("repo map"));
        }

        [Fact]
        public void Load_MissingFile_ReturnsEmpty()
        {
            Assert.Equal(AliasStore.Empty, AliasStore.Load("Z:\\does-not-exist\\aliases.json"));
        }

        [Fact]
        public void Load_MalformedJson_ReturnsEmpty()
        {
            var path = Path.Combine(Path.GetTempPath(), $"aliases-bad-{Path.GetRandomFileName()}.json");
            File.WriteAllText(path, "{ not json");
            try
            {
                Assert.Equal(AliasStore.Empty, AliasStore.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void SaveThenLoad_RoundTrips()
        {
            var path = Path.Combine(Path.GetTempPath(), $"aliases-{Path.GetRandomFileName()}.json");
            try
            {
                var store = new AliasStore(
                    new[]
                    {
                        new AliasGroup("repomaps", new[] { "repo-map", "repo map" }),
                        new AliasGroup("color", new[] { "colour" }),
                    }
                );
                store.Save(path);

                var loaded = AliasStore.Load(path);
                Assert.Equal("repomaps", loaded.TryGetCanonical("repo-map"));
                Assert.Equal("repomaps", loaded.TryGetCanonical("repo map"));
                Assert.Equal("color", loaded.TryGetCanonical("colour"));
                Assert.Null(loaded.TryGetCanonical("repomaps"));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
