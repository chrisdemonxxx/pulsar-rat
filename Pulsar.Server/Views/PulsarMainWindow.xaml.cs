using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pulsar.Server.Networking;
using Pulsar.Server.Services.Wpf;
using Pulsar.Server.Utilities;
using Pulsar.Server.ViewModels;

namespace Pulsar.Server.Views
{
    /// <summary>
    /// PulsarMainWindow.xaml code-behind.
    /// WPF port of FrmMain (dashboard and client management).
    /// Handles window-scoped theme synchronization (including StatsView and HeatMapView),
    /// right-click row selection, double-click remote desktop launch, smooth client-selection
    /// transitions, dialog ownership wiring, and clean ViewModel disposal.
    /// </summary>
    public partial class PulsarMainWindow : Window
    {
        private MainViewModel _viewModel;

        public PulsarMainWindow()
        {
            InitializeComponent();

            bool isDark = ThemeService.Instance.CurrentTheme == ThemeService.Theme.Dark;
            ThemeService.ApplyThemeTo(Resources, ThemeService.Instance.CurrentTheme);
            ApplyAnalyticsViewsTheme(isDark);
            ThemeService.Instance.ThemeChanged += OnThemeChanged;

            _viewModel = new MainViewModel(
                listenServer: null,
                clientOperationService: null,
                dialogService: new WpfClientDialogService(() => this),
                toolLauncher: new WinFormsClientToolLauncher(),
                uiDispatcher: DispatchToUi,
                serverWindowLauncher: new WinFormsServerWindowLauncher());

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.AnalyticsSnapshotsUpdated += OnAnalyticsSnapshotsUpdated;
            DataContext = _viewModel;

            _viewModel.RefreshAnalyticsSnapshots();

            Title = $"Pulsar Premium - {ServerVersion.Display}";
            Closed += OnWindowClosed;
        }

        /// <summary>
        /// Initializes the window with a PulsarServer instance for data binding.
        /// Call this after creating the PulsarServer and before showing the window.
        /// </summary>
        public void SetServer(PulsarServer server)
        {
            ViewModel?.SetServer(server);
        }

        /// <summary>
        /// Gets the ViewModel instance.
        /// </summary>
        public MainViewModel ViewModel => _viewModel ?? DataContext as MainViewModel;

        private void DispatchToUi(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.Invoke(action);
            }
        }

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e)
        {
            DispatchToUi(() =>
            {
                ThemeService.ApplyThemeTo(Resources, e.NewTheme);
                ApplyAnalyticsViewsTheme(e.NewTheme == ThemeService.Theme.Dark);
            });
        }

        private void ApplyAnalyticsViewsTheme(bool isDarkMode)
        {
            try
            {
                StatsPanelView?.ApplyTheme(isDarkMode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StatsView theme error: {ex.Message}");
            }

            try
            {
                HeatMapPanelView?.ApplyTheme(isDarkMode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HeatMapView theme error: {ex.Message}");
            }
        }

        private void OnAnalyticsSnapshotsUpdated(object sender, AnalyticsSnapshotsEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            DispatchToUi(() =>
            {
                try
                {
                    if (e.Statistics != null)
                    {
                        StatsPanelView?.UpdateSnapshot(e.Statistics);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"StatsView update error: {ex.Message}");
                }

                try
                {
                    if (e.Geo != null)
                    {
                        HeatMapPanelView?.UpdateSnapshot(e.Geo);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HeatMapView update error: {ex.Message}");
                }
            });
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedClient))
            {
                PlayClientInfoTransition();
            }
        }

        private void OnlineClientsDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                var row = FindAncestor<DataGridRow>(source);
                if (row != null)
                {
                    row.IsSelected = true;
                    if (row.Item is Client client && ViewModel != null)
                    {
                        ViewModel.SelectedClient = client;
                    }
                }
            }
        }

        private void OnlineClientsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<DataGridRow>(source) != null)
            {
                if (ViewModel?.QuickRemoteDesktopCommand?.CanExecute(null) == true)
                {
                    ViewModel.QuickRemoteDesktopCommand.Execute(null);
                }
            }
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void PlayClientInfoTransition()
        {
            if (ClientInfoCard == null || ClientInfoSlide == null)
            {
                return;
            }

            var fadeAnimation = new DoubleAnimation
            {
                From = 0.45,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var slideAnimation = new DoubleAnimation
            {
                From = 6.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            ClientInfoCard.BeginAnimation(OpacityProperty, fadeAnimation);
            ClientInfoSlide.BeginAnimation(TranslateTransform.YProperty, slideAnimation);
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            ThemeService.Instance.ThemeChanged -= OnThemeChanged;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.AnalyticsSnapshotsUpdated -= OnAnalyticsSnapshotsUpdated;
                _viewModel.Dispose();
                _viewModel = null;
            }
        }
    }
}
