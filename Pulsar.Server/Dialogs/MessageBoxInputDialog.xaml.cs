using System.Windows;
using System.Windows.Controls;
using Pulsar.Server.Services.Wpf;

namespace Pulsar.Server.Dialogs
{
    public partial class MessageBoxInputDialog : Window
    {
        public string MessageCaption => CaptionTextBox.Text?.Trim() ?? string.Empty;
        public string MessageText => MessageTextBox.Text ?? string.Empty;
        public string SelectedButton => (ButtonComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "OK";
        public string SelectedIcon => (IconComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Information";

        public MessageBoxInputDialog(string clientDisplay)
        {
            InitializeComponent();
            ThemeService.ApplyThemeTo(Resources, ThemeService.Instance.CurrentTheme);

            TargetClientTextBlock.Text = $"Target: {(string.IsNullOrWhiteSpace(clientDisplay) ? "Selected Client" : clientDisplay)}";
            Loaded += (_, __) => MessageTextBox.Focus();
        }

        private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SendButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageTextBox.Text);
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageTextBox.Text))
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
