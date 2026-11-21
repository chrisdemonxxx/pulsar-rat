using System.Windows;
using Pulsar.Server.ViewModels;
using Pulsar.Server.Utilities;

namespace Pulsar.Server.Views
{
    /// <summary>
    /// PulsarMainWindow.xaml code-behind
    /// WPF port of FrmMain (dashboard and client management)
    /// </summary>
    public partial class PulsarMainWindow : Window
    {
        public PulsarMainWindow()
        {
            InitializeComponent();
            
            // Set the data context to MainViewModel
            this.DataContext = new MainViewModel();
            
            // Set window title with version
            this.Title = $"Pulsar Premium - {ServerVersion.Display}";
        }

        /// <summary>
        /// Gets the ViewModel instance
        /// </summary>
        public MainViewModel ViewModel => (MainViewModel)this.DataContext;
    }
}
