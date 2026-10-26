using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using EverythingToolbar.App.Helpers;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.App.Search
{
    /// <summary>
    /// Local search bookmarks backing the expanded window's Bookmarks menu (the Everything 3 SDK
    /// has no bookmark API, so the store is fully local). Persistence goes to
    /// %APPDATA%\EverythingToolbar\bookmarks.json and mirrors SearchTabStore exactly: versioned
    /// JSON, atomic tmp+move writes, corrupt/absent/wrong-version files silently read as empty,
    /// and an injectable file path for tests. Bookmarks are kept sorted alphabetically
    /// (case-insensitive); Add replaces an existing bookmark with the same name
    /// (case-insensitive) instead of duplicating it.
    /// </summary>
    public sealed class SearchBookmarkStore
    {
        public const int SchemaVersion = 1;

        private readonly string _filePath;
        private bool _loaded;

        public ObservableCollection<SearchBookmark> Bookmarks { get; } = new();

        public SearchBookmarkStore()
            : this(Path.Combine(ConfigPaths.GetConfigDirectory(), "bookmarks.json")) { }

        public SearchBookmarkStore(string filePath)
        {
            _filePath = filePath;
        }

        /// <summary>
        /// Loads bookmarks.json once per app run (first expanded-window show). Missing, corrupt or
        /// schema-incompatible files yield an empty bookmark list.
        /// </summary>
        public void Load()
        {
            if (_loaded)
                return;
            _loaded = true;

            var dto = SearchBookmarkFile.Load(_filePath);
            if (dto?.Bookmarks is null)
                return;

            foreach (var bookmarkDto in dto.Bookmarks)
            {
                if (bookmarkDto is null || string.IsNullOrWhiteSpace(bookmarkDto.Name))
                    continue;

                InsertSorted(
                    new SearchBookmark(
                        bookmarkDto.Name.Trim(),
                        bookmarkDto.SearchTerm ?? "",
                        bookmarkDto.FilterName,
                        bookmarkDto.SortBy,
                        bookmarkDto.IsSortDescending
                    )
                );
            }
        }

        /// <summary>Persists bookmarks.json atomically.</summary>
        public void Save()
        {
            SearchBookmarkFile.Save(
                _filePath,
                new SearchBookmarkFile.BookmarksFileDto(
                    SchemaVersion,
                    Bookmarks
                        .Select(b => new SearchBookmarkFile.BookmarkDto(
                            b.Name,
                            b.SearchTerm,
                            b.FilterName,
                            b.SortBy,
                            b.IsSortDescending
                        ))
                        .ToList()
                )
            );
        }

        /// <summary>
        /// Adds a bookmark (name must be non-empty after trimming). An existing bookmark with the
        /// same name (case-insensitive) is replaced. The collection stays alphabetically sorted
        /// and the store is saved.
        /// </summary>
        public void Add(string name, string searchTerm, string? filterName, SortBy sortBy, bool isSortDescending)
        {
            var trimmedName = (name ?? "").Trim();
            if (trimmedName.Length == 0)
                return;

            var existing = Find(trimmedName);
            if (existing is not null)
                Bookmarks.Remove(existing);

            InsertSorted(new SearchBookmark(trimmedName, searchTerm ?? "", filterName, sortBy, isSortDescending));
            Save();
        }

        /// <summary>Removes the bookmark with the given name (case-insensitive). False if absent.</summary>
        public bool Remove(string name)
        {
            var existing = Find((name ?? "").Trim());
            if (existing is null)
                return false;

            Bookmarks.Remove(existing);
            Save();
            return true;
        }

        private SearchBookmark? Find(string name) =>
            Bookmarks.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

        private void InsertSorted(SearchBookmark bookmark)
        {
            var index = 0;
            while (
                index < Bookmarks.Count
                && string.Compare(Bookmarks[index].Name, bookmark.Name, StringComparison.OrdinalIgnoreCase) < 0
            )
            {
                index++;
            }
            Bookmarks.Insert(index, bookmark);
        }
    }

    /// <summary>Versioned bookmarks.json layout with atomic tmp+move writes; any read failure reads as absent.</summary>
    public static class SearchBookmarkFile
    {
        public sealed record BookmarksFileDto(int Version, List<BookmarkDto> Bookmarks);

        public sealed record BookmarkDto(
            string Name,
            string SearchTerm,
            string? FilterName,
            SortBy SortBy,
            bool IsSortDescending
        );

        /// <summary>Null when the file is absent, unreadable, corrupt or schema-incompatible.</summary>
        public static BookmarksFileDto? Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var dto = JsonSerializer.Deserialize<BookmarksFileDto>(File.ReadAllText(path));
                if (dto is null || dto.Version != SearchBookmarkStore.SchemaVersion || dto.Bookmarks is null)
                    return null;

                return dto;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Save(string path, BookmarksFileDto dto)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(dto));
                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp))
                        File.Delete(tmp);
                }
                catch (Exception)
                {
                    // Best-effort temp cleanup; the write itself has already resolved.
                }
            }
        }
    }
}
