using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Pulsar.Server.Controls.Wpf
{
    public sealed class HVNCWorkspaceViewModel : INotifyPropertyChanged
    {
        private string _sessionState = "Ready";
        private string _streamState = "Idle";
        private string _launchState = "Idle";
        private string _selectedMonitor = "Display 1";
        private int _quality = 100;
        private double _captureFps;
        private double _renderFps;
        private int _backlog;
        private int _maximumBacklog;
        private bool _commandRailVisible = true;
        private bool _inspectorVisible = true;
        private bool _launchCenterVisible;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<string> Monitors { get; } =
            new ObservableCollection<string> { "Display 1" };

        public ObservableCollection<HVNCLaunchCardViewModel> Launches { get; } =
            new ObservableCollection<HVNCLaunchCardViewModel>();

        public string SessionState
        {
            get => _sessionState;
            set => SetProperty(ref _sessionState, value);
        }

        public string StreamState
        {
            get => _streamState;
            set => SetProperty(ref _streamState, value);
        }

        public string LaunchState
        {
            get => _launchState;
            set => SetProperty(ref _launchState, value);
        }

        public string SelectedMonitor
        {
            get => _selectedMonitor;
            set => SetProperty(ref _selectedMonitor, value);
        }

        public int Quality
        {
            get => _quality;
            set => SetProperty(ref _quality, value);
        }

        public double CaptureFps
        {
            get => _captureFps;
            set => SetProperty(ref _captureFps, value);
        }

        public double RenderFps
        {
            get => _renderFps;
            set => SetProperty(ref _renderFps, value);
        }

        public int Backlog
        {
            get => _backlog;
            set => SetProperty(ref _backlog, value);
        }

        public int MaximumBacklog
        {
            get => _maximumBacklog;
            set => SetProperty(ref _maximumBacklog, value);
        }

        public bool CommandRailVisible
        {
            get => _commandRailVisible;
            set => SetProperty(ref _commandRailVisible, value);
        }

        public bool InspectorVisible
        {
            get => _inspectorVisible;
            set => SetProperty(ref _inspectorVisible, value);
        }

        public bool LaunchCenterVisible
        {
            get => _launchCenterVisible;
            set => SetProperty(ref _launchCenterVisible, value);
        }

        public void SetMonitors(string[] monitors)
        {
            Monitors.Clear();
            if (monitors == null || monitors.Length == 0)
                monitors = new[] { "Display 1" };

            foreach (var monitor in monitors)
                Monitors.Add(monitor);

            SelectedMonitor = Monitors[0];
        }

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class HVNCLaunchCardViewModel : INotifyPropertyChanged
    {
        private string _stage = "Ready";
        private bool _isActive;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string LaunchId { get; set; } = string.Empty;

        public string Stage
        {
            get => _stage;
            set
            {
                if (_stage == value)
                    return;
                _stage = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Stage)));
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }
    }
}
