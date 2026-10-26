using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Search;

namespace EverythingToolbar.App.Search
{
    public sealed partial class SearchState : ObservableObject
    {
        [ObservableProperty]
        private string _searchTerm = "";

        public SortBy SortBy => (SortBy)_settings.SortBy;
        public SortBy EffectiveSortBy => _useSettingsSortKey ? SortBy : Filter.SortBy ?? SortBy;
        public bool IsSortDescending => _settings.IsSortDescending;
        public bool EffectiveIsSortDescending =>
            _useSettingsSortDirection ? IsSortDescending
            : Filter.SortBy is not null ? Filter.SortDescending
            : IsSortDescending;

        // Per-tab match flags: SearchState owns these (carried through tab snapshots). The
        // settings ini keeps only the user's defaults, read at construction and re-applied when
        // settings change — the compact popup (SearchBox/SettingsControl) and the expanded
        // window's Search menu bind/toggle ISettings directly, so their changes flow IN here via
        // OnSettingsChanged. Setters never write back to settings, so tab switches
        // (RestoreSnapshot) stay side-effect-free with respect to the persisted ini.
        private bool _isMatchCase;
        private bool _isMatchPath;
        private bool _isMatchWholeWord;
        private bool _isMatchDiacritics;
        private bool _isMatchPrefix;
        private bool _isMatchSuffix;
        private bool _isIgnorePunctuation;
        private bool _isIgnoreWhitespace;
        private bool _isRegExEnabled;
        private bool _isHideResultOmissions;

        public bool IsMatchCase
        {
            get => _isMatchCase;
            private set => SetProperty(ref _isMatchCase, value);
        }

        public bool IsMatchPath
        {
            get => _isMatchPath;
            private set => SetProperty(ref _isMatchPath, value);
        }

        public bool IsMatchWholeWord
        {
            get => _isMatchWholeWord;
            private set => SetProperty(ref _isMatchWholeWord, value);
        }

        public bool IsMatchDiacritics
        {
            get => _isMatchDiacritics;
            private set => SetProperty(ref _isMatchDiacritics, value);
        }

        public bool IsMatchPrefix
        {
            get => _isMatchPrefix;
            private set => SetProperty(ref _isMatchPrefix, value);
        }

        public bool IsMatchSuffix
        {
            get => _isMatchSuffix;
            private set => SetProperty(ref _isMatchSuffix, value);
        }

        public bool IsIgnorePunctuation
        {
            get => _isIgnorePunctuation;
            private set => SetProperty(ref _isIgnorePunctuation, value);
        }

        public bool IsIgnoreWhitespace
        {
            get => _isIgnoreWhitespace;
            private set => SetProperty(ref _isIgnoreWhitespace, value);
        }

        public bool IsRegExEnabled
        {
            get => _isRegExEnabled;
            private set => SetProperty(ref _isRegExEnabled, value);
        }

        public bool IsHideResultOmissions
        {
            get => _isHideResultOmissions;
            private set => SetProperty(ref _isHideResultOmissions, value);
        }

        private bool _useSettingsSortKey;
        private bool _useSettingsSortDirection;

        /// <summary>Everything 1.5 caps multi-sort at a primary plus two additional sorts.</summary>
        public const int MaxSecondarySorts = 2;

        // Session-only secondary/tertiary sorts (applied after the primary sort). Deliberately
        // NOT persisted to settings (settings ini is schema v1: a single primary sort); they live
        // only in tab snapshots for the current session.
        private List<(SortBy SortBy, bool Descending)>? _secondarySorts;

        public IReadOnlyList<(SortBy SortBy, bool Descending)>? SecondarySorts => _secondarySorts;

        private Filter _currentFilter;
        public Filter Filter
        {
            get => _currentFilter;
            set
            {
                if (SetProperty(ref _currentFilter, value))
                {
                    _settings.LastFilter = value.Name;
                    _useSettingsSortKey = false;
                    _useSettingsSortDirection = false;
                }
            }
        }

        private readonly SearchHistory _history;
        private readonly FilterProvider _filterProvider;
        private readonly ISettings _settings;
        private readonly IEverythingClient _everythingClient;

