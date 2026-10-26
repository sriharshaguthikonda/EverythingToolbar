using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using EverythingToolbar.Core.Search;

namespace EverythingToolbar.App
{
    /// <summary>
    /// Form-based Advanced Search dialog (v1, English-only strings). Composes an Everything
    /// 1.5 query string via <see cref="AdvancedSearchQuery.Compose"/> and assigns it to
    /// SearchState.SearchTerm, which rebuilds the search session in whichever window is
    /// active (self-contained syntax, works in the popup and the expanded window alike).
    /// Size fields are plain bytes; non-numeric size text is treated as an empty field
    /// (the composer does not validate content). Search stays disabled while every field
    /// is empty; Clear empties all fields; Esc (IsCancel) closes without searching.
    /// </summary>
    public partial class AdvancedSearchDialog : Window
    {
        private readonly SearchState _searchState;

        public AdvancedSearchDialog(SearchState searchState)
        {
            InitializeComponent();
            _searchState = searchState;
            UpdateSearchEnabled();
        }

        /// <summary>The fields as currently entered (empty/whitespace text becomes null).</summary>
        public AdvancedSearchFields CollectFields()
        {
            return new AdvancedSearchFields(
                Filename: NullIfEmpty(FilenameBox.Text),
                PathContains: NullIfEmpty(PathBox.Text),
                Extension: NullIfEmpty(ExtensionBox.Text),
                SizeMinBytes: ParseBytes(SizeMinBox.Text),
                SizeMaxBytes: ParseBytes(SizeMaxBox.Text),
                DateModifiedFrom: DateModifiedFromPicker.SelectedDate,
                DateModifiedTo: DateModifiedToPicker.SelectedDate,
                DateCreatedFrom: DateCreatedFromPicker.SelectedDate,
                DateCreatedTo: DateCreatedToPicker.SelectedDate,
                Attributes: NullIfEmpty(AttributesBox.Text)
            );
        }

        private void OnFieldTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSearchEnabled();
        }

        private void OnFieldDateChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateSearchEnabled();
        }

        private void UpdateSearchEnabled()
        {
            var fields = CollectFields();
            SearchButton.IsEnabled =
                fields.Filename is not null
                || fields.PathContains is not null
                || fields.Extension is not null
                || fields.SizeMinBytes is not null
                || fields.SizeMaxBytes is not null
                || fields.DateModifiedFrom is not null
                || fields.DateModifiedTo is not null
                || fields.DateCreatedFrom is not null
                || fields.DateCreatedTo is not null
                || fields.Attributes is not null;
        }

        private void OnSearchClick(object sender, RoutedEventArgs e)
        {
            var query = AdvancedSearchQuery.Compose(CollectFields());
            System.Diagnostics.Debug.Assert(query is not null, "Search is disabled while all fields are empty.");
            if (query is null)
                return;

            _searchState.SearchTerm = query;
            DialogResult = true;
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            FilenameBox.Text = "";
            PathBox.Text = "";
            ExtensionBox.Text = "";
            SizeMinBox.Text = "";
            SizeMaxBox.Text = "";
            AttributesBox.Text = "";
            DateModifiedFromPicker.SelectedDate = null;
            DateModifiedToPicker.SelectedDate = null;
            DateCreatedFromPicker.SelectedDate = null;
            DateCreatedToPicker.SelectedDate = null;

            UpdateSearchEnabled();
        }

        private static string? NullIfEmpty(string text)
        {
            var trimmed = text.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static long? ParseBytes(string text)
        {
            return long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
                ? bytes
                : null;
        }
    }
}
