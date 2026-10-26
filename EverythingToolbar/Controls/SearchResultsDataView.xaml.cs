using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using CommunityToolkit.Mvvm.DependencyInjection;
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
        private readonly ISettings _settings = Ioc.Default.GetRequiredService<ISettings>();

        /// <summary>
        /// Hideable columns with their width-collapse state, seeded with the designed widths from
        /// the XAML (reused by "Reset Columns"). The Name column is never hideable.
        /// </summary>
        private readonly Dictionary<GridViewColumn, ColumnVisibilityState> _columnVisibility;

        /// <summary>Designed widths from the XAML, reused by "Reset Columns".</summary>
        private readonly Dictionary<GridViewColumn, double> _defaultColumnWidths = new();

        /// <summary>Press point of an in-progress left-button drag gesture.</summary>
        private Point? _dragStartPoint;

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
            ResultsListView.PreviewMouseLeftButtonDown += OnResultsListPreviewMouseLeftButtonDown;
            ResultsListView.MouseMove += OnResultsListMouseMove;

            _columnVisibility = new Dictionary<GridViewColumn, ColumnVisibilityState>
            {
                [ColumnPath] = new ColumnVisibilityState("Path", savedWidth: ColumnPath.Width),
                [ColumnSize] = new ColumnVisibilityState("Size", savedWidth: ColumnSize.Width),
                [ColumnDateModified] = new ColumnVisibilityState("Date Modified", savedWidth: ColumnDateModified.Width),
                [ColumnDateCreated] = new ColumnVisibilityState("Date Created", savedWidth: ColumnDateCreated.Width),
                [ColumnExtension] = new ColumnVisibilityState("Ext", savedWidth: ColumnExtension.Width),
                [ColumnTypeName] = new ColumnVisibilityState("Type", savedWidth: ColumnTypeName.Width),
            };

            foreach (var pair in _columnVisibility)
                _defaultColumnWidths[pair.Key] = pair.Key.Width;

            if (ResultsListView.View is GridView gridView)
                gridView.ColumnHeaderContextMenu = BuildHeaderContextMenu();

            _detailsView = ResultsListView.View;
        }

        /// <summary>The Details (full GridView) presentation, restored when Thumbnails is turned off.</summary>
        private ViewBase? _detailsView;

        /// <summary>
        /// View mode only changes presentation: selection, sort state, context menu, drag and edit
        /// gestures all live on the shared ListView and keep working across the swap.
        /// </summary>
        private void ApplyViewMode()
        {
            var view = _settings.IsThumbnailsView ? (ViewBase)FindResource("ThumbnailsView") : _detailsView;
            if (view != null && !ReferenceEquals(ResultsListView.View, view))
                ResultsListView.View = view;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _searchState.PropertyChanged += OnSearchStateChanged;
            _settings.PropertyChanged += OnSettingsPropertyChanged;
            _session.Start();
            ApplyColumnVisibilityFromSettings();
            ApplyViewMode();
            UpdateSortGlyphs();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _searchState.PropertyChanged -= OnSearchStateChanged;
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ISettings.IsThumbnailsView))
                ApplyViewMode();
        }

        private void OnSearchStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SearchState.EffectiveSortBy) or nameof(SearchState.EffectiveIsSortDescending))
                UpdateSortGlyphs();
        }

        private readonly Dictionary<GridViewColumn, SortBy> _columnSortMap;

        private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            // GridViewColumnHeader.Click only fires for the left button; guard anyway so a
            // right-click (the header context menu) can never trigger a sort change.
            if (Mouse.RightButton == MouseButtonState.Pressed)
                return;

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

        /// <summary>
        /// Builds the right-click context menu shown on the results column headers: one checkable
        /// entry per hideable column plus a "Reset Columns" entry. The Name column is not listed
        /// (Everything does not allow hiding the first column).
        /// </summary>
        private ContextMenu BuildHeaderContextMenu()
        {
            var menu = new ContextMenu();
            foreach (var pair in _columnVisibility)
            {
                var item = new MenuItem
                {
                    Header = pair.Value.ColumnName,
                    IsCheckable = true,
                    Tag = pair.Key,
                };
                item.IsChecked = IsColumnVisible(pair.Key);
                item.Click += OnColumnVisibilityMenuItemClick;
                menu.Items.Add(item);
            }

            var reset = new MenuItem { Header = "Reset Columns" };
            reset.Click += OnResetColumnsClick;
            menu.Items.Add(reset);
            return menu;
        }

        private void OnColumnVisibilityMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || item.Tag is not GridViewColumn column)
                return;
            if (!_columnVisibility.TryGetValue(column, out var state))
                return;

            var visible = !IsColumnVisible(column);
            SetColumnVisible(column, visible);
            column.Width = state.SetVisible(visible, column.Width);
        }

        private void OnResetColumnsClick(object sender, RoutedEventArgs e)
        {
            foreach (var pair in _columnVisibility)
            {
                SetColumnVisible(pair.Key, true);
                pair.Key.Width = pair.Value.ResetTo(_defaultColumnWidths[pair.Key]);
            }

            RefreshHeaderContextMenuCheckedStates();
        }

        private void RefreshHeaderContextMenuCheckedStates()
        {
            if (ResultsListView.View is not GridView gridView || gridView.ColumnHeaderContextMenu == null)
                return;

            foreach (var item in gridView.ColumnHeaderContextMenu.Items.OfType<MenuItem>())
            {
                if (item.Tag is GridViewColumn column)
                    item.IsChecked = IsColumnVisible(column);
            }
        }

        private void ApplyColumnVisibilityFromSettings()
        {
            foreach (var pair in _columnVisibility)
            {
                var visible = IsColumnVisible(pair.Key);
                pair.Key.Width = pair.Value.SetVisible(visible, pair.Key.Width);
            }
        }

        private bool IsColumnVisible(GridViewColumn column)
        {
            if (column == ColumnPath)
                return _settings.IsPathColumnVisible;
            if (column == ColumnSize)
                return _settings.IsSizeColumnVisible;
            if (column == ColumnDateModified)
                return _settings.IsDateModifiedColumnVisible;
            if (column == ColumnDateCreated)
                return _settings.IsDateCreatedColumnVisible;
            if (column == ColumnExtension)
                return _settings.IsExtensionColumnVisible;
            if (column == ColumnTypeName)
                return _settings.IsTypeNameColumnVisible;
            return true;
        }

        private void SetColumnVisible(GridViewColumn column, bool value)
        {
            if (column == ColumnPath)
                _settings.IsPathColumnVisible = value;
            else if (column == ColumnSize)
                _settings.IsSizeColumnVisible = value;
            else if (column == ColumnDateModified)
                _settings.IsDateModifiedColumnVisible = value;
            else if (column == ColumnDateCreated)
                _settings.IsDateCreatedColumnVisible = value;
            else if (column == ColumnExtension)
                _settings.IsExtensionColumnVisible = value;
            else if (column == ColumnTypeName)
                _settings.IsTypeNameColumnVisible = value;
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
        /// Ctrl+V paste, Delete recycle-delete, F2 rename, Ctrl+Shift+C copy full name and path.
        /// Skipped when a text box has keyboard focus so text editing keeps its native behaviour
        /// (F2, like Cut/Copy/Paste, acts on files and is blocked while a text box is focused).
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
                case EditFileAction.Rename:
                    _editOperations.Rename(selection, Window.GetWindow(this));
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

        /// <summary>
        /// Records the press point so a subsequent move beyond the drag thresholds can start a
        /// file drag of the selected results. Drags are never started from a press on the column
        /// headers or on empty space below the rows.
        /// </summary>
        private void OnResultsListPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = null;

            if (IsColumnHeader(e.OriginalSource as DependencyObject))
                return;

            var item = FindListViewItem(e.OriginalSource as DependencyObject);
            if (item == null)
                return;

            _dragStartPoint = e.GetPosition(ResultsListView);
        }

        private void OnResultsListMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _dragStartPoint = null;
                return;
            }

            if (_dragStartPoint is not Point start || ResultsListView.SelectedItems.Count == 0)
                return;

            var current = e.GetPosition(ResultsListView);
            if (
                !DragSelectionBuilder.ShouldStartDrag(
                    start.X,
                    start.Y,
                    current.X,
                    current.Y,
                    SystemParameters.MinimumHorizontalDragDistance,
                    SystemParameters.MinimumVerticalDragDistance
                )
            )
                return;

            var paths = DragSelectionBuilder.BuildFilePaths(SelectedResults());
            if (paths.Length == 0)
                return;

            _dragStartPoint = null;

            var data = new DataObject(DataFormats.FileDrop, paths);
            data.SetData(DataFormats.Text, paths[0]);
            DragDrop.DoDragDrop(ResultsListView, data, DragDropEffects.All);
        }

        private static bool IsColumnHeader(DependencyObject? source)
        {
            while (source != null && source is not ListView)
            {
                if (source is GridViewColumnHeader)
                    return true;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }

            return false;
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
