using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using EverythingToolbar.App.Helpers;
using EverythingToolbar.App.Search;
using EverythingToolbar.Core.Search;

namespace EverythingToolbar.Controls
{
    /// <summary>
    /// Folder sidebar for the expanded search window. Roots are ready local Fixed/Removable drives;
    /// expansion runs one `parent:"..." folder:` query per node (lazy, off the UI thread) and the
    /// selection navigates the main results to the folder's contents. No icons in v1.
    /// </summary>
    public partial class FolderSidebarView
    {
        private readonly SearchState _searchState = Ioc.Default.GetRequiredService<SearchState>();
        private readonly IEverythingClient _everythingClient = Ioc.Default.GetRequiredService<IEverythingClient>();

        public FolderSidebarView()
        {
            InitializeComponent();

            FolderTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(OnItemExpanded));
            FolderTree.ItemsSource = new ObservableCollection<FolderNode>(GetDriveRoots());
        }

        private static FolderNode[] GetDriveRoots()
        {
            var roots = DriveInfo
                .GetDrives()
                .Where(d => d.IsReady && (d.DriveType is DriveType.Fixed or DriveType.Removable))
                .Select(d => FolderNode.CreatePlaceholder(d.Name))
                .ToArray();

            // Give drive roots ("C:\") a clean label without the trailing separator.
            foreach (var root in roots.Where(r => r.Name.EndsWith("\\", StringComparison.Ordinal)))
                root.Name = root.Name.TrimEnd('\\');

            return roots;
        }

        private void OnItemExpanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not TreeViewItem { DataContext: FolderNode node })
                return;

            LoadChildren(node);
        }

        private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is not FolderNode node)
                return;

            // Navigate the main results to this folder's contents (no folder: suffix — files count too).
            _searchState.SearchTerm = FolderQueryFactory.BuildNavigateQuery(node.FullPath).SearchText;
        }

        private async void LoadChildren(FolderNode node)
        {
            // Double-expansion guard: a second Expanded event (or IsExpanded flip) while a query is
            // in flight is ignored; the in-flight completion fills the children anyway.
            if (node.IsLoaded || node.IsLoading)
                return;

            node.IsLoading = true;
            // Stale-result guard: collapse + re-expand while a query is running bumps the
            // generation, so the earlier query's late result is discarded.
            var generation = ++node.LoadGeneration;

            var query = FolderQueryFactory.BuildChildrenQuery(node.FullPath);
            var startIndex = 0;
            var pageSize = FolderQueryFactory.SidebarPageSize;

            try
            {
                var results = await Task.Run(() =>
                    _everythingClient.QueryRangeSync(
                        query,
                        startIndex,
                        pageSize,
                        System.Threading.CancellationToken.None
                    )
                );

                if (generation != node.LoadGeneration || node.IsLoaded)
                    return;

                var children = results
                    .Select(r => FolderNode.CreatePlaceholder(r.FullPathAndFileName.TrimEnd('\\', '/')))
                    .Where(n => n.Name.Length > 0)
                    .ToArray();

                node.Children.Clear();
                foreach (var child in children)
                    node.Children.Add(child);

                node.IsLoaded = true;
            }
            catch (Exception exception)
            {
                EverythingToolbar
                    .App.Helpers.ToolbarLogger.GetLogger<FolderSidebarView>()
                    .Error(exception, "Failed to list child folders of {0}.", node.FullPath);
            }
            finally
            {
                node.IsLoading = false;
            }
        }

        /// <summary>Tree node for one folder. Children are placeholders until first expansion.</summary>
        public sealed class FolderNode
        {
            private const string PlaceholderName = "Loading…";

            public string Name { get; set; } = "";
            public string FullPath { get; private set; } = "";
            public bool IsLoaded { get; set; }
            public bool IsLoading { get; set; }
            public int LoadGeneration { get; set; }
            public bool IsExpanded { get; set; }
            public ObservableCollection<FolderNode> Children { get; } = new();

            /// <summary>Creates a node with one dummy child so the expander arrow shows before the lazy load.</summary>
            public static FolderNode CreatePlaceholder(string fullPath)
            {
                var node = new FolderNode
                {
                    FullPath = fullPath,
                    Name = Path.GetFileName(
                        fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    ),
                };
                if (node.Name.Length == 0)
                    node.Name = fullPath;

                node.Children.Add(new FolderNode { Name = PlaceholderName });
                return node;
            }
        }
    }
}
