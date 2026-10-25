using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using Xunit;

namespace EverythingToolbar.FuzzySearch.Tests
{
    /// <summary>
    /// Local bookmark store: add/replace-by-name (case-insensitive dedup), remove, alphabetical
    /// (case-insensitive) ordering, bookmarks.json round-trip via an injected temp path, and
    /// corrupt files silently reading as absent — mirroring the SearchTabStore guarantees.
    /// </summary>
    public sealed class SearchBookmarkStoreTests : IDisposable
    {
        private readonly string _bookmarksPath = Path.Combine(
            Path.GetTempPath(),
            "etb-bookmarks-" + Guid.NewGuid().ToString("N") + ".json"
        );

        public void Dispose()
        {
            try
            {
                File.Delete(_bookmarksPath);
            }
            catch (IOException)
            {
                // Temp cleanup is best-effort.
            }
        }

        private SearchBookmarkStore CreateStore()
        {
            return new SearchBookmarkStore(_bookmarksPath);
        }

        [Fact]
        public void Add_IgnoresEmptyAndWhitespaceNames()
        {
            var store = CreateStore();

            store.Add("", "query", null, SortBy.Name, false);
            store.Add("   ", "query", null, SortBy.Name, false);

            Assert.Empty(store.Bookmarks);
        }

        [Fact]
        public void Add_TrimsName()
        {
            var store = CreateStore();

            store.Add("  notes  ", "notes ext:txt", null, SortBy.Name, false);

            var bookmark = Assert.Single(store.Bookmarks);
            Assert.Equal("notes", bookmark.Name);
        }

        [Fact]
        public void Add_ReplacesExistingNameCaseInsensitively()
        {
            var store = CreateStore();
            store.Add("Notes", "notes", "Text", SortBy.Name, false);

            store.Add("NOTES", "notes updated", null, SortBy.Size, true);

            var bookmark = Assert.Single(store.Bookmarks);
            Assert.Equal("notes updated", bookmark.SearchTerm);
            Assert.Null(bookmark.FilterName);
            Assert.Equal(SortBy.Size, bookmark.SortBy);
            Assert.True(bookmark.IsSortDescending);
        }

        [Fact]
        public void Remove_RemovesByNameCaseInsensitively_AndReturnsFalseWhenAbsent()
        {
            var store = CreateStore();
            store.Add("Notes", "notes", null, SortBy.Name, false);

            Assert.True(store.Remove("NOTES"));
            Assert.Empty(store.Bookmarks);
            Assert.False(store.Remove("Notes"));
        }

        [Fact]
        public void Bookmarks_AreSortedAlphabeticallyCaseInsensitive()
        {
            var store = CreateStore();

            store.Add("zeta", "z", null, SortBy.Name, false);
            store.Add("Alpha", "a", null, SortBy.Name, false);
            store.Add("beta", "b", null, SortBy.Name, false);
            store.Add("delta", "d", null, SortBy.Name, false);

            Assert.Equal(["Alpha", "beta", "delta", "zeta"], store.Bookmarks.Select(b => b.Name).ToList());
        }

        [Fact]
        public void RoundTrip_PreservesBookmarksAcrossInstances()
        {
            var store = CreateStore();
            store.Add("Notes", "notes ext:txt", "Text", SortBy.DateModified, true);
            store.Add("All Music", " *.mp3", null, SortBy.Name, false);

            var reloaded = CreateStore();
            reloaded.Load();

            Assert.Equal(2, reloaded.Bookmarks.Count);
            var music = reloaded.Bookmarks[0];
            Assert.Equal("All Music", music.Name);
            Assert.Null(music.FilterName);
            var notes = reloaded.Bookmarks[1];
            Assert.Equal("Notes", notes.Name);
            Assert.Equal("notes ext:txt", notes.SearchTerm);
            Assert.Equal("Text", notes.FilterName);
            Assert.Equal(SortBy.DateModified, notes.SortBy);
            Assert.True(notes.IsSortDescending);
        }

        [Fact]
        public void SavedFile_IsVersionedJson()
        {
            var store = CreateStore();
            store.Add("Notes", "notes", null, SortBy.Name, false);

            var json = File.ReadAllText(_bookmarksPath);
            var document = JsonDocument.Parse(json);

            Assert.Equal(1, document.RootElement.GetProperty("Version").GetInt32());
            Assert.Equal(1, document.RootElement.GetProperty("Bookmarks").GetArrayLength());
        }

        [Fact]
        public void Load_CorruptFile_IsIgnored()
        {
            File.WriteAllText(_bookmarksPath, "{ this is not json");

            var store = CreateStore();
            store.Load();

            Assert.Empty(store.Bookmarks);
        }

        [Fact]
        public void Load_WrongSchemaVersion_IsIgnored()
        {
            var store = CreateStore();
            store.Add("Notes", "notes", null, SortBy.Name, false);
            File.WriteAllText(_bookmarksPath, """{"Version":999,"Bookmarks":[]}""");

            var reloaded = CreateStore();
            reloaded.Load();

            Assert.Empty(reloaded.Bookmarks);
        }

        [Fact]
        public void Load_MissingFile_YieldsEmptyStore()
        {
            var store = CreateStore();
            store.Load();

            Assert.Empty(store.Bookmarks);
        }

        [Fact]
        public void Load_SkipsEntriesWithBlankNames()
        {
            File.WriteAllText(
                _bookmarksPath,
                """{"Version":1,"Bookmarks":[{"Name":"  ","SearchTerm":"x","FilterName":null,"SortBy":0,"IsSortDescending":false},{"Name":"Notes","SearchTerm":"notes","FilterName":null,"SortBy":0,"IsSortDescending":false}]}"""
            );

            var store = CreateStore();
            store.Load();

            var bookmark = Assert.Single(store.Bookmarks);
            Assert.Equal("Notes", bookmark.Name);
        }
    }
}
