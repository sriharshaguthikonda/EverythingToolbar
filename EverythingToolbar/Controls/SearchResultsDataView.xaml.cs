using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CommunityToolkit.Mvvm.DependencyInjection;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Platform.Shell;
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
        private readonly ShellContextMenuService _shellContextMenu =
            Ioc.Default.GetRequiredService<ShellContextMenuService>();
        private readonly Search.EditOperationsController _editOperations =
            Ioc.Default.GetRequiredService<Search.EditOperationsController>();

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
            if (IsContextMenuKey(e) && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
            {
                ShowShellContextMenuAtSelectedResult();
                e.Handled = true;
                return;
            }

            if (TryHandleEditGesture(e))
                return;

            if (_commands.TranslateResultsGesture(e.Key, e.SystemKey, Keyboard.Modifiers, fromSearchBox: false))
                e.Handled = true;
        }

        /// <summary>
        /// Handles the Edit-menu gestures (Everything semantics): Ctrl+X cut, Ctrl+C copy files,
        /// Ctrl+V paste, Delete recycle-delete, Ctrl+Shift+C copy full name and path. Skipped when
        /// a text box has keyboard focus so text editing keeps its native behaviour.
        /// </summary>
        public bool TryHandleEditGesture(KeyEventArgs e)
        {
            var action = EditOperationPlanner.MapGesture(e.Key, Keyboard.Modifiers);
            if (action == EditFileAction.None)
                return false;

            // Ctrl+X/C/V and Delete keep their native text-editing behaviour while a text box
            // (the search box) has focus; the Ctrl+Shift+ C/N/P text-copy gestures are not
            // text-editing gestures.
            if (
                action is not (EditFileAction.CopyFullPath or EditFileAction.CopyName or EditFileAction.CopyPath)
                && Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
            )
                return false;

            var selection = GetSelectedResults();
            switch (action)
            {
                case EditFileAction.Cut:
                    _editOperations.Cut(selection);
                    break;
                case EditFileAction.Copy:
                    _editOperations.Copy(selection);
                    break;
                case EditFileAction.Paste:
                    _editOperations.Paste(selection);
                    break;
                case EditFileAction.Delete:
                    _editOperations.Delete(selection);
                    break;
                case EditFileAction.CopyFullPath:
                    _commands.CopySelectedPath();
                    break;
                case EditFileAction.CopyPath:
                    _editOperations.CopyPaths(selection);
                    break;
                case EditFileAction.CopyName:
                    _editOperations.CopyNames(selection);
                    break;
            }

            e.Handled = true;
            return true;
        }

        private void OnListRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None)
                return;

            var item = FindListViewItem(e.OriginalSource as DependencyObject);
            if (item == null)
                return;

            var clicked = ResultsListView.ItemContainerGenerator.ItemFromContainer(item) as SearchResult;
            var clickedIsSelected = clicked != null && ResultsListView.SelectedItems.Contains(clicked);
            if (
                ShellContextMenuSelection.ShouldReselectOnRightClick(
                    clickedIsSelected,
                    ResultsListView.SelectedItems.Count
                )
            )
            {
                ResultsListView.UnselectAll();
                if (clicked != null)
                    ResultsListView.SelectedItems.Add(clicked);
            }

            var screenPoint = ResultsListView.PointToScreen(e.GetPosition(ResultsListView));
            ShowShellContextMenu(screenPoint);
            e.Handled = true;
        }

        private static bool IsContextMenuKey(KeyEventArgs e) =>
            e.Key == Key.Apps || (e.Key == Key.System && e.SystemKey == Key.F10);

        private void ShowShellContextMenuAtSelectedResult()
        {
            if (ResultsListView.SelectedItem == null)
                return;

            if (
                ResultsListView.ItemContainerGenerator.ContainerFromItem(ResultsListView.SelectedItem)
                is FrameworkElement container
            )
            {
                var center = new Point(container.ActualWidth / 2, container.ActualHeight / 2);
                ShowShellContextMenu(container.PointToScreen(center));
            }
        }

        private void ShowShellContextMenu(Point screenPoint)
        {
            var paths = ShellContextMenuSelection.BuildPathList(SelectedResults());
            if (paths.Count == 0)
                return;

            var window = Window.GetWindow(this);
            var handle = window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
            _shellContextMenu.ShowMenu(handle, paths, (int)screenPoint.X, (int)screenPoint.Y);
        }

        private IEnumerable<SearchResult?> SelectedResults()
        {
            foreach (var item in ResultsListView.SelectedItems)
                yield return item as SearchResult;
        }

        /// <summary>The currently selected results, for the Edit-menu operations.</summary>
        public IReadOnlyList<SearchResult?> GetSelectedResults()
        {
            var results = new List<SearchResult?>(ResultsListView.SelectedItems.Count);
            foreach (var item in ResultsListView.SelectedItems)
                results.Add(item as SearchResult);
            return results;
        }

        private static ListViewItem? FindListViewItem(DependencyObject? source)
        {
            while (source != null && source is not ListViewItem)
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            return source as ListViewItem;
        }
    }
}
