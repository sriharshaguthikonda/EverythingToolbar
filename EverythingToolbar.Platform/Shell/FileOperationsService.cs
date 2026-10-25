// Portions adapted from Files Community (MIT) — https://github.com/files-community/Files —
// src/Files.App/Utils/Shell/ShellFileOperations2.cs (minimal IFileOperation subset only:
// Copy/Move/Delete + PerformOperations; no progress sink, no new-item operation).

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using NLog;

namespace EverythingToolbar.Platform.Shell
{
    /// <summary>
    /// Pure codec for the shell "Preferred DropEffect" clipboard format: the value Explorer
    /// reads to decide whether a pasted file drop list should be copied or moved.
    /// Kept pure so the cut/copy marking can be round-trip tested without touching the
    /// real Windows clipboard.
    /// </summary>
    public static class PreferredDropEffectCodec
    {
        public const string FormatName = "Preferred DropEffect";

        public const int CopyEffect = 1; // DROPEFFECT_COPY
        public const int MoveEffect = 2; // DROPEFFECT_MOVE

        /// <summary>Encodes the DWORD that accompanies a CF_HDROP on the clipboard.</summary>
        public static byte[] Encode(bool isCut) => BitConverter.GetBytes(isCut ? MoveEffect : CopyEffect);

        /// <summary>Decodes whether a "Preferred DropEffect" payload requests a move (cut).</summary>
        public static bool DecodeIsCut(byte[]? data) =>
            data != null && data.Length >= 4 && (BitConverter.ToInt32(data, 0) & MoveEffect) != 0;
    }

    /// <summary>
    /// Explorer-parity file operations on selected search results:
    /// shell IFileOperation for Delete (to the recycle bin, undoable via Explorer's
    /// Edit &gt; Undo), Copy and Move (used by Paste), and CF_HDROP clipboard set/read with
    /// the "Preferred DropEffect" marking that distinguishes copy from cut.
    /// All methods must run on an STA UI thread; every failure is logged and never thrown.
    /// </summary>
    public sealed class FileOperationsService
    {
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        private const uint FOF_ALLOWUNDO = 0x0040; // recycle bin + Explorer undo
        private const uint FOF_NOCONFIRMMKDIR = 0x0200;
        private const uint FOF_DEFAULT_FOR_MOVE = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR;

        private enum OperationKind
        {
            Copy,
            Move,
            Delete,
        }

        /// <summary>
        /// Deletes the given paths via the shell with FOF_ALLOWUNDO (recycle bin). The shell's
        /// own confirmation and progress dialogs are deliberately left enabled (no
        /// FOF_SILENT / FOF_NOCONFIRMDIALOG), matching Everything and Explorer.
        /// </summary>
        public void Delete(IEnumerable<string> paths) => Perform(OperationKind.Delete, paths, destinationFolder: null);

        /// <summary>Copies the given paths into the destination folder via the shell.</summary>
        public void CopyTo(IEnumerable<string> paths, string destinationFolder) =>
            Perform(OperationKind.Copy, paths, destinationFolder);

        /// <summary>Moves the given paths into the destination folder via the shell.</summary>
        public void MoveTo(IEnumerable<string> paths, string destinationFolder) =>
            Perform(OperationKind.Move, paths, destinationFolder);

        /// <summary>
        /// Puts the given paths on the clipboard as a file drop list marked for copy.
        /// Implemented with System.Windows.Clipboard (WPF DataObject, converted to OLE by WPF):
        /// CF_HDROP + "Preferred DropEffect" = DROPEFFECT_COPY.
        /// </summary>
        public void CopyToClipboard(IEnumerable<string> paths) => SetClipboard(paths, isCut: false);

        /// <summary>
        /// Puts the given paths on the clipboard as a file drop list marked for cut.
        /// CF_HDROP + "Preferred DropEffect" = DROPEFFECT_MOVE, so Explorer pastes it as a move.
        /// </summary>
        public void CutToClipboard(IEnumerable<string> paths) => SetClipboard(paths, isCut: true);