        public SearchState(
            SearchHistory history,
            FilterProvider filterProvider,
            ISettings settings,
            IEverythingClient everythingClient
        )
        {
            _history = history;
            _filterProvider = filterProvider;
            _settings = settings;
            _everythingClient = everythingClient;

            _currentFilter = _filterProvider.GetInitialFilter();

            ApplyFlagsFrom(_settings);

            _settings.PropertyChanged += OnSettingsChanged;
        }

        public void Reset()
        {
            if (_settings.IsEnableHistory)
                _history.AddToHistory(SearchTerm);
            else
                SearchTerm = "";

            Filter = _filterProvider.GetInitialFilter();
        }

        public string GetPreviousSearchTerm() => _history.GetPreviousItem();

        public string GetNextSearchTerm() => _history.GetNextItem();

        public void ClearHistory() => _history.ClearHistory();

        public void CycleFilters(int offset = 1)
        {
            var filterCount = _filterProvider.Filters.Count;
            if (filterCount == 0)
                return;

            var currentIndex = _filterProvider.Filters.IndexOf(Filter);
            var newIndex = (currentIndex + offset + filterCount) % filterCount;
            Filter = _filterProvider.Filters[newIndex];
        }

        public void SelectFilterFromIndex(int index)
        {
            if (index < 0 || index >= _filterProvider.Filters.Count)
                return;

            Filter = _filterProvider.Filters[index];
        }

        public void UseSettingsSortKey() => _useSettingsSortKey = true;

        public void UseSettingsSortDirection() => _useSettingsSortDirection = true;

        /// <summary>
        /// Applies an explicit sort (e.g. from a results column header click) that overrides the
        /// filter defaults. Raises change notifications so the search session rebuilds.
        /// </summary>
        public void SetSort(SortBy sortBy, bool descending)
        {
            UseSettingsSortKey();
            UseSettingsSortDirection();
            _settings.SortBy = (int)sortBy;
            _settings.IsSortDescending = descending;
            OnPropertyChanged(nameof(SortBy));
            OnPropertyChanged(nameof(IsSortDescending));
            OnPropertyChanged(nameof(EffectiveSortBy));
            OnPropertyChanged(nameof(EffectiveIsSortDescending));
        }

        /// <summary>Clears the explicit sort override and falls back to the filter/settings defaults.</summary>
        public void ResetSort()
        {
            _useSettingsSortKey = false;
            _useSettingsSortDirection = false;
            OnPropertyChanged(nameof(EffectiveSortBy));
            OnPropertyChanged(nameof(EffectiveIsSortDescending));
        }

        /// <summary>
        /// Sets one secondary/tertiary sort slot (0 = secondary, 1 = tertiary). Slots beyond
        /// <see cref="MaxSecondarySorts"/> are ignored. Raises <see cref="SecondarySorts"/> so the
        /// search session rebuilds. Session-only; not persisted to settings.
        /// </summary>
        public void SetSecondarySort(int slot, SortBy sortBy, bool descending)
        {
            if (slot < 0 || slot >= MaxSecondarySorts)
                return;

            // Replace (never mutate) the list so unchanged queries stay record-equal across
            // rebuilds — the pipe client keys its result-set reuse on query equality.
            var updated = _secondarySorts is null
                ? new List<(SortBy SortBy, bool Descending)>(MaxSecondarySorts)
                : new List<(SortBy SortBy, bool Descending)>(_secondarySorts);
            while (updated.Count <= slot)
                updated.Add(default);

            updated[slot] = (sortBy, descending);
            _secondarySorts = updated;
            OnPropertyChanged(nameof(SecondarySorts));
        }

        /// <summary>Removes all secondary/tertiary sorts (session-only state).</summary>
        public void ClearSecondarySorts()
        {
            if (_secondarySorts is null)
                return;

            _secondarySorts = null;
            OnPropertyChanged(nameof(SecondarySorts));
        }

        private string ApplyMacros(string searchTerm)
        {
            var result = searchTerm;

            foreach (var f in _filterProvider.Filters)
            {
                if (string.IsNullOrEmpty(f.Macro))
                    continue;

                result = result.Replace(f.Macro + ":", f.Search + " ");
            }

            var defaultMacros = new Dictionary<string, string>
            {
                // Macros quot:, gt: and lt: are not supported by the SDK
                { "apos:", "'" },
                { "amp:", "&" },
            };
            foreach (var defaultMacro in defaultMacros)
            {
                result = result.Replace(defaultMacro.Key, defaultMacro.Value);
            }

            return result;
        }

