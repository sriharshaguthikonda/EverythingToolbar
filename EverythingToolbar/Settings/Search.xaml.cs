using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using EverythingToolbar.Core.Data;
using EverythingToolbar.FuzzySearch;

namespace EverythingToolbar.Settings
{
    [ObservableObject]
    public partial class Search
    {
        private static readonly string AliasesPath = Path.Combine(
            ConfigPaths.GetConfigDirectory(),
            "aliases.json"
        );

        public ISettings Settings { get; } = Ioc.Default.GetRequiredService<ISettings>();
        private readonly SearchState _searchState = Ioc.Default.GetRequiredService<SearchState>();
        private readonly EverythingClientRouter _everythingClient =
            Ioc.Default.GetRequiredService<EverythingClientRouter>();
        private readonly AliasStore _aliasStore = Ioc.Default.GetRequiredService<AliasStore>();

        public bool IsResultOmissionsSupported => _everythingClient.IsPipeClientActive;

        public List<KeyValuePair<string, FocusBehavior>> FocusBehaviorItems { get; } =
        [
            new(Properties.Resources.FocusBehaviorClamp, FocusBehavior.Clamp),
            new(Properties.Resources.FocusBehaviorRepeat, FocusBehavior.Repeat),
            new(Properties.Resources.FocusBehaviorRepeatWithSearch, FocusBehavior.RepeatWithSearch),
        ];

        public Search()
        {
            InitializeComponent();
            DataContext = this;
            Settings.PropertyChanged += OnSettingsChanged;
            LoadAliases();
        }

        private void LoadAliases()
        {
            AliasesTextBox.Text = string.Join(
                Environment.NewLine,
                _aliasStore.GetGroups().Select(g => g.Canonical + " = " + string.Join(", ", g.Aliases))
            );
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ISettings.IsForceLegacySdk))
            {
                OnPropertyChanged(nameof(IsResultOmissionsSupported));
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Settings.PropertyChanged -= OnSettingsChanged;
        }

        private void OnClearHistoryClicked(object sender, RoutedEventArgs e)
        {
            _searchState.ClearHistory();
        }

        private void OnSaveAliasesClicked(object sender, RoutedEventArgs e)
        {
            var groups = new List<AliasGroup>();
            foreach (var rawLine in AliasesTextBox.Text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || !line.Contains('='))
                    continue;

                var canonical = line[..line.IndexOf('=')].Trim();
                var aliases = line[(line.IndexOf('=') + 1)..]
                    .Split(',')
                    .Select(a => a.Trim())
                    .Where(a => a.Length > 0)
                    .ToList();
                if (canonical.Length > 0 && aliases.Count > 0)
                    groups.Add(new AliasGroup(canonical, aliases));
            }

            var store = new AliasStore(groups);
            store.Save(AliasesPath);
            _aliasStore.Reload(groups);
        }
    }
}
