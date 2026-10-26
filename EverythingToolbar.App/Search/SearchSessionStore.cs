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
    /// Named sessions backing the expanded window's File &gt; Sessions menu: each session is a
    /// saved tab set (see <see cref="SearchSessionSnapshot"/>). Persistence goes to
    /// %APPDATA%\EverythingToolbar\sessions.json and mirrors SearchTabStore/SearchBookmarkFile:
    /// versioned JSON, atomic tmp+move writes, corrupt/absent/wrong-version files silently read as
    /// empty, and an injectable file path for tests. Sessions are name-keyed like bookmarks:
    /// saving replaces an existing session with the same name (case-insensitive) instead of
    /// duplicating, empty names are rejected, and the in-memory list stays alphabetically sorted
    /// (case-insensitive). Unlike SearchBookmarkStore, Save(name, snapshot)/Delete save the file
    /// immediately from the same call (the bookmark store's Add/Remove pattern — no separate
    /// explicit Save step is exposed for sessions).
    /// </summary>
    public sealed class SearchSessionStore
    {
        public const int SchemaVersion = 1;

        private readonly string _filePath;
        private readonly FilterProvider? _filterProvider;
        private bool _loaded;

        public ObservableCollection<SearchSessionSnapshot> Sessions { get; } = new();

        public SearchSessionStore()
            : this(Path.Combine(ConfigPaths.GetConfigDirectory(), "sessions.json")) { }

        public SearchSessionStore(string filePath)
            : this(filePath, filterProvider: null) { }

        public SearchSessionStore(string filePath, FilterProvider? filterProvider)
        {
            _filePath = filePath;
            _filterProvider = filterProvider;
        }

        /// <summary>
        /// Loads sessions.json once per app run (first expanded-window show). Missing, corrupt or
        /// schema-incompatible files yield an empty session list; entries with empty names and
        /// duplicate names (case-insensitive) are skipped.
        /// </summary>
        public void Load()
        {
            if (_loaded)
                return;
            _loaded = true;

            var dto = SearchSessionFile.Load(_filePath);
            if (dto?.Sessions is null)
                return;

            foreach (var sessionDto in dto.Sessions)
            {
                if (sessionDto is null || string.IsNullOrWhiteSpace(sessionDto.Name))
                    continue;

                if (Find(sessionDto.Name) is not null)
                    continue;

                var tabs = new List<SearchTab>();
                if (sessionDto.Tabs is not null)
                {
                    foreach (var tabDto in sessionDto.Tabs)
                    {
                        tabs.Add(
                            new SearchTab
                            {
                                Id = tabDto.Id,
                                SearchTerm = tabDto.SearchTerm ?? "",
                                Filter = ResolveFilter(tabDto.FilterName),
                                SortBy = tabDto.SortBy,
                                IsSortDescending = tabDto.IsSortDescending,
                                IsMatchCase = tabDto.IsMatchCase,
                                IsMatchPath = tabDto.IsMatchPath,
                                IsMatchWholeWord = tabDto.IsMatchWholeWord,
                                IsMatchDiacritics = tabDto.IsMatchDiacritics,
                                IsMatchPrefix = tabDto.IsMatchPrefix,
                                IsMatchSuffix = tabDto.IsMatchSuffix,
                                IsIgnorePunctuation = tabDto.IsIgnorePunctuation,
                                IsIgnoreWhitespace = tabDto.IsIgnoreWhitespace,
                                IsRegExEnabled = tabDto.IsRegExEnabled,
                                IsHideResultOmissions = tabDto.IsHideResultOmissions,
                            }
                        );
                    }
                }

                InsertSorted(new SearchSessionSnapshot(sessionDto.Name, tabs, sessionDto.ActiveIndex));
            }
        }

        /// <summary>
        /// Saves a session under the given name (empty names are ignored, mirroring the bookmark
        /// store's rejection of empty names). An existing session with the same name
        /// (case-insensitive) is replaced. The list stays alphabetically sorted and sessions.json
        /// is persisted atomically.
        /// </summary>
        public void Save(string name, SearchSessionSnapshot snapshot)
        {
            var trimmedName = (name ?? "").Trim();
            if (trimmedName.Length == 0 || snapshot is null)
                return;

            var stored = new SearchSessionSnapshot(trimmedName, snapshot.Tabs, snapshot.ActiveIndex);

            var existing = Find(trimmedName);
            if (existing is not null)
                Sessions.Remove(existing);

            InsertSorted(stored);
            Persist();
        }

        /// <summary>Removes the session with the given name (case-insensitive). False if absent.</summary>
        public bool Delete(string name)
        {
            var existing = Find((name ?? "").Trim());
            if (existing is null)
                return false;

            Sessions.Remove(existing);
            Persist();
            return true;
        }

        private Filter? ResolveFilter(string? name)
        {
            if (string.IsNullOrEmpty(name) || _filterProvider is null)
                return null;

            return _filterProvider.Filters.FirstOrDefault(f => f.Name == name);
        }

        private SearchSessionSnapshot? Find(string name) =>
            Sessions.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        private void InsertSorted(SearchSessionSnapshot session)
        {
            var index = 0;
            while (
                index < Sessions.Count
                && string.Compare(Sessions[index].Name, session.Name, StringComparison.OrdinalIgnoreCase) < 0
            )
            {
                index++;
            }
            Sessions.Insert(index, session);
        }

        private void Persist()
        {
            SearchSessionFile.Save(
                _filePath,
                new SearchSessionFile.SessionsFileDto(
                    SchemaVersion,
                    Sessions
                        .Select(s => new SearchSessionFile.SessionDto(
                            s.Name,
                            s.ActiveIndex,
                            s.Tabs.Select(t => new SearchTabStore.TabDto(
                                    t.Id,
                                    t.SearchTerm,
                                    t.Filter?.Name,
                                    t.SortBy,
                                    t.IsSortDescending,
                                    t.IsMatchCase,
                                    t.IsMatchPath,
                                    t.IsMatchWholeWord,
                                    t.IsMatchDiacritics,
                                    t.IsMatchPrefix,
                                    t.IsMatchSuffix,
                                    t.IsIgnorePunctuation,
                                    t.IsIgnoreWhitespace,
                                    t.IsRegExEnabled,
                                    t.IsHideResultOmissions
                                ))
                                .ToList()
                        ))
                        .ToList()
                )
            );
        }
    }

    /// <summary>Versioned sessions.json layout with atomic tmp+move writes; any read failure reads as absent.</summary>
    public static class SearchSessionFile
    {
        public sealed record SessionsFileDto(int Version, List<SessionDto> Sessions);

        public sealed record SessionDto(string Name, int ActiveIndex, List<SearchTabStore.TabDto> Tabs);

        /// <summary>Null when the file is absent, unreadable, corrupt or schema-incompatible.</summary>
        public static SessionsFileDto? Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var dto = JsonSerializer.Deserialize<SessionsFileDto>(File.ReadAllText(path));
                if (dto is null || dto.Version != SearchSessionStore.SchemaVersion || dto.Sessions is null)
                    return null;

                return dto;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Save(string path, SessionsFileDto dto)
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
