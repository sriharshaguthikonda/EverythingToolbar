using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Platform.Shell;
using NLog;

namespace EverythingToolbar.App
{
    /// <summary>
    /// Modal batch-rename dialog (PowerRename-like scope, English-only strings). The preview is
    /// planned purely in memory from the selected results' directory-stripped file names via
    /// <see cref="BatchRenameEngine.Plan"/>, recomputed on a background thread with a 250 ms
    /// debounce and marshalled back to the UI (generation-checked, so stale plans are dropped).
    /// Nothing touches the filesystem until Rename is clicked; then the effective changes
    /// (name changed, no error) are handed to <see cref="FileOperationsService.RenameMany"/>
    /// as one shell IFileOperation batch. The dialog closes only on success — shell conflict
    /// dialogs surface natively and a failed/aborted batch keeps the dialog open.
    /// "Enumerate items" sets <see cref="BatchRenameOptions.CounterFormat"/> to
    /// "${start=0,increment=1,padding=0}": per the engine's API, CounterFormat is only the
    /// on/off switch for ${...} expansion — the pattern body in the Replace text itself
    /// controls start/increment/padding, so explicit ${...} patterns keep working.
    /// </summary>
    public partial class BatchRenameDialog : Window
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>Counter enablement token; the body values are the engine's documented defaults.</summary>
        private const string CounterFormat = "${start=0,increment=1,padding=0}";

        private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(250);

        private readonly FileOperationsService _fileOperations =
            Ioc.Default.GetRequiredService<FileOperationsService>();

        private readonly IReadOnlyDictionary<string, string> _fullPathByOriginalName;
        private readonly DispatcherTimer _debounceTimer;
        private int _planGeneration;
        private PreviewResult? _latestPreview;

        private sealed record InputSnapshot(
            string Search,
            string Replace,
            bool UseRegex,
            bool CaseSensitive,
            bool MatchAll,
            bool IncludeExtension,
            bool Enumerate
        )
        {
            public BatchRenameOptions ToOptions() =>
                new(
                    Search,
                    Replace,
                    UseRegex,
                    CaseSensitive,
                    MatchAll,
                    IncludeExtension,
                    Enumerate ? CounterFormat : null
                );
        }

        public sealed class PreviewRow
        {
            public string Original { get; init; } = "";
            public string New { get; init; } = "";
            public string? Error { get; init; }
        }

        private sealed record PreviewResult(
            BatchRenamePlan Plan,
            IReadOnlyList<PreviewRow> Rows,
            IReadOnlyList<(string Path, string NewName)> EffectiveChanges,
            string Summary,
            bool HasItemErrors
        );

        public BatchRenameDialog(IEnumerable<string> fullPaths)
        {
            InitializeComponent();

            // Plan input is file NAMES only (engine contract); keep the original full paths so
            // the effective changes can be mapped back per file.
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var fullPath in fullPaths)
            {
                var name = Path.GetFileName(fullPath);
                if (name.Length > 0 && !map.ContainsKey(name))
                    map[name] = fullPath;
            }

            _fullPathByOriginalName = map;

            _debounceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = DebounceInterval };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                SchedulePreview();
            };

            SchedulePreview();
        }

        private void OnInputChanged(object sender, RoutedEventArgs e)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e)
        {
            OnInputChanged(sender, (RoutedEventArgs)e);
        }

        private InputSnapshot CaptureInput() =>
            new(
                Search: SearchBox.Text,
                Replace: ReplaceBox.Text,
                UseRegex: UseRegexCheck.IsChecked == true,
                CaseSensitive: CaseSensitiveCheck.IsChecked == true,
                MatchAll: MatchAllCheck.IsChecked == true,
                IncludeExtension: IncludeExtensionCheck.IsChecked == true,
                Enumerate: EnumerateCheck.IsChecked == true
            );

        private void SchedulePreview()
        {
            var generation = ++_planGeneration;
            var snapshot = CaptureInput();
            var names = _fullPathByOriginalName.Keys.ToArray();
            var nameMap = _fullPathByOriginalName;

            Task.Run(() =>
                {
                    // Background thread: pure engine work only, no WPF, no I/O, no shell.
                    var plan = BatchRenameEngine.Plan(names, snapshot.ToOptions());
                    var changes = BatchRenameApply.SelectEffectiveChanges(plan, nameMap);
                    var skipped = plan.Items.Count - changes.Count;
                    var summary = BatchRenameApply.BuildSummary(changes.Count, plan.Items.Count, skipped);
                    var rows = plan
                        .Items.Select(item => new PreviewRow
                        {
                            Original = item.Original,
                            New = item.New,
                            Error = item.Error,
                        })
                        .ToList();
                    var hasItemErrors = plan.Items.Any(item => item.Error is not null);
                    return new PreviewResult(plan, rows, changes, summary, hasItemErrors);
                })
                .ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted)
                        {
                            Logger.Error(task.Exception, "Batch rename preview computation failed.");
                            return;
                        }

                        if (generation != _planGeneration)
                            return; // A newer input snapshot superseded this plan.

                        _latestPreview = task.Result;
                        PreviewList.ItemsSource = task.Result.Rows;
                        StatusColumn.Width = task.Result.HasItemErrors ? 200 : 0;
                        SummaryText.Text = task.Result.Summary;
                        RenameButton.IsEnabled =
                            !task.Result.Plan.IsNoOp
                            && !task.Result.Plan.HasError
                            && task.Result.EffectiveChanges.Count > 0;
                    },
                    TaskScheduler.FromCurrentSynchronizationContext()
                );
        }

        private void OnRenameClick(object sender, RoutedEventArgs e)
        {
            var preview = _latestPreview;
            if (preview is null || preview.EffectiveChanges.Count == 0)
                return;

            // One shell IFileOperation batch for the whole list; shell conflict dialogs surface
            // natively. Close only when the batch succeeded and was not aborted.
            if (_fileOperations.RenameMany(preview.EffectiveChanges))
            {
                DialogResult = true;
            }
            else
            {
                Logger.Info("Batch rename did not complete; dialog kept open.");
            }
        }
    }
}
