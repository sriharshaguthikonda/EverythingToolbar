using System.Collections.Generic;
using System.IO;
using EverythingToolbar.App.Helpers;
using EverythingToolbar.Core.Data;
using EverythingToolbar.Core.Platform;
using NLog;

namespace EverythingToolbar.App.Search
{
    public sealed class EverythingSearchLauncher(
        INotifier notifier,
        IShellDialogs shellDialogs,
        IFileLauncher fileLauncher,
        ISettings settings
    )
    {
        private static readonly ILogger Logger = ToolbarLogger.GetLogger<EverythingSearchLauncher>();

        /// <summary>
        /// Launches the configured Everything.exe with optional arguments (never throws).
        /// With no arguments this opens/activates Everything's main search window.
        /// </summary>
        public void Launch(IReadOnlyList<string>? arguments = null)
        {
            if (!EnsureEverythingPath())
                return;

            var args = "";
            if (!string.IsNullOrEmpty(settings.InstanceName))
                args += " -instance \"" + settings.InstanceName + "\"";
            if (arguments is { Count: > 0 })
                args += " " + string.Join(" ", arguments);

            Logger.Debug("Launching Everything with args:" + args);
            fileLauncher.OpenWithArguments(settings.EverythingPath, args);
        }

        public void OpenSearchInEverything(SearchState searchState, string filenameToHighlight = "")
        {
            if (!EnsureEverythingPath())
                return;

            var searchTerm = searchState.BuildSearchTerm();
            var args = "";
            if (!string.IsNullOrEmpty(settings.InstanceName))
                args += " -instance \"" + settings.InstanceName + "\"";
            if (!string.IsNullOrEmpty(filenameToHighlight))
                args += " -select \"" + filenameToHighlight + "\"";
            args += " -sort \"" + searchState.EffectiveSortBy.ToCliName() + "\"";
            args += searchState.EffectiveIsSortDescending ? " -sort-descending" : " -sort-ascending";
            args += searchState.IsMatchCase ? " -case" : " -nocase";
            args += searchState.IsMatchPath ? " -matchpath" : " -nomatchpath";
            args += searchState.IsMatchWholeWord && !searchState.IsRegExEnabled ? " -ww" : " -noww";
            args += searchState.IsRegExEnabled ? " -regex" : " -noregex";
            args += " -s \"" + searchTerm.Replace("\"", "\"\"") + "\"";

            Logger.Debug("Showing in Everything with args: " + args);
            fileLauncher.OpenWithArguments(settings.EverythingPath, args);
        }

        private bool EnsureEverythingPath()
        {
            if (File.Exists(settings.EverythingPath))
                return true;

            notifier.ShowInformation("MessageBoxSelectEverythingExe");

            var pickedPath = shellDialogs.BrowseForFile("Everything.exe", "Everything.exe", "c:\\");
            if (pickedPath != null)
            {
                settings.EverythingPath = pickedPath;
                return true;
            }

            return false;
        }
    }
}
