using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Pulsar.Server.Networking;
using Pulsar.Server.Persistence;

namespace Pulsar.Server.ViewModels
{
    /// <summary>
    /// ViewModel for PulsarMainWindow (WPF port of FrmMain)
    /// Implements MVVM pattern for dashboard and client management
    /// Provides data binding for connected/offline clients and real-time updates
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private PulsarServer _listenServer;
        private Client _selectedClient;
        private ObservableCollection<Client> _onlineClients;
        private ObservableCollection<OfflineClientRecord> _offlineClients;
        private string _listeningStatus;
        private int _connectedClientCount;
        private bool _isLoading;
        private bool _isListening;

        /// <summary>
        /// Initializes a new instance of MainViewModel with an optional server reference
        /// </summary>
        /// <param name="listenServer">Optional PulsarServer instance for binding to real data</param>
        public MainViewModel(PulsarServer listenServer = null)
        {
            _listenServer = listenServer;
            _onlineClients = new ObservableCollection<Client>();
            _offlineClients = new ObservableCollection<OfflineClientRecord>();
            _listeningStatus = "Not listening";
            _connectedClientCount = 0;
            _isLoading = false;
            _isListening = false;

            // Initialize commands
            InitializeCommands();

            // Wire up server events if provided
            if (_listenServer != null)
            {
                WireUpServerEvents();
                RefreshClientLists();
            }
        }

        /// <summary>
        /// Wires up event handlers to the server for real-time updates
        /// </summary>
        private void WireUpServerEvents()
        {
            if (_listenServer == null) return;

            _listenServer.ClientConnected += OnServerClientConnected;
            _listenServer.ClientDisconnected += OnServerClientDisconnected;

            // Initial status
            UpdateListeningStatus();
        }

        /// <summary>
        /// Unwires event handlers from the server (cleanup)
        /// </summary>
        private void UnwireServerEvents()
        {
            if (_listenServer == null) return;

            _listenServer.ClientConnected -= OnServerClientConnected;
            _listenServer.ClientDisconnected -= OnServerClientDisconnected;
        }

