using System.Windows;

namespace Pulsar.Server.Views
{
    /// <summary>
    /// Base dialog class for all Pulsar modal dialogs.
    /// Provides automatic theme support, consistent styling, and dialog results.
    /// </summary>
    public partial class PulsarDialog : Window
    {
        public PulsarDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the dialog result to True (OK/Accept).
        /// </summary>
        protected void AcceptDialog()
        {
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Sets the dialog result to False (Cancel/Reject).
        /// </summary>
        protected void CancelDialog()
        {
            DialogResult = false;
            Close();
        }
    }
}
