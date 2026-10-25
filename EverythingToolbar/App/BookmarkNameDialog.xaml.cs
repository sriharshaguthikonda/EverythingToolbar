using System.Windows;

namespace EverythingToolbar.App
{
    /// <summary>
    /// Small modal dialog for naming a bookmark. Prefilled with the current query text;
    /// Enter (IsDefault) confirms, Esc (IsCancel) cancels. OK stays disabled while the
    /// trimmed name is empty. Reuses the expanded window's theme via the AppWindow surface.
    /// </summary>
    public partial class BookmarkNameDialog : Window
    {
        public BookmarkNameDialog(string initialName)
        {
            InitializeComponent();
            NameBox.Text = initialName;
            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        /// <summary>The trimmed bookmark name entered by the user.</summary>
        public string BookmarkName => NameBox.Text.Trim();

        private void OnNameTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            OkButton.IsEnabled = BookmarkName.Length > 0;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (BookmarkName.Length == 0)
                return;

            DialogResult = true;
        }
    }
}
