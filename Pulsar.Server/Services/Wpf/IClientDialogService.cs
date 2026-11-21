using System;
using System.Windows;
using Pulsar.Server.Dialogs;

namespace Pulsar.Server.Services.Wpf
{
    public enum ActionRiskLevel
    {
        Critical,
        High,
        Medium
    }

    public sealed class ActionConfirmationRequest
    {
        public string Title { get; set; }
        public string ActionName { get; set; }
        public string ClientDisplay { get; set; }
        public string ClientEndpoint { get; set; }
        public string Consequence { get; set; }
        public string Recoverability { get; set; }
        public ActionRiskLevel RiskLevel { get; set; }
        public bool AllowDontAskAgain { get; set; }
    }

    public sealed class ActionConfirmationResult
    {
        public bool Confirmed { get; set; }
        public bool DontAskAgain { get; set; }
    }

    public sealed class MessageBoxInputResult
    {
        public bool Confirmed { get; set; }
        public string Caption { get; set; }
        public string Text { get; set; }
        public string Button { get; set; }
        public string Icon { get; set; }
    }

    public sealed class QuickCommandInputResult
    {
        public bool Confirmed { get; set; }
        public string Host { get; set; }
        public string Command { get; set; }
    }

    public sealed class AutoTaskInputResult
    {
        public bool Confirmed { get; set; }
        public string Title { get; set; }
        public string Param1 { get; set; }
        public string Param2 { get; set; }
        public Pulsar.Server.Forms.AutoTaskExecutionMode ExecutionMode { get; set; }
    }

    /// <summary>
    /// Abstraction for modal confirmation and parameter-entry dialogs used by MainViewModel.
    /// Enables deterministic unit testing while showing styled WPF dialogs at runtime.
    /// </summary>
    public interface IClientDialogService
    {
        ActionConfirmationResult ShowConfirmation(ActionConfirmationRequest request);
        MessageBoxInputResult ShowMessageBoxInput(string clientDisplay);
        QuickCommandInputResult ShowQuickCommandInput(string clientDisplay);
        AutoTaskInputResult ShowAutoTaskInput();
    }

    /// <summary>
    /// Default WPF dialog service that displays theme-aware modal dialogs owned by PulsarMainWindow.
    /// </summary>
    public sealed class WpfClientDialogService : IClientDialogService
    {
        private readonly Func<Window> _ownerProvider;

        public WpfClientDialogService(Func<Window> ownerProvider = null)
        {
            _ownerProvider = ownerProvider;
        }

        public ActionConfirmationResult ShowConfirmation(ActionConfirmationRequest request)
        {
            var dialog = new ConfirmationDialog(request);
            AttachOwner(dialog);
            bool confirmed = dialog.ShowDialog() == true;
            return new ActionConfirmationResult
            {
                Confirmed = confirmed,
                DontAskAgain = confirmed && dialog.DontAskAgain
            };
        }

        public MessageBoxInputResult ShowMessageBoxInput(string clientDisplay)
        {
            var dialog = new MessageBoxInputDialog(clientDisplay);
            AttachOwner(dialog);
            bool confirmed = dialog.ShowDialog() == true;
            return new MessageBoxInputResult
            {
                Confirmed = confirmed,
                Caption = dialog.MessageCaption,
                Text = dialog.MessageText,
                Button = dialog.SelectedButton,
                Icon = dialog.SelectedIcon
            };
        }

        public QuickCommandInputResult ShowQuickCommandInput(string clientDisplay)
        {
            var dialog = new QuickCommandInputDialog(clientDisplay);
            AttachOwner(dialog);
            bool confirmed = dialog.ShowDialog() == true;
            return new QuickCommandInputResult
            {
                Confirmed = confirmed,
                Host = dialog.SelectedHost,
                Command = dialog.CommandText
            };
        }

        public AutoTaskInputResult ShowAutoTaskInput()
        {
            var dialog = new AutoTaskInputDialog();
            AttachOwner(dialog);
            bool confirmed = dialog.ShowDialog() == true;
            return new AutoTaskInputResult
            {
                Confirmed = confirmed,
                Title = dialog.TaskTitle,
                Param1 = dialog.Param1,
                Param2 = dialog.Param2,
                ExecutionMode = dialog.ExecutionMode
            };
        }

        private void AttachOwner(Window dialog)
        {
            var owner = _ownerProvider?.Invoke();
            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
    }
}
