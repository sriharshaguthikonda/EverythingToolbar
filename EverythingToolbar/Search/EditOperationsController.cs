using System;
using System.Collections.Generic;
using EverythingToolbar.App.Helpers;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Platform.Shell;
using NLog;

namespace EverythingToolbar.Search
{
    /// <summary>
    /// Executes the Edit-menu file operations (Cut/Copy/Paste/Delete) on the selected results of
    /// the expanded search window. All failures are logged, never thrown; nothing runs unless a
    /// user gesture (menu item or keyboard shortcut) triggers it.
    /// </summary>
    public sealed class EditOperationsController
    {
        private static readonly ILogger Logger = ToolbarLogger.GetLogger<EditOperationsController>();

        private readonly FileOperationsService _fileOperations;

        public EditOperationsController(FileOperationsService fileOperations)
        {
            _fileOperations = fileOperations;
        }

        /// <summary>Marks the selected results as cut on the clipboard (paste moves them).</summary>
        public void Cut(IReadOnlyList<SearchResult?> selection)
        {
            var paths = ShellContextMenuSelection.BuildPathList(selection);
            if (paths.Count == 0)
                return;

            Logger.Info("Cutting {0} item(s) to the clipboard.", paths.Count);
            _fileOperations.CutToClipboard(paths);
        }

        /// <summary>Copies the selected files to the clipboard (shell file copy, not path copy).</summary>
        public void Copy(IReadOnlyList<SearchResult?> selection)
        {
            var paths = ShellContextMenuSelection.BuildPathList(selection);
            if (paths.Count == 0)
                return;

            Logger.Info("Copying {0} item(s) to the clipboard.", paths.Count);
            _fileOperations.CopyToClipboard(paths);
        }

        /// <summary>
        /// Pastes the clipboard files into the paste target decided by <see cref="EditOperationPlanner"/>:
        /// move when the clipboard drop list is marked as cut, copy otherwise.
        /// </summary>
        public void Paste(IReadOnlyList<SearchResult?> selection)
        {
            if (!FileOperationsService.TryGetClipboardFiles(out var files, out var isCut))
                return;

            var plan = EditOperationPlanner.PlanPaste(selection, clipboardHasFiles: files.Count > 0);
            if (!plan.CanPaste || string.IsNullOrEmpty(plan.DestinationFolder))
                return;

            Logger.Info(
                "Pasting {0} item(s) into {1} ({2}).",
                files.Count,
                plan.DestinationFolder,
                isCut ? "move" : "copy"
            );
            if (isCut)
                _fileOperations.MoveTo(files, plan.DestinationFolder);
            else
                _fileOperations.CopyTo(files, plan.DestinationFolder);
        }

        /// <summary>Copies the selected file names to the text clipboard, one per line.</summary>
        public void CopyNames(IReadOnlyList<SearchResult?> selection) =>
            SetClipboardText(ClipboardTextBuilder.BuildNameLines(selection), "names");

        /// <summary>Copies the selected containing folder paths to the text clipboard, one per line.</summary>
        public void CopyPaths(IReadOnlyList<SearchResult?> selection) =>
            SetClipboardText(ClipboardTextBuilder.BuildPathLines(selection), "paths");

        /// <summary>Copies the selected full name and paths to the text clipboard, one per line.</summary>
        public void CopyFullPaths(IReadOnlyList<SearchResult?> selection) =>
            SetClipboardText(ClipboardTextBuilder.BuildFullPathLines(selection), "full name and paths");

        private void SetClipboardText(string text, string what)
        {
            if (string.IsNullOrEmpty(text))
                return;

            try
            {
                System.Windows.Clipboard.SetText(text);
                Logger.Info("Copied {0} to the clipboard.", what);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to copy {0} to the clipboard.", what);
            }
        }

        /// <summary>Recycle-deletes the selected results via the shell (with its own confirmation).</summary>
        public void Delete(IReadOnlyList<SearchResult?> selection)
        {
            var paths = ShellContextMenuSelection.BuildPathList(selection);
            if (paths.Count == 0)
                return;

            Logger.Info("Deleting {0} item(s) to the recycle bin.", paths.Count);
            _fileOperations.Delete(paths);
        }

        /// <summary>
        /// Renames the selected result (Everything F2 parity): enabled only when EXACTLY one result
        /// is selected — Everything renames one item at a time, multi-select rename belongs to
        /// Advanced Rename (Tools menu). Shows the rename dialog owned by the given window, then
        /// lets the shell perform the rename. Failures are logged, never thrown.
        /// </summary>
        public void Rename(IReadOnlyList<SearchResult?> selection, System.Windows.Window? owner)
        {
            var (fullPath, fileName) = EditOperationPlanner.PlanRename(selection);
            if (fullPath == null || fileName == null)
            {
                if (selection is { Count: > 1 })
                    Logger.Info("Rename skipped: multiple items are selected (use Tools > Advanced Rename).");
                return;
            }

            var dialog = new App.RenameDialog(fullPath) { Owner = owner };
            if (dialog.ShowDialog() != true)
                return;

            Logger.Info("Renaming {0} to {1}.", fullPath, dialog.NewFileName);
            _fileOperations.Rename(fullPath, dialog.NewFileName);
        }

        /// <summary>Whether Cut/Copy/Delete are enabled for the current selection.</summary>
        public bool CanActOnSelection(IReadOnlyList<SearchResult?> selection) =>
            EditOperationPlanner.CanActOnSelection(selection);

        /// <summary>Whether Paste is enabled: exactly one folder selected and files on the clipboard.</summary>
        public PastePlan GetPastePlan(IReadOnlyList<SearchResult?> selection) =>
            FileOperationsService.TryGetClipboardFiles(out var files, out _)
                ? EditOperationPlanner.PlanPaste(selection, clipboardHasFiles: files.Count > 0)
                : PastePlan.Disabled;
    }
}
