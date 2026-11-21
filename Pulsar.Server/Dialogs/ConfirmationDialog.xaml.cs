using System.Windows;
using System.Windows.Media;
using Pulsar.Server.Services.Wpf;

namespace Pulsar.Server.Dialogs
{
    public partial class ConfirmationDialog : Window
    {
        public bool DontAskAgain => DontAskAgainCheckBox.IsChecked == true;

        public ConfirmationDialog(ActionConfirmationRequest request)
        {
            InitializeComponent();
            ThemeService.ApplyThemeTo(Resources, ThemeService.Instance.CurrentTheme);

            if (request == null)
            {
                return;
            }

            Title = string.IsNullOrWhiteSpace(request.Title) ? "Confirm System Control" : request.Title;
            TitleTextBlock.Text = Title;

            string clientLabel = string.IsNullOrWhiteSpace(request.ClientDisplay) ? "Selected Client" : request.ClientDisplay;
            if (!string.IsNullOrWhiteSpace(request.ClientEndpoint))
            {
                clientLabel = $"{clientLabel} ({request.ClientEndpoint})";
            }

            ClientTextBlock.Text = clientLabel;
            ActionTextBlock.Text = request.ActionName ?? "Operation";
            ConsequenceTextBlock.Text = request.Consequence ?? "This operation affects the remote machine.";
            RecoverabilityTextBlock.Text = request.Recoverability ?? "Depends on remote host state.";
            PromptTextBlock.Text = $"Are you sure you want to execute {ActionTextBlock.Text.ToUpperInvariant()} on {clientLabel}?";
            ConfirmButton.Content = $"Confirm {ActionTextBlock.Text}";

            bool isCritical = request.RiskLevel == ActionRiskLevel.Critical;
            RiskBadgeText.Text = isCritical ? "CRITICAL" : request.RiskLevel == ActionRiskLevel.High ? "HIGH RISK" : "CONFIRM";

            if (!isCritical)
            {
                RiskBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B));
                RiskBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xF5, 0x9E, 0x0B));
                RiskBadgeText.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            }

            // CRITICAL operations always require explicit confirmation and default focus to Cancel
            bool canSuppress = !isCritical && request.AllowDontAskAgain;
            DontAskAgainCheckBox.Visibility = canSuppress ? Visibility.Visible : Visibility.Collapsed;

            Loaded += (_, __) =>
            {
                if (isCritical)
                {
                    CancelButton.Focus();
                }
                else
                {
                    ConfirmButton.Focus();
                }
            };
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
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
