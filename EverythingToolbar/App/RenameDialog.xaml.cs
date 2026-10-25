using System;
using System.Windows;
using EverythingToolbar.Core.Data;

namespace EverythingToolbar.App
{
    /// <summary>
    /// Small modal dialog for renaming a single search result (F2, Everything parity). The text
    /// box is prefilled with the current file name (not the full path); the full old path is
    /// shown as a subtitle. Enter (IsDefault) confirms, Esc (IsCancel) cancels. OK stays disabled
    /// while the name is empty, unchanged, or contains characters Windows forbids. Reuses the
    /// expanded window's theme via the AppWindow surface.
    /// </summary>
    public partial class RenameDialog : Window
    {
        private readonly string _oldFileName;

        public RenameDialog(string fullPath)
        {
            InitializeComponent();
            _oldFileName = System.IO.Path.GetFileName(fullPath);
            OldPathText.Text = fullPath;
            NameBox.Text = _oldFileName;
            Loaded += (_, _) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        /// <summary>The new (trimmed) file name entered by the user.</summary>
        public string NewFileName => NameBox.Text.Trim();

        private void OnNameTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            OkButton.IsEnabled =
                WindowsPathValidator.IsValidFileName(NewFileName)
                && !string.Equals(NewFileName, _oldFileName, StringComparison.Ordinal);
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (OkButton.IsEnabled)
                DialogResult = true;
        }
    }
}
