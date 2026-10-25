using System.Collections.Generic;
using System.Windows.Input;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.Platform.Shell
{
    /// <summary>Edit-menu file actions on the selected results, Everything-parity.</summary>
    public enum EditFileAction
    {
        None,
        Cut,
        Copy,
        Paste,
        Rename,
        Delete,
        CopyFullPath,
        CopyPath,
        CopyName,
    }

    /// <summary>Outcome of the paste-target decision: the destination folder and whether paste is enabled.</summary>
    public sealed record PastePlan(bool CanPaste, string? DestinationFolder)
    {
        public static readonly PastePlan Disabled = new(false, null);
    }

    /// <summary>
    /// Pure decision helpers for the expanded search window's Edit menu. No COM, no clipboard
    /// access — the clipboard state is passed in, so tests never touch the real Windows clipboard.
    /// </summary>
    public static class EditOperationPlanner
    {
        /// <summary>Cut, Copy and Delete act on the selection; enabled when anything is selected.</summary>
        public static bool CanActOnSelection(IEnumerable<SearchResult?>? selection)
        {
            if (selection == null)
                return false;

            foreach (var result in selection)
            {
                if (result != null && !string.IsNullOrWhiteSpace(result.FullPathAndFileName))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Everything parity: Rename (F2) targets exactly one selected result — Everything renames
        /// one item at a time; multi-select rename belongs to Advanced Rename in the Tools menu.
        /// Returns the single target's full path and file name, or null when rename is disabled.
        /// </summary>
        public static (string? FullPath, string? FileName) PlanRename(IEnumerable<SearchResult?>? selection)
        {
            if (selection == null)
                return (null, null);

            SearchResult? single = null;
            var count = 0;
            foreach (var result in selection)
            {
                if (result == null || string.IsNullOrWhiteSpace(result.FullPathAndFileName))
                    return (null, null);

                count++;
                single = result;
            }

            if (count != 1)
                return (null, null);

            return (single!.FullPathAndFileName, single.FileName);
        }

        /// <summary>
        /// Everything parity: Paste targets the selected folder when exactly one FOLDER result is
        /// selected and the clipboard holds files; otherwise paste is disabled.
        /// </summary>
        public static PastePlan PlanPaste(IEnumerable<SearchResult?>? selection, bool clipboardHasFiles)
        {
            if (!clipboardHasFiles || selection == null)
                return PastePlan.Disabled;

            SearchResult? single = null;
            var count = 0;
            foreach (var result in selection)
            {
                if (result == null || string.IsNullOrWhiteSpace(result.FullPathAndFileName))
                    return PastePlan.Disabled;

                count++;
                single = result;
            }

            if (count != 1 || single!.IsFile)
                return PastePlan.Disabled;

            return new PastePlan(true, single.FullPathAndFileName);
        }

        /// <summary>
        /// Gesture table for the Edit actions (Everything semantics): Ctrl+X cut, Ctrl+C copy
        /// files, Ctrl+V paste, Delete recycle-delete, Ctrl+Shift+C copy full name and path,
        /// Ctrl+Shift+N copy names, Ctrl+Shift+P copy folder paths, F2 rename the single selection.
        /// Everything else, including modified Delete and Shift+modified Ctrl+C, maps to None.
        /// </summary>
        public static EditFileAction MapGesture(Key key, ModifierKeys modifiers)
        {
            if (key == Key.F2)
                return modifiers == ModifierKeys.None ? EditFileAction.Rename : EditFileAction.None;

            if (key == Key.Delete)
                return modifiers == ModifierKeys.None ? EditFileAction.Delete : EditFileAction.None;

            if (key == Key.C && modifiers == ModifierKeys.Control)
                return EditFileAction.Copy;
            if (key == Key.C && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                return EditFileAction.CopyFullPath;
            if (key == Key.N && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                return EditFileAction.CopyName;
            if (key == Key.P && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                return EditFileAction.CopyPath;
            if (key == Key.X && modifiers == ModifierKeys.Control)
                return EditFileAction.Cut;
            if (key == Key.V && modifiers == ModifierKeys.Control)
                return EditFileAction.Paste;

            return EditFileAction.None;
        }
    }
}
