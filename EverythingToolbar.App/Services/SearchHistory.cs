using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using EverythingToolbar.App.Helpers;
using NLog;

namespace EverythingToolbar.App.Services
{
    public class SearchHistory
    {
        private static readonly ILogger Logger = ToolbarLogger.GetLogger<SearchHistory>();
        private static readonly string DefaultHistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EverythingToolbar",
            "history.xml"
        );

        private readonly string _historyPath;

        private const int MaxHistorySize = 50;

        private int _currentHistorySize;
        private int _currentIndex;
        private readonly List<string> _history;
        private readonly ISettings _settings;

        private readonly object _saveGate = new();
        private Task _saveTask = Task.CompletedTask;

        public SearchHistory(ISettings settings)
            : this(settings, DefaultHistoryPath) { }

        /// <summary>
        /// Injectable history path for tests (same class otherwise); internal so the DI container
        /// keeps resolving through the single public constructor.
        /// </summary>
        internal SearchHistory(ISettings settings, string historyPath)
        {
            _historyPath = historyPath;
            _settings = settings;
            _history = LoadHistory();
            _currentIndex = _history.Count;
            _currentHistorySize = _settings.IsEnableHistory ? MaxHistorySize : 0;
            _settings.PropertyChanged += OnSettingChanged;
        }

        private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ISettings.IsEnableHistory))
            {
                if (_settings.IsEnableHistory)
                {
                    _currentHistorySize = MaxHistorySize;
                }
                else
                {
                    _currentHistorySize = 0;
                    ClearHistory();
                }
            }
        }

        private List<string> LoadHistory()
        {
            if (!File.Exists(_historyPath))
                return [];

            try
            {
                var serializer = new XmlSerializer(typeof(List<string>));
                using var reader = XmlReader.Create(_historyPath);
                return serializer.Deserialize(reader) as List<string> ?? [];
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load search term history.");
            }

            return [];
        }

        private void SaveHistory()
        {
            var snapshot = new List<string>(_history);

            lock (_saveGate)
                _saveTask = _saveTask.ContinueWith(_ => WriteHistory(snapshot), TaskScheduler.Default);
        }

        private void WriteHistory(List<string> history)
        {
            try
            {
                if (Path.GetDirectoryName(_historyPath) is { } path)
                    Directory.CreateDirectory(path);

                var serializer = new XmlSerializer(typeof(List<string>));
                using var writer = XmlWriter.Create(_historyPath);
                serializer.Serialize(writer, history);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to save search term history.");
            }
        }

        public void ClearHistory()
        {
            _history.Clear();

            SaveHistory();
        }

        public void AddToHistory(string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return;

            if (_history.Count > 0 && _history.Last() == searchTerm)
                return;

            _history.Add(searchTerm);
            while (_history.Count > _currentHistorySize)
                _history.RemoveAt(0);
            _currentIndex = _history.Count;

            SaveHistory();
        }

        /// <summary>
        /// Imports Everything search history strings (file order = most-recent-first) into the
        /// existing history. Chosen over reusing AddToHistory because that only dedupes against
        /// the single last item and cannot batch; this dedupes case-insensitively against the
        /// whole history, appends imported terms after the existing ones (the list is
        /// oldest-to-newest, so the newest imported term ends up last), trims to MaxHistorySize
        /// dropping the oldest entries first, persists via the normal save path, and returns the
        /// number of terms actually added. A no-op when history is disabled.
        /// </summary>
        internal int Import(IEnumerable<string>? searches)
        {
            if (searches is null || !_settings.IsEnableHistory)
                return 0;

            var known = new HashSet<string>(_history, StringComparer.OrdinalIgnoreCase);
            var added = 0;

            foreach (var searchTerm in searches)
            {
                if (string.IsNullOrEmpty(searchTerm) || !known.Add(searchTerm))
                    continue;

                _history.Add(searchTerm);
                added++;
            }

            if (added == 0)
                return 0;

            while (_history.Count > MaxHistorySize)
                _history.RemoveAt(0);
            _currentIndex = _history.Count;

            SaveHistory();
            return added;
        }

        /// <summary>Blocks until any pending background save has completed (test seam).</summary>
        internal void WaitForPendingSave()
        {
            lock (_saveGate)
                _saveTask.Wait(TimeSpan.FromSeconds(5));
        }

        /// <summary>Read-only copy of the current history (test seam), oldest to newest.</summary>
        internal IReadOnlyList<string> GetHistorySnapshot() => _history.ToArray();

        public string GetPreviousItem()
        {
            if (_history.Count == 0)
                return "";

            _currentIndex = Math.Max(0, _currentIndex - 1);
            return _history.ElementAt(_currentIndex);
        }

        public string GetNextItem()
        {
            if (_history.Count == 0)
                return "";

            if (_currentIndex >= _history.Count - 1)
            {
                _currentIndex = _history.Count;
                return "";
            }

            _currentIndex = Math.Min(_currentIndex + 1, _history.Count - 1);
            return _history.ElementAt(_currentIndex);
        }
    }
}
