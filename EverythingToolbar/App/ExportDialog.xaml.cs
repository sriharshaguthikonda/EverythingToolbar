using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using EverythingToolbar.Core.Search;

namespace EverythingToolbar.App
{
    /// <summary>
    /// Everything-parity Export dialog (v1, English-only strings). Lets the user pick the export
    /// format (CSV/TXT/EFU) and, for CSV, which columns to include via a checked list whose
    /// order is the fixed display order of <see cref="ResultExporter.AvailableColumns"/> (the
    /// checked set is the choice; drag reordering is not offered). TXT/EFU have fixed layouts,
    /// so the column list is hidden for them. The footer shows the current result count.
    /// Export applies the selection via <see cref="SelectedFormat"/>/<see cref="SelectedColumns"/>
    /// and closes with DialogResult = true; the caller then runs the SaveFileDialog and the
    /// streaming export. Esc (IsCancel) closes without exporting.
    /// </summary>
    public partial class ExportDialog : Window
    {
        private readonly Dictionary<string, CheckBox> _columnChecks = new();

        /// <summary>The format chosen via the radio group.</summary>
        public ExportFormat SelectedFormat { get; private set; } = ExportFormat.Csv;

        /// <summary>
        /// The checked columns in fixed display order (CSV only; equals
        /// <see cref="ResultExporter.DefaultCsvColumns"/> unless the user changed the checks).
        /// </summary>
        public IReadOnlyList<ExportColumn> SelectedColumns { get; private set; } = ResultExporter.DefaultCsvColumns;

        public ExportDialog(int resultCount)
        {
            InitializeComponent();
            ResultCountText.Text = $"Exports {resultCount} results";

            var defaultHeaders = ResultExporter.DefaultCsvColumns.Select(c => c.Header).ToHashSet();
            foreach (var column in ResultExporter.AvailableColumns)
            {
                var checkBox = new CheckBox
                {
                    Content = column.Header,
                    IsChecked = defaultHeaders.Contains(column.Header),
                    Margin = new Thickness(0, 2, 0, 2),
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                checkBox.Checked += OnColumnCheckChanged;
                checkBox.Unchecked += OnColumnCheckChanged;
                _columnChecks[column.Header] = checkBox;
                ColumnsPanel.Children.Add(checkBox);
            }
        }

        private void OnFormatChanged(object sender, RoutedEventArgs e)
        {
            if (CsvRadio.IsChecked == true)
                SelectedFormat = ExportFormat.Csv;
            else if (TxtRadio.IsChecked == true)
                SelectedFormat = ExportFormat.Txt;
            else
                SelectedFormat = ExportFormat.Efu;

            // Only CSV has a selectable column set; TXT/EFU layouts are fixed.
            ColumnsSection.Visibility = SelectedFormat == ExportFormat.Csv ? Visibility.Visible : Visibility.Collapsed;
            UpdateExportEnabled();
        }

        private void OnColumnCheckChanged(object sender, RoutedEventArgs e)
        {
            UpdateExportEnabled();
        }

        private void UpdateExportEnabled()
        {
            ExportButton.IsEnabled = SelectedFormat != ExportFormat.Csv || GetCheckedColumns().Count > 0;
        }

        private List<ExportColumn> GetCheckedColumns()
        {
            return ResultExporter
                .AvailableColumns.Where(column => _columnChecks[column.Header].IsChecked == true)
                .ToList();
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (SelectedFormat == ExportFormat.Csv)
            {
                var columns = GetCheckedColumns();
                if (columns.Count == 0)
                    return;
                SelectedColumns = columns;
            }

            DialogResult = true;
        }
    }
}
