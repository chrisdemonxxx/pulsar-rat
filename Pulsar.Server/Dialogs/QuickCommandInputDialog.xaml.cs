using System.Windows;
using System.Windows.Controls;
using Pulsar.Server.Services.Wpf;

namespace Pulsar.Server.Dialogs
{
    public partial class QuickCommandInputDialog : Window
    {
        public string SelectedHost => string.IsNullOrWhiteSpace(HostComboBox.Text) ? "cmd.exe" : HostComboBox.Text.Trim();
        public string CommandText => CommandTextBox.Text ?? string.Empty;

        public QuickCommandInputDialog(string clientDisplay)
        {
            InitializeComponent();
            ThemeService.ApplyThemeTo(Resources, ThemeService.Instance.CurrentTheme);

            TargetClientTextBlock.Text = $"Target: {(string.IsNullOrWhiteSpace(clientDisplay) ? "Selected Client" : clientDisplay)}";
            Loaded += (_, __) => CommandTextBox.Focus();
        }

        private void CommandTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ExecuteButton.IsEnabled = !string.IsNullOrWhiteSpace(CommandTextBox.Text);
        }

        private void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CommandTextBox.Text))
            {
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