        public string BuildSearchTerm()
        {
            var supportsEverything15 = _everythingClient.GetEverythingVersion().Minor >= 5;
            // The filter prefix comes first so the filter's own match settings aren't overridden by
            // the global modifiers below. Its search text starts from Everything's defaults, hence
            // the false values.
            var rawSearchTerm =
                Filter.GetSearchPrefix(
                    IsMatchCase,
                    IsMatchWholeWord,
                    IsMatchPath,
                    IsRegExEnabled,
                    false,
                    false,
                    false,
                    false,
                    false,
                    supportsEverything15
                ) + GetGlobalModifierPrefix(supportsEverything15);
            var searchTermWithAppliedMacros = ApplyMacros(rawSearchTerm);
            return searchTermWithAppliedMacros;
        }

        private string GetGlobalModifierPrefix(bool supportsEverything15)
        {
            if (IsRegExEnabled)
                return SearchTerm;

            var modifiers = "";

            if (IsMatchDiacritics)
                modifiers += "diacritics:";

            if (supportsEverything15)
            {
                if (IsMatchPrefix)
                    modifiers += "prefix:";
                if (IsMatchSuffix)
                    modifiers += "suffix:";
                if (IsIgnorePunctuation)
                    modifiers += "ignore-punctuation:";
                if (IsIgnoreWhitespace)
                    modifiers += "ignore-whitespace:";
            }

            if (string.IsNullOrEmpty(modifiers) || string.IsNullOrEmpty(SearchTerm))
                return SearchTerm;

            return $"{modifiers}<{SearchTerm}>";
        }

        public SearchQuery BuildSearchQuery()
        {
            return new SearchQuery(
                BuildSearchTerm(),
                EffectiveSortBy,
                EffectiveIsSortDescending,
                IsMatchCase,
                IsMatchPath,
                IsMatchWholeWord,
                IsRegExEnabled,
                IsHideResultOmissions,
                // Passed by reference on purpose: the list is treated as immutable and replaced
                // (not mutated) on change, so record equality still lets the pipe client reuse
                // result sets for unchanged queries.
                SecondarySorts: _secondarySorts
            );
        }

        /// <summary>
        /// Captures the full user-mutable search state (query, filter reference, effective sort,
        /// match flags) into a tab snapshot for browser-style tabs.
        /// </summary>
        public SearchTab CaptureSnapshot()
        {
            var tab = new SearchTab();
            CaptureSnapshotInto(tab);
            return tab;
        }

        /// <summary>Same as <see cref="CaptureSnapshot"/> but reuses an existing snapshot instance.</summary>
        public void CaptureSnapshotInto(SearchTab tab)
        {
            tab.SearchTerm = SearchTerm;
            tab.Filter = Filter;
            tab.SortBy = EffectiveSortBy;
            tab.IsSortDescending = EffectiveIsSortDescending;
            // Secondary sorts are session-only; carried through the snapshot (copied so the tab
            // owns its list) but never persisted to settings.
            tab.SecondarySorts = _secondarySorts is null
                ? null
                : new List<(SortBy SortBy, bool Descending)>(_secondarySorts);
            tab.IsMatchCase = IsMatchCase;
            tab.IsMatchPath = IsMatchPath;
            tab.IsMatchWholeWord = IsMatchWholeWord;
            tab.IsMatchDiacritics = IsMatchDiacritics;
            tab.IsMatchPrefix = IsMatchPrefix;
            tab.IsMatchSuffix = IsMatchSuffix;
            tab.IsIgnorePunctuation = IsIgnorePunctuation;
            tab.IsIgnoreWhitespace = IsIgnoreWhitespace;
            tab.IsRegExEnabled = IsRegExEnabled;
            tab.IsHideResultOmissions = IsHideResultOmissions;
        }