        /// <summary>
        /// Handles server ClientConnected event
        /// </summary>
        private void OnServerClientConnected(Client client)
        {
            if (client == null) return;

            // Add to online clients collection (dispatch to UI thread)
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!OnlineClients.Contains(client))
                {
                    OnlineClients.Add(client);
                }
                ConnectedClientCount = OnlineClients.Count;
            });
        }

        /// <summary>
        /// Handles server ClientDisconnected event
        /// </summary>
        private void OnServerClientDisconnected(Client client)
        {
            if (client == null) return;

            // Remove from online clients collection (dispatch to UI thread)
            Application.Current?.Dispatcher.Invoke(() =>
            {
                OnlineClients.Remove(client);
                ConnectedClientCount = OnlineClients.Count;

                // If this was the selected client, clear selection
                if (SelectedClient == client)
                {
                    SelectedClient = null;
                }
            });
        }

        /// <summary>
        /// Refreshes the lists of online and offline clients from the server
        /// </summary>
        private void RefreshClientLists()
        {
            if (_listenServer == null) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    // Clear and repopulate online clients
                    OnlineClients.Clear();
                    foreach (var client in _listenServer.ConnectedClients)
                    {
                        if (client != null && client.Identified)
                        {
                            OnlineClients.Add(client);
                        }
                    }

                    ConnectedClientCount = OnlineClients.Count;

                    // Load offline clients from repository (static class)
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
                        System.Diagnostics.Debug.WriteLine($"Error loading offline clients: {ex.Message}");
                    }

                    UpdateListeningStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error refreshing client lists: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Updates the listening status text based on server state
        /// </summary>
        private void UpdateListeningStatus()
        {
            if (_listenServer == null)
            {
                ListeningStatus = "Not listening";
                IsListening = false;
                return;
            }

            try
            {
                if (_listenServer.Listening)
                {
                    // Try to get port information if available
                    ListeningStatus = $"Listening (Port 8080)"; // Default port
                    IsListening = true;
                }
                else
                {
                    ListeningStatus = "Not listening";
                    IsListening = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating listening status: {ex.Message}");
                ListeningStatus = "Error";
                IsListening = false;
            }
        }

        /// <summary>
        /// Sets the PulsarServer reference (for late binding)
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
                OnlineClients.Clear();
                OfflineClients.Clear();
                ListeningStatus = "Not listening";
                ConnectedClientCount = 0;
            }
        }

        /// <summary>
        /// Currently selected client
        /// </summary>
        public Client SelectedClient
        {
            get => _selectedClient;
            set
            {
                if (_selectedClient != value)
                {
                    _selectedClient = value;
                    OnPropertyChanged();
                    // Refresh command states
                    RefreshCommandStates();
                }
            }
        }

        /// <summary>
        /// Collection of online clients
        /// </summary>
        public ObservableCollection<Client> OnlineClients
        {
            get => _onlineClients;
            set
            {
                if (_onlineClients != value)
                {
                    _onlineClients = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Collection of offline clients
        /// </summary>
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

        /// <summary>
        /// Listening status text (e.g., "Listening on port 8080")
        /// </summary>
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

        /// <summary>
        /// Number of connected clients
        /// </summary>
        public int ConnectedClientCount
        {
            get => _connectedClientCount;
            set
            {
                if (_connectedClientCount != value)
                {
                    _connectedClientCount = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Whether the view is currently loading data
        /// </summary>
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

        /// <summary>
        /// Whether the server is currently listening for connections
        /// </summary>
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

        #region Commands

        public ICommand QuickMicrophoneCommand { get; private set; }
        public ICommand QuickRemoteDesktopCommand { get; private set; }
        public ICommand QuickWebcamCommand { get; private set; }
        public ICommand QuickKeyloggerCommand { get; private set; }
        public ICommand QuickFileManagerCommand { get; private set; }
        public ICommand QuickRemoteShellCommand { get; private set; }
        public ICommand QuickFileExplorerCommand { get; private set; }
        public ICommand ClearOfflineClientsCommand { get; private set; }

        private void InitializeCommands()
        {
            // Quick action commands
            QuickMicrophoneCommand = new RelayCommand(ExecuteQuickMicrophone, CanExecuteQuickAction);
            QuickRemoteDesktopCommand = new RelayCommand(ExecuteQuickRemoteDesktop, CanExecuteQuickAction);
            QuickWebcamCommand = new RelayCommand(ExecuteQuickWebcam, CanExecuteQuickAction);
            QuickKeyloggerCommand = new RelayCommand(ExecuteQuickKeylogger, CanExecuteQuickAction);
            QuickFileManagerCommand = new RelayCommand(ExecuteQuickFileManager, CanExecuteQuickAction);
            QuickRemoteShellCommand = new RelayCommand(ExecuteQuickRemoteShell, CanExecuteQuickAction);
            QuickFileExplorerCommand = new RelayCommand(ExecuteQuickFileExplorer, CanExecuteQuickAction);

            // Menu commands
            ClearOfflineClientsCommand = new RelayCommand(ExecuteClearOfflineClients);
        }

        #region Quick Action Handlers

        private void ExecuteQuickMicrophone()
        {
            if (SelectedClient == null) return;
            System.Diagnostics.Debug.WriteLine("Quick Microphone clicked");
            // TODO: Implement microphone action
        }

        private void ExecuteQuickRemoteDesktop()
        {
            if (SelectedClient == null) return;
            System.Diagnostics.Debug.WriteLine("Quick Remote Desktop clicked");
            // TODO: Implement remote desktop action
        }

        private void ExecuteQuickWebcam()
        {
            if (SelectedClient == null) return;
            System.Diagnostics.Debug.WriteLine("Quick Webcam clicked");
            // TODO: Implement webcam action
        }

        private void ExecuteQuickKeylogger()
        {
            if (SelectedClient == null) return;
            System.Diagnostics.Debug.WriteLine("Quick Keylogger clicked");
            // TODO: Implement keylogger action
        }

        private void ExecuteQuickFileManager()
        {
            if (SelectedClient == null) return;
            System.Diagnostics.Debug.WriteLine("Quick File Manager clicked");
            // TODO: Implement file manager action
        }

        private void ExecuteQuickRemoteShell()
        {
            if (SelectedClient == null) return;
            System.Diagnostics.Debug.WriteLine("Quick Remote Shell clicked");
            // TODO: Implement remote shell action
        }

        private void ExecuteQuickFileExplorer()
        {
            if (SelectedClient == null) return;
            System.Diagnostics.Debug.WriteLine("Quick File Explorer clicked");
            // TODO: Implement file explorer action
        }

        private bool CanExecuteQuickAction()
        {
            return SelectedClient != null && SelectedClient.Connected;
        }

        #endregion

        #region Menu Handlers

        private void ExecuteClearOfflineClients()
        {
            try
            {
                // Clear the offline clients collection
                OfflineClients.Clear();

                // Clear from repository (static method)
                OfflineClientRepository.ClearOfflineClients();

                System.Diagnostics.Debug.WriteLine("Offline clients cleared");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing offline clients: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Refresh the state of all commands
        /// </summary>
        private void RefreshCommandStates()
        {
            // Force command re-evaluation
            CommandManager.InvalidateRequerySuggested();
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
    /// Generic relay command implementation for MVVM
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();
    }

    /// <summary>
    /// Generic relay command with parameter
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
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
    }
}