        /// <summary>
        /// Reads the clipboard file drop list and whether it is marked as cut.
        /// Returns false when the clipboard holds no file drop list; never throws.
        /// </summary>
        public static bool TryGetClipboardFiles(out IReadOnlyList<string> files, out bool isCut)
        {
            files = Array.Empty<string>();
            isCut = false;
            try
            {
                if (!Clipboard.ContainsFileDropList())
                    return false;

                var dropList = Clipboard.GetFileDropList();
                var paths = new List<string>(dropList.Count);
                foreach (var path in dropList)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                        paths.Add(path);
                }

                if (paths.Count == 0)
                    return false;

                isCut = ReadPreferredDropEffectIsCut();
                files = paths;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to read the clipboard file drop list.");
                return false;
            }
        }

        private static void SetClipboard(IEnumerable<string>? paths, bool isCut)
        {
            try
            {
                var dropList = new StringCollection();
                if (paths != null)
                {
                    foreach (var path in paths)
                    {
                        if (!string.IsNullOrWhiteSpace(path))
                            dropList.Add(path);
                    }
                }

                if (dropList.Count == 0)
                    return;

                var data = new DataObject();
                data.SetFileDropList(dropList);
                using var effect = new MemoryStream(PreferredDropEffectCodec.Encode(isCut));
                data.SetData(PreferredDropEffectCodec.FormatName, effect);
                Clipboard.SetDataObject(data, true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to put files on the clipboard.");
            }
        }

        private static bool ReadPreferredDropEffectIsCut()
        {
            try
            {
                if (Clipboard.GetData(PreferredDropEffectCodec.FormatName) is not MemoryStream stream)
                    return false;

                return PreferredDropEffectCodec.DecodeIsCut(stream.ToArray());
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to read the clipboard Preferred DropEffect; assuming copy.");
                return false;
            }
        }

        private void Perform(OperationKind kind, IEnumerable<string>? paths, string? destinationFolder)
        {
            try
            {
                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                {
                    Logger.Error("Shell file operations require an STA thread; operation skipped.");
                    return;
                }

                var items = Deduplicate(paths);
                if (items.Count == 0)
                    return;

                IFileOperation operation = (IFileOperation)new CFileOperation();
                var sourcePointers = new List<IntPtr>(items.Count);
                var destinationPointer = IntPtr.Zero;
                try
                {
                    // FOF_ALLOWUNDO routes delete to the recycle bin and makes copy/move undoable
                    // through Explorer's Edit > Undo (same delegation Everything relies on).
                    // The shell's own confirmation and progress dialogs stay enabled.
                    var hrFlags = operation.SetOperationFlags(FOF_DEFAULT_FOR_MOVE);
                    if (hrFlags < 0)
                    {
                        Logger.Error("IFileOperation.SetOperationFlags failed with HRESULT 0x{0:X8}.", hrFlags);
                        return;
                    }

                    if (kind != OperationKind.Delete)
                    {
                        if (!CreateShellItem(destinationFolder, out destinationPointer))
                            return;
                    }

                    foreach (var path in items)
                    {
                        if (!CreateShellItem(path, out var itemPointer))
                            continue;

                        sourcePointers.Add(itemPointer);
                        var hr = kind switch
                        {
                            OperationKind.Copy => operation.CopyItem(
                                itemPointer,
                                destinationPointer,
                                IntPtr.Zero,
                                IntPtr.Zero
                            ),
                            OperationKind.Move => operation.MoveItem(
                                itemPointer,
                                destinationPointer,
                                IntPtr.Zero,
                                IntPtr.Zero
                            ),
                            _ => operation.DeleteItem(itemPointer, IntPtr.Zero),
                        };
                        if (hr < 0)
                            Logger.Error("IFileOperation queue failed for {0} with HRESULT 0x{1:X8}.", path, hr);
                    }

                    if (sourcePointers.Count == 0)
                        return;

                    var hrPerform = operation.PerformOperations();
                    if (hrPerform < 0)
                    {
                        Logger.Error("IFileOperation.PerformOperations failed with HRESULT 0x{0:X8}.", hrPerform);
                        return;
                    }

                    if (operation.GetAnyOperationsAborted(out var aborted) >= 0 && aborted != 0)
                        Logger.Info("The user canceled the shell file operation.");
                }
                finally
                {
                    foreach (var pointer in sourcePointers)
                    {
                        if (pointer != IntPtr.Zero)
                            Marshal.Release(pointer);
                    }
                    if (destinationPointer != IntPtr.Zero)
                        Marshal.Release(destinationPointer);
                    Marshal.ReleaseComObject(operation);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to perform the shell file operation.");
            }
        }

        private static List<string> Deduplicate(IEnumerable<string>? paths)
        {
            var items = new List<string>();
            if (paths == null)
                return items;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                    continue;
                items.Add(path);
            }

            return items;
        }

        private static bool CreateShellItem(string? path, out IntPtr shellItem)
        {
            shellItem = IntPtr.Zero;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var iidShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"); // IShellItem
            var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, in iidShellItem, out shellItem);
            if (hr < 0 || shellItem == IntPtr.Zero)
            {
                Logger.Error("SHCreateItemFromParsingName failed for {0} with HRESULT 0x{1:X8}.", path, hr);
                return false;
            }

            return true;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            in Guid riid,
            out IntPtr ppv
        );

