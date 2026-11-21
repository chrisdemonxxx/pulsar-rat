using System.Windows.Controls;

namespace Pulsar.Server.Controls.Wpf
{
    public partial class HVNCWorkspaceView : UserControl
    {
        public HVNCWorkspaceView()
        {
            InitializeComponent();
        }

        public HVNCWorkspaceView(HVNCWorkspaceViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }
    }
}
