#if NET9_0_OR_GREATER
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Server.Controls.Wpf;

namespace Pulsar.Common.Tests.HVNC
{
    [TestClass]
    public class HVNCWorkspaceViewModelTests
    {
        [TestMethod]
        public void SetMonitors_UpdatesSelectionAndCatalog()
        {
            var viewModel = new HVNCWorkspaceViewModel();

            viewModel.SetMonitors(new[] { "Display 1", "Display 2" });

            Assert.AreEqual(2, viewModel.Monitors.Count);
            Assert.AreEqual("Display 1", viewModel.SelectedMonitor);
        }

        [TestMethod]
        public void SetMonitors_EmptyFallbackUsesOneDisplay()
        {
            var viewModel = new HVNCWorkspaceViewModel();

            viewModel.SetMonitors(new string[0]);

            Assert.AreEqual(1, viewModel.Monitors.Count);
            Assert.AreEqual("Display 1", viewModel.SelectedMonitor);
        }

        [TestMethod]
        public void LaunchCard_StageAndActiveUpdate()
        {
            var card = new HVNCLaunchCardViewModel
            {
                Name = "Chrome",
                LaunchId = "launch-1"
            };

            card.Stage = "Starting";
            card.IsActive = true;

            Assert.AreEqual("Starting", card.Stage);
            Assert.IsTrue(card.IsActive);
        }
    }
}
#endif