        // Not sealed: casting the co-class to the ComImport interface requires an unsealed class.
        [ComImport]
        [Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]
        private class CFileOperation { }

        /// <summary>Minimal IFileOperation vtable; only the members this service calls are usable.</summary>
        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
        private interface IFileOperation
        {
            [PreserveSig]
            int Advise(IntPtr pfops, out uint pdwCookie);

            [PreserveSig]
            int Unadvise(uint dwCookie);

            [PreserveSig]
            int SetOperationFlags(uint fFlags);

            [PreserveSig]
            int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

            [PreserveSig]
            int SetProgressDialog(IntPtr popd);

            [PreserveSig]
            int SetProperties(IntPtr pproparray);

            [PreserveSig]
            int SetOwnerWindow(IntPtr hwndOwner);

            [PreserveSig]
            int CopyItem(IntPtr psiItem, IntPtr psiDestinationFolder, IntPtr pszCopyName, IntPtr pfopsItem);

            [PreserveSig]
            int CopyItems(IntPtr ppsiItemArray, IntPtr psiDestinationFolder, IntPtr pfopsItem);

            [PreserveSig]
            int MoveItem(IntPtr psiItem, IntPtr psiDestinationFolder, IntPtr pszNewName, IntPtr pfopsItem);

            [PreserveSig]
            int MoveItems(IntPtr ppsiItemArray, IntPtr psiDestinationFolder, IntPtr pfopsItem);

            [PreserveSig]
            int RenameItem(IntPtr psiItem, IntPtr pszNewName, IntPtr pfopsItem);

            [PreserveSig]
            int RenameItems(IntPtr ppsiItemArray, IntPtr pszNewName, IntPtr pfopsItem);

            [PreserveSig]
            int DeleteItem(IntPtr psiItem, IntPtr pfopsItem);

            [PreserveSig]
            int DeleteItems(IntPtr ppsiItemArray, IntPtr pfopsItem);

            [PreserveSig]
            int NewItem(
                IntPtr psiDestinationFolder,
                uint dwFileAttributes,
                IntPtr pszName,
                IntPtr pszTemplateName,
                IntPtr pfopsItem
            );

            [PreserveSig]
            int PerformOperations();

            [PreserveSig]
            int GetAnyOperationsAborted(out int pfAnyOperationsAborted);
        }
    }
}
