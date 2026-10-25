using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EverythingToolbar.App.Helpers;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.App.Search
{
    /// <summary>
    /// Browser-style tabs over the single shared <see cref="SearchState"/>. There is still exactly
    /// one search pipeline: switching tabs snapshots the outgoing tab's state out of SearchState
    /// and restores the incoming tab's state into it, and the search session rebuilds through its
    /// existing PropertyChanged path. Persistence goes to %APPDATA%\EverythingToolbar\tabs.json
    /// (versioned, atomic tmp+move, corrupt file silently ignored) and is written only on an
    /// explicit <see cref="Save"/> call (made on expanded-window close) — no debounce.
    ///
    /// Snapshot-on-switch tradeoff: only the query text is mirrored live (so headers stay current
    /// while typing); filter, sort and match flags are captured the moment the user leaves a tab.
    /// If the app were killed mid-typing those secondary flags would persist from the last switch,
    /// which is acceptable for v1.
    /// </summary>
    public sealed partial class SearchTabManager : ObservableObject
    {
        public const int SchemaVersion = 1;

        private readonly SearchState _searchState;
        private readonly FilterProvider? _filterProvider;
        private readonly string _filePath;
        private bool _loaded;
        private bool _restoring;

        public ObservableCollection<SearchTab> Tabs { get; } = new();

        [ObservableProperty]
        private int _activeIndex;

        [ObservableProperty]
        private bool _isTabStripVisible;

        public SearchTabManager(SearchState searchState, FilterProvider? filterProvider)
            : this(searchState, filterProvider, DefaultPath()) { }

        public SearchTabManager(SearchState searchState, FilterProvider? filterProvider, string filePath)
        {
            _searchState = searchState;
            _filterProvider = filterProvider;
            _filePath = filePath;

            Tabs.CollectionChanged += OnTabsChanged;
            _searchState.PropertyChanged += OnSearchStateChanged;
        }

        private static string DefaultPath() => Path.Combine(ConfigPaths.GetConfigDirectory(), "tabs.json");

        /// <summary>Appends an empty tab and switches to it; the outgoing tab is snapshotted first.</summary>
        public SearchTab NewTab()
        {
            SnapshotActive();
            var tab = new SearchTab();
            Tabs.Add(tab);
            // New tabs start with an empty query and inherit whatever filter is currently active;
            // the filter is only captured into the tab once the user leaves it.
            Activate(Tabs.Count - 1, restoreTab: tab);
            return tab;
        }

        /// <summary>
        /// Closes a tab. Closing the last remaining tab never empties the list — it clears that tab
        /// to an empty new-tab state instead (Explorer/Everything behaviour: at least one tab).
        /// </summary>
        public void CloseTab(int index)
        {
            if (index < 0 || index >= Tabs.Count)
                return;

            if (Tabs.Count == 1)
            {
                var empty = new SearchTab();
                Tabs[0] = empty;
                if (ActiveIndex == 0)
                    Activate(0, restoreTab: empty);
                return;
            }

            var closingActive = index == ActiveIndex;
            Tabs.RemoveAt(index);

            var newIndex =
                closingActive ? Math.Min(index, Tabs.Count - 1)
                : index < ActiveIndex ? ActiveIndex - 1
                : ActiveIndex;
            ActiveIndex = newIndex;

            if (closingActive)
                Activate(newIndex, restoreTab: Tabs[newIndex]);
        }

        /// <summary>
        /// Switches tabs: snapshots the current SearchState into the outgoing tab, then restores
        /// the incoming tab into SearchState so the session rebuilds via PropertyChanged.
        /// </summary>
        public void SwitchTo(int index)
        {
            if (index < 0 || index >= Tabs.Count || index == ActiveIndex)
                return;

            SnapshotActive();
            Activate(index, restoreTab: Tabs[index]);
        }

        /// <summary>Cycles to the next tab (wrapping around), like Ctrl+Tab in browsers.</summary>
        public void CycleNext()
        {
            if (Tabs.Count > 1)
                SwitchTo((ActiveIndex + 1) % Tabs.Count);
        }

        /// <summary>
        /// Replaces the whole tab set (used by session restore). Takes ownership of the given
        /// tab instances — callers pass fresh deep copies (never live tabs or a stored session's
        /// tabs) so restoring stays non-aliasing. Never yields an empty tab set (an empty input
        /// becomes one empty tab, matching <see cref="CloseTab"/>); the active index is clamped
        /// and the incoming tab is restored into SearchState. Persistence is the caller's job
        /// via <see cref="Save"/> afterwards.
        /// </summary>
        public void ReplaceAll(IEnumerable<SearchTab> tabs, int activeIndex)
        {
            Tabs.Clear();
            foreach (var tab in tabs ?? Array.Empty<SearchTab>())
                Tabs.Add(tab);

            if (Tabs.Count == 0)
                Tabs.Add(new SearchTab());

            var index = Math.Clamp(activeIndex, 0, Tabs.Count - 1);
            Activate(index, restoreTab: Tabs[index]);
        }

        /// <summary>
        /// Loads tabs.json once per app run (first expanded-window show). Missing, corrupt or
        /// schema-incompatible files yield a single empty tab.
        /// </summary>
        public void Load()
        {
            if (_loaded)
                return;
            _loaded = true;

            var dto = SearchTabStore.Load(_filePath);
            if (dto is null || dto.Tabs.Count == 0)
            {
                Tabs.Add(new SearchTab());
                Activate(0, restoreTab: Tabs[0]);
                return;
            }

            foreach (var tabDto in dto.Tabs)
            {
                Tabs.Add(
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

            var active = Math.Clamp(dto.ActiveIndex, 0, Tabs.Count - 1);
            Activate(active, restoreTab: Tabs[active]);
        }

        /// <summary>Snapshots the active SearchState into its tab, then persists tabs.json atomically.</summary>
        public void Save()
        {
            SnapshotActive();

            var dto = new SearchTabStore.TabsFileDto(
                SchemaVersion,
                ActiveIndex,
                Tabs.Select(t => new SearchTabStore.TabDto(
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
            );
            SearchTabStore.Save(_filePath, dto);
        }

        private void Activate(int index, SearchTab restoreTab)
        {
            ActiveIndex = index;
            for (var i = 0; i < Tabs.Count; i++)
                Tabs[i].IsActive = i == index;

            _restoring = true;
            try
            {
                _searchState.RestoreSnapshot(restoreTab);
            }
            finally
            {
                _restoring = false;
            }
        }

        private void SnapshotActive()
        {
            if (ActiveIndex >= 0 && ActiveIndex < Tabs.Count)
                _searchState.CaptureSnapshotInto(Tabs[ActiveIndex]);
        }

        private Filter? ResolveFilter(string? name)
        {
            if (string.IsNullOrEmpty(name) || _filterProvider is null)
                return null;

            return _filterProvider.Filters.FirstOrDefault(f => f.Name == name);
        }

        private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            IsTabStripVisible = Tabs.Count > 1;
        }

        // Live mirror of the query text only (see class doc): keeps tab headers current while the
        // user types. Everything else is snapshot-on-switch. Suppressed while restoring so the
        // restore's own SearchTerm write doesn't bounce straight back into the same tab.
        private void OnSearchStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_restoring || e.PropertyName != nameof(SearchState.SearchTerm))
                return;

            if (ActiveIndex >= 0 && ActiveIndex < Tabs.Count)
                Tabs[ActiveIndex].SearchTerm = _searchState.SearchTerm;
        }
    }

    /// <summary>Versioned tabs.json layout with atomic tmp+move writes; any read failure reads as absent.</summary>
    public static class SearchTabStore
    {
        public sealed record TabsFileDto(int Version, int ActiveIndex, List<TabDto> Tabs);

        public sealed record TabDto(
            Guid Id,
            string SearchTerm,
            string? FilterName,
            SortBy SortBy,
            bool IsSortDescending,
            bool IsMatchCase,
            bool IsMatchPath,
            bool IsMatchWholeWord,
            bool IsMatchDiacritics,
            bool IsMatchPrefix,
            bool IsMatchSuffix,
            bool IsIgnorePunctuation,
            bool IsIgnoreWhitespace,
            bool IsRegExEnabled,
            bool IsHideResultOmissions
        );

        /// <summary>Null when the file is absent, unreadable, corrupt or schema-incompatible.</summary>
        public static TabsFileDto? Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var dto = JsonSerializer.Deserialize<TabsFileDto>(File.ReadAllText(path));
                if (dto is null || dto.Version != SearchTabManager.SchemaVersion || dto.Tabs is null)
                    return null;

                return dto;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Save(string path, TabsFileDto dto)
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
