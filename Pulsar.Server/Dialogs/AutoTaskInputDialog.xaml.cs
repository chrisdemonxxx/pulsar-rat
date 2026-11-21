using System;
using System.Windows;
using System.Windows.Controls;
using Pulsar.Server.Forms;
using Pulsar.Server.Services.Wpf;

namespace Pulsar.Server.Dialogs
{
    public partial class AutoTaskInputDialog : Window
    {
        public string TaskTitle => TitleComboBox.Text?.Trim() ?? string.Empty;
        public string Param1 => Param1TextBox.Text?.Trim() ?? string.Empty;
        public string Param2 => Param2TextBox.Text?.Trim() ?? string.Empty;
        public AutoTaskExecutionMode ExecutionMode
        {
            get
            {
                var selected = (ExecutionModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                return string.Equals(selected, nameof(AutoTaskExecutionMode.EveryConnection), StringComparison.OrdinalIgnoreCase)
                    ? AutoTaskExecutionMode.EveryConnection
                    : AutoTaskExecutionMode.OncePerClient;
            }
        }

        public AutoTaskInputDialog()
        {
            InitializeComponent();
            ThemeService.ApplyThemeTo(Resources, ThemeService.Instance.CurrentTheme);
            Loaded += (_, __) => Param1TextBox.Focus();
        }

        private void Input_Changed(object sender, TextChangedEventArgs e)
        {
            UpdateSaveButton();
        }

        private void TitleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSaveButton();
        }

        private void UpdateSaveButton()
        {
            if (SaveButton != null)
            {
                SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(TitleComboBox.Text);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TaskTitle))
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
