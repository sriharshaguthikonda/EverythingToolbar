using System;
using System.Collections.Generic;
using EverythingToolbar.App.SettingsSearch;
using EverythingToolbar.Properties;

namespace EverythingToolbar.Settings
{
    /// <summary>
    /// Searchable metadata for every built-in Preferences setting. Titles/help/page names resolve
    /// through Resources at query time, so search follows the active UI language and the strings
    /// always come from the same source the pages display. Keywords only add synonyms the help
    /// text does not already contain. A new setting becomes searchable by adding an entry here
    /// next to its XAML; entries without a SettingItem anchor navigate to the page only.
    /// </summary>
    public static class SettingsSearchIndex
    {
        public static IReadOnlyList<SettingsSearchEntry> Build() =>
            new[]
            {
                // Search page
                Entry(
                    () => Resources.SettingsHideEmptyResults,
                    () => Resources.SettingsHideEmptyResultsHelp,
                    typeof(Search),
                    new[] { "empty", "results" }
                ),
                Entry(
                    () => Resources.SettingsSearchAsYouType,
                    () => Resources.SettingsSearchAsYouTypeHelp,
                    typeof(Search),
                    new[] { "live", "instant", "incremental" }
                ),
                Entry(
                    () => Resources.SettingsTypoTolerantSearch,
                    () => Resources.SettingsTypoTolerantSearchHelp,
                    typeof(Search),
                    new[] { "spelling", "typo", "fuzzy", "misspelled", "correction", "distance", "index" }
                ),
                Entry(
                    () => Resources.SettingsTypoMaxIndexDistance,
                    () => Resources.SettingsTypoMaxIndexDistanceHelp,
                    typeof(Search),
                    new[] { "spelling", "distance", "index", "typo", "edit" }
                ),
                Entry(
                    () => Resources.SettingsTypoLongWordDistance,
                    () => Resources.SettingsTypoLongWordDistanceHelp,
                    typeof(Search),
                    new[] { "long", "word", "distance", "spelling", "typo" }
                ),
                Entry(
                    () => Resources.SettingsTypoLongWordThreshold,
                    () => Resources.SettingsTypoLongWordThresholdHelp,
                    typeof(Search),
                    new[] { "long", "word", "length", "characters", "threshold" }
                ),
                Entry(
                    () => Resources.SettingsPreferredSpellings,
                    () => Resources.SettingsPreferredSpellingsHelp,
                    typeof(Search),
                    new[] { "alias", "aliases", "spelling" }
                ),
                Entry(
                    () => Resources.SettingsSelectFirstResult,
                    () => Resources.SettingsSelectFirstResultHelp,
                    typeof(Search)
                ),
                Entry(
                    () => Resources.SettingsHomeEndNavigateResults,
                    () => Resources.SettingsHomeEndNavigateResultsHelp,
                    typeof(Search),
                    new[] { "keyboard", "keys", "navigation" }
                ),
                Entry(
                    () => Resources.SettingsListFocusBehavior,
                    () => Resources.SettingsListFocusBehaviorHelp,
                    typeof(Search),
                    new[] { "focus", "wrap", "clamp" }
                ),
                Entry(
                    () => Resources.SettingsDoubleClickToOpen,
                    () => Resources.SettingsDoubleClickHelp,
                    typeof(Search),
                    new[] { "mouse", "open" }
                ),
                Entry(
                    () => Resources.SettingsEnableResultOmissions,
                    () => Resources.SettingsEnableResultOmissionsHelp,
                    typeof(Search),
                    new[] { "omit", "omissions", "hide" }
                ),
                Entry(
                    () => Resources.SettingsEnableHistory,
                    () => Resources.SettingsHistoryHelp,
                    typeof(Search),
                    new[] { "history", "recent", "clear" }
                ),
                // User interface page
                Entry(
                    () => Resources.SettingsView,
                    () => Resources.SettingsViewLayoutHelp,
                    typeof(UserInterface),
                    new[] { "theme", "appearance", "layout" }
                ),
                Entry(
                    () => Resources.SettingsUILanguage,
                    () => Resources.SettingsUILanguageHelp,
                    typeof(UserInterface),
                    new[] { "language", "translation", "localization" }
                ),
                Entry(
                    () => Resources.SettingsSearchWindowBackground,
                    () => Resources.SettingsSearchWindowBackgroundHelp,
                    typeof(UserInterface),
                    new[] { "theme", "appearance", "acrylic", "mica", "background" }
                ),
                Entry(
                    () => Resources.SettingsThumbnailsEnabled,
                    () => Resources.SettingsThumbnailsHelp,
                    typeof(UserInterface),
                    new[] { "icons", "images", "preview" }
                ),
                Entry(
                    () => Resources.SettingsSystemContextMenuDefault,
                    () => Resources.SettingsSystemContextMenuDefaultHelp,
                    typeof(UserInterface),
                    new[] { "context menu" }
                ),
                Entry(
                    () => Resources.SettingsPreviewPaneEnabled,
                    () => Resources.SettingsPreviewPaneHelp,
                    typeof(UserInterface),
                    new[] { "preview", "pane" }
                ),
                Entry(
                    () => Resources.SettingsShowResultsCount,
                    () => Resources.SettingsResultsCountHelp,
                    typeof(UserInterface),
                    new[] { "count", "number" }
                ),
                Entry(
                    () => Resources.SettingsShowQuickToggles,
                    () => Resources.SettingsQuickTogglesHelp,
                    typeof(UserInterface),
                    new[] { "toolbar", "toggles" }
                ),
                Entry(
                    () => Resources.SettingsDisableAnimations,
                    () => Resources.SettingsAnimationsHelp,
                    typeof(UserInterface),
                    new[] { "animation", "performance" }
                ),
                // Filters page
                Entry(
                    () => Resources.SettingsRememberFilter,
                    () => Resources.SettingsRememberFilterHelp,
                    typeof(Filters)
                ),
                Entry(
                    () => Resources.SettingsUseEverythingFilters,
                    () => Resources.SettingsEverythingFiltersHelp,
                    typeof(Filters)
                ),
                Entry(
                    () => Resources.SettingsMaxTabItems,
                    () => Resources.SettingsMaxTabItemsHelp,
                    typeof(Filters),
                    new[] { "tabs" }
                ),
                // Custom actions page (a list editor, no single SettingItem anchor)
                new SettingsSearchEntry(
                    () => Resources.SettingsCustomActions,
                    () => Resources.CustomActionsHelpText,
                    () => Resources.SettingsCustomActions,
                    typeof(CustomActions),
                    null,
                    new[] { "actions", "regex", "command" }
                ),
                // Shortcuts page
                Entry(
                    () => Resources.SettingsOpenSearchWindow,
                    () => Resources.SettingsOpenSearchWindowHelp,
                    typeof(Shortcuts),
                    new[] { "hotkey", "shortcut", "key", "open" }
                ),
                Entry(
                    () => Resources.ShortcutNavigateResults,
                    () => Resources.SettingsOtherShortcuts,
                    typeof(Shortcuts),
                    new[] { "keyboard", "keys" }
                ),
                Entry(
                    () => Resources.ShortcutNavigateHistory,
                    () => Resources.SettingsOtherShortcuts,
                    typeof(Shortcuts),
                    new[] { "keyboard", "keys" }
                ),
                Entry(() => Resources.ShortcutOpen, () => Resources.SettingsOtherShortcuts, typeof(Shortcuts)),
                Entry(() => Resources.ShortcutOpenPath, () => Resources.SettingsOtherShortcuts, typeof(Shortcuts)),
                Entry(
                    () => Resources.ShortcutOpenInEverything,
                    () => Resources.SettingsOtherShortcuts,
                    typeof(Shortcuts)
                ),
                Entry(() => Resources.ShortcutRunAsAdmin, () => Resources.SettingsOtherShortcuts, typeof(Shortcuts)),
                // Advanced page
                Entry(
                    () => Resources.SettingsCheckForUpdates,
                    () => Resources.SettingsUpdatesHelp,
                    typeof(Advanced),
                    new[] { "update", "version" }
                ),
                Entry(
                    () => Resources.SettingsInstanceName,
                    () => Resources.SettingsInstanceNameHelp,
                    typeof(Advanced),
                    new[] { "everything", "instance" }
                ),
                Entry(
                    () => Resources.SettingsReplaceStartMenuSearch,
                    () => Resources.SettingsReplaceStartMenuHelp,
                    typeof(Advanced),
                    new[] { "start menu", "windows search" }
                ),
                Entry(
                    () => Resources.SettingsEnableAutostart,
                    () => Resources.SettingsEnableAutostartHelp,
                    typeof(Advanced),
                    new[] { "startup", "boot", "login" }
                ),
                Entry(
                    () => Resources.SettingsForceWin10Styles,
                    () => Resources.SettingsForceWin10StylesHelp,
                    typeof(Advanced),
                    new[] { "theme", "windows 10", "styles" }
                ),
                Entry(
                    () => Resources.SettingsForceLegacySdk,
                    () => Resources.SettingsForceLegacySdkHelp,
                    typeof(Advanced),
                    new[] { "legacy", "compatibility", "sdk" }
                ),
            };

        private static SettingsSearchEntry Entry(
            Func<string> title,
            Func<string> helpText,
            Type pageType,
            string[]? keywords = null
        )
        {
            return new SettingsSearchEntry(title, helpText, () => PageNameFor(pageType), pageType, title, keywords);
        }

        private static string PageNameFor(Type pageType)
        {
            if (pageType == typeof(Search))
                return Resources.SettingsSearch;
            if (pageType == typeof(UserInterface))
                return Resources.SettingsUserInterface;
            if (pageType == typeof(Filters))
                return Resources.SettingsFilters;
            if (pageType == typeof(CustomActions))
                return Resources.SettingsCustomActions;
            if (pageType == typeof(Shortcuts))
                return Resources.SettingsShortcuts;
            if (pageType == typeof(Advanced))
                return Resources.SettingsAdvanced;
            return Resources.SettingsHome;
        }
    }
}
