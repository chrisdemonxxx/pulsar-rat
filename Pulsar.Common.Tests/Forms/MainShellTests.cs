#if NET9_0_OR_GREATER
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Server.Forms;

namespace Pulsar.Common.Tests.Forms
{
    [TestClass]
    public class MainShellTests
    {
        [TestMethod]
        public void ResolvePreviewState_NoSelection_IsEmpty()
        {
            Assert.AreEqual(
                FrmMain.MainShellPreviewState.Empty,
                FrmMain.ResolveMainShellPreviewState(0, false));
        }

        [TestMethod]
        public void ResolvePreviewState_StaleSingleSelection_IsEmpty()
        {
            Assert.AreEqual(
                FrmMain.MainShellPreviewState.Empty,
                FrmMain.ResolveMainShellPreviewState(1, false));
        }

        [TestMethod]
        public void ResolvePreviewState_SingleConnected_IsReady()
        {
            Assert.AreEqual(
                FrmMain.MainShellPreviewState.Ready,
                FrmMain.ResolveMainShellPreviewState(1, true));
        }

        [TestMethod]
        public void ResolvePreviewState_Multiple_IsMultiple()
        {
            Assert.AreEqual(
                FrmMain.MainShellPreviewState.Multiple,
                FrmMain.ResolveMainShellPreviewState(3, true));
        }

        [TestMethod]
        public void SearchMatches_IsCaseInsensitiveAndRequiresMatch()
        {
            Assert.IsTrue(FrmMain.MainShellSearchMatches(
                "ADMIN",
                new[] { "192.0.2.10", "ADMIN-PC", "win11" }));
            Assert.IsFalse(FrmMain.MainShellSearchMatches(
                "linux",
                new[] { "192.0.2.10", "ADMIN-PC", "win11" }));
        }

        [TestMethod]
        public void SearchMatches_EmptyFilterMatchesEmptyFilter()
        {
            Assert.IsTrue(FrmMain.MainShellSearchMatches("  ", null));
        }
    }
}
#endif
