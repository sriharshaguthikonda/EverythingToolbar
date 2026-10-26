using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.App.Search
{
    /// <summary>
    /// Snapshot of one browser-style tab's user-mutable search state (query text, filter reference,
    /// effective sort, match flags). Pure data: the live state lives in the single
    /// <see cref="SearchState"/>; a tab only holds what was captured from it.
    /// </summary>
    public sealed partial class SearchTab : ObservableObject
    {
        public Guid Id { get; init; } = Guid.NewGuid();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayName))]
        private string _searchTerm = "";

        [ObservableProperty]
        private bool _isActive;

        /// <summary>Filter reference captured from SearchState; null when unknown (new tab / unresolved persisted name).</summary>
        public Filter? Filter { get; set; }

        public SortBy SortBy { get; set; }

        public bool IsSortDescending { get; set; }

        /// <summary>
        /// Session-only secondary/tertiary sorts captured from SearchState (max 2, applied after
        /// the primary sort). Never persisted to settings; carried across tab switches only.
        /// </summary>
        public List<(SortBy SortBy, bool Descending)>? SecondarySorts { get; set; }

        public bool IsMatchCase { get; set; }

        public bool IsMatchPath { get; set; }

        public bool IsMatchWholeWord { get; set; }

        public bool IsMatchDiacritics { get; set; }

        public bool IsMatchPrefix { get; set; }

        public bool IsMatchSuffix { get; set; }

        public bool IsIgnorePunctuation { get; set; }

        public bool IsIgnoreWhitespace { get; set; }

        public bool IsRegExEnabled { get; set; }

        public bool IsHideResultOmissions { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(SearchTerm) ? "New Tab" : SearchTerm;

        /// <summary>Copies every mutable field (everything except Id) from another snapshot into this one.</summary>
        public void CopyInto(SearchTab target)
        {
            target.SearchTerm = SearchTerm;
            target.Filter = Filter;
            target.SortBy = SortBy;
            target.IsSortDescending = IsSortDescending;
            target.SecondarySorts = SecondarySorts is null
                ? null
                : new List<(SortBy SortBy, bool Descending)>(SecondarySorts);
            target.IsMatchCase = IsMatchCase;
            target.IsMatchPath = IsMatchPath;
            target.IsMatchWholeWord = IsMatchWholeWord;
            target.IsMatchDiacritics = IsMatchDiacritics;
            target.IsMatchPrefix = IsMatchPrefix;
            target.IsMatchSuffix = IsMatchSuffix;
            target.IsIgnorePunctuation = IsIgnorePunctuation;
            target.IsIgnoreWhitespace = IsIgnoreWhitespace;
            target.IsRegExEnabled = IsRegExEnabled;
            target.IsHideResultOmissions = IsHideResultOmissions;
        }
    }
}