        /// <summary>
        /// Restores a tab snapshot: sets the query and filter, forces the captured effective sort
        /// as an explicit override, and applies the captured match flags to this state's
        /// per-tab properties (settings are not touched). Raises the same property change
        /// notifications the search session listens to (an explicit pass at the end guarantees a
        /// rebuild even when individual values were unchanged).
        /// </summary>
        public void RestoreSnapshot(SearchTab tab)
        {
            SearchTerm = tab.SearchTerm ?? "";

            if (tab.Filter is not null)
                Filter = tab.Filter;

            SetSort(tab.SortBy, tab.IsSortDescending);

            if (tab.SecondarySorts is { Count: > 0 } secondarySorts)
            {
                _secondarySorts = new List<(SortBy SortBy, bool Descending)>(secondarySorts);
                OnPropertyChanged(nameof(SecondarySorts));
            }
            else
            {
                ClearSecondarySorts();
            }

            // Match flags are SearchState-owned; setting the local properties leaves the
            // settings ini untouched (per-tab isolation).
            IsMatchCase = tab.IsMatchCase;
            IsMatchPath = tab.IsMatchPath;
            IsMatchWholeWord = tab.IsMatchWholeWord;
            IsMatchDiacritics = tab.IsMatchDiacritics;
            IsMatchPrefix = tab.IsMatchPrefix;
            IsMatchSuffix = tab.IsMatchSuffix;
            IsIgnorePunctuation = tab.IsIgnorePunctuation;
            IsIgnoreWhitespace = tab.IsIgnoreWhitespace;
            IsRegExEnabled = tab.IsRegExEnabled;
            IsHideResultOmissions = tab.IsHideResultOmissions;

            // The property setters above only raise on change; raise explicitly so the session
            // always rebuilds on switch.
            OnPropertyChanged(nameof(SearchTerm));
            OnPropertyChanged(nameof(IsMatchCase));
            OnPropertyChanged(nameof(IsMatchPath));
            OnPropertyChanged(nameof(IsMatchWholeWord));
            OnPropertyChanged(nameof(IsMatchDiacritics));
            OnPropertyChanged(nameof(IsMatchPrefix));
            OnPropertyChanged(nameof(IsMatchSuffix));
            OnPropertyChanged(nameof(IsIgnorePunctuation));
            OnPropertyChanged(nameof(IsIgnoreWhitespace));
            OnPropertyChanged(nameof(IsRegExEnabled));
            OnPropertyChanged(nameof(IsHideResultOmissions));
        }

        /// <summary>
        /// Reads the match-flag defaults from settings into this state's per-tab properties
        /// (settings remain the source for startup defaults and external changes, e.g. the
        /// compact popup or settings UI toggles). Pure with respect to settings: never writes.
        /// </summary>
        internal void ApplyFlagsFrom(ISettings settings)
        {
            IsMatchCase = settings.IsMatchCase;
            IsMatchPath = settings.IsMatchPath;
            IsMatchWholeWord = settings.IsMatchWholeWord;
            IsMatchDiacritics = settings.IsMatchDiacritics;
            IsMatchPrefix = settings.IsMatchPrefix;
            IsMatchSuffix = settings.IsMatchSuffix;
            IsIgnorePunctuation = settings.IsIgnorePunctuation;
            IsIgnoreWhitespace = settings.IsIgnoreWhitespace;
            IsRegExEnabled = settings.IsRegExEnabled;
            IsHideResultOmissions = settings.IsHideResultOmissions;
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ISettings.IsMatchCase):
                case nameof(ISettings.IsMatchPath):
                case nameof(ISettings.IsMatchWholeWord):
                case nameof(ISettings.IsMatchDiacritics):
                case nameof(ISettings.IsMatchPrefix):
                case nameof(ISettings.IsMatchSuffix):
                case nameof(ISettings.IsIgnorePunctuation):
                case nameof(ISettings.IsIgnoreWhitespace):
                case nameof(ISettings.IsRegExEnabled):
                case nameof(ISettings.IsHideResultOmissions):
                    ApplyFlagsFrom(_settings);
                    break;
                case nameof(ISettings.SortBy):
                    OnPropertyChanged(nameof(SortBy));
                    break;
                case nameof(ISettings.IsSortDescending):
                    OnPropertyChanged(nameof(IsSortDescending));
                    break;
                case nameof(ISettings.IsHideEmptySearchResults):
                    SearchTerm = "";
                    OnPropertyChanged(nameof(SearchTerm));
                    break;
            }
        }
    }
}
