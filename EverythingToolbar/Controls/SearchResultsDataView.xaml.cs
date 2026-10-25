using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.DependencyInjection;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Search;

namespace EverythingToolbar.Controls
{
    public partial class SearchResultsDataView
    {
        public static readonly DependencyProperty SelectedResultProperty = DependencyProperty.Register(
            nameof(SelectedResult),
            typeof(SearchResult),
            typeof(SearchResultsDataView),
            new PropertyMetadata(null)
        );

        public SearchResult? SelectedResult
        {
            get => (SearchResult?)GetValue(SelectedResultProperty);
            set => SetValue(SelectedResultProperty, value);
        }

        private readonly SearchSession _session = Ioc.Default.GetRequiredService<SearchSession>();
        private readonly SearchState _searchState = Ioc.Default.GetRequiredService<SearchState>();
        private readonly SearchCommands _commands = Ioc.Default.GetRequiredService<SearchCommands>();

        public SearchResultsDataView()
        {
            InitializeComponent();

            _columnSortMap = new Dictionary<GridViewColumn, SortBy>
            {
                [ColumnName] = SortBy.Name,
                [ColumnPath] = SortBy.Path,
                [ColumnSize] = SortBy.Size,
                [ColumnDateModified] = SortBy.DateModified,
                [ColumnDateCreated] = SortBy.DateCreated,
                [ColumnExtension] = SortBy.Extension,
                [ColumnTypeName] = SortBy.TypeName,
            };

            ResultsListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnColumnHeaderClick));
            ResultsListView.SelectionChanged += OnSelectionChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _searchState.PropertyChanged += OnSearchStateChanged;
            _session.Start();
            UpdateSortGlyphs();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _searchState.PropertyChanged -= OnSearchStateChanged;
        }

        private void OnSearchStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SearchState.EffectiveSortBy) or nameof(SearchState.EffectiveIsSortDescending))
                UpdateSortGlyphs();
        }

        private readonly Dictionary<GridViewColumn, SortBy> _columnSortMap;

        private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not GridViewColumnHeader header || header.Column == null)
                return;

            if (!_columnSortMap.TryGetValue(header.Column, out var clicked))
                return;

            var next = SortCycle.Next(_searchState.EffectiveSortBy, clicked, _searchState.EffectiveIsSortDescending);
            if (next is { } sort)
                _searchState.SetSort(sort.SortBy, sort.Descending);
            else
                _searchState.ResetSort();
        }

        private void UpdateSortGlyphs()
        {
            foreach (var pair in _columnSortMap)
            {
                var column = pair.Key;
                var sortBy = pair.Value;
                if (column.Header is not string header)
                    continue;

                var baseHeader = header.TrimEnd(' ', '▲', '▼');
                column.Header =
                    sortBy == _searchState.EffectiveSortBy
                        ? baseHeader + (_searchState.EffectiveIsSortDescending ? " ▼" : " ▲")
                        : baseHeader;
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsListView.SelectedItem != null)
                ResultsListView.ScrollIntoView(ResultsListView.SelectedItem);
        }

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _commands.OpenSelected(SelectedResult);
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (_commands.TranslateResultsGesture(e.Key, e.SystemKey, Keyboard.Modifiers, fromSearchBox: false))
                e.Handled = true;
        }
    }
}
