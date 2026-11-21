using System.Windows;
using Pulsar.Server.Networking;
using Pulsar.Server.ViewModels;
using Pulsar.Server.Utilities;

namespace Pulsar.Server.Views
{
    /// <summary>
    /// PulsarMainWindow.xaml code-behind
    /// WPF port of FrmMain (dashboard and client management)
    /// Provides data binding for connected/offline clients and real-time updates
    /// </summary>
    public partial class PulsarMainWindow : Window
    {
        public PulsarMainWindow()
        {
            InitializeComponent();
            
            // Set the data context to MainViewModel (without server initially)
            this.DataContext = new MainViewModel();
            
            // Set window title with version
            this.Title = $"Pulsar Premium - {ServerVersion.Display}";
        }

        /// <summary>
        /// Initializes the window with a PulsarServer instance for data binding
        /// Call this after creating the PulsarServer and before showing the window
        /// </summary>
        /// <param name="server">The PulsarServer instance to bind to</param>
        public void SetServer(PulsarServer server)
        {
            if (ViewModel != null)
            {
                ViewModel.SetServer(server);
            }
        }

        /// <summary>
        /// Gets the ViewModel instance
        /// </summary>
        public MainViewModel ViewModel => (MainViewModel)this.DataContext;
    }
}
