#if NET9_0_OR_GREATER
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pulsar.Server;
using System.Linq;

namespace Pulsar.Common.Tests.Server
{
    [TestClass]
    public class StartupOptionsTests
    {
        [TestMethod]
        public void DefaultsToWinFormsWithoutFlags()
        {
            var options = StartupOptions.Parse(new string[0], _ => null);
            Assert.IsFalse(options.UseWpfShell);
            Assert.IsFalse(options.ListenImmediately);
            Assert.IsFalse(options.DisableAutoListen);
            Assert.IsFalse(options.DisableWebApi);
        }

        [TestMethod]
        public void WpfFlagAndEnvironmentAreRecognized()
        {
            var cli = StartupOptions.Parse(new[] { "--wpf" }, _ => null);
            Assert.IsTrue(cli.UseWpfShell);

            var env = StartupOptions.Parse(new string[0], key => key == "PULSAR_USE_WPF" ? "1" : null);
            Assert.IsTrue(env.UseWpfShell);
        }

        [TestMethod]
        public void WinFormsOverrideWinsAfterWpfFlag()
        {
            var options = StartupOptions.Parse(new[] { "--wpf", "--winforms" }, _ => null);
            Assert.IsFalse(options.UseWpfShell);
        }

        [TestMethod]
        public void ListenerAndWebApiOverridesAreParsed()
        {
            var options = StartupOptions.Parse(new[] { "--listen", "--no-webapi" }, _ => null);
            Assert.IsTrue(options.ListenImmediately);
            Assert.IsFalse(options.DisableAutoListen);
            Assert.IsTrue(options.DisableWebApi);

            var stopped = StartupOptions.Parse(new[] { "--listen", "--no-listen" }, _ => null);
            Assert.IsFalse(stopped.ListenImmediately);
            Assert.IsTrue(stopped.DisableAutoListen);
        }

        [TestMethod]
        public void UnknownArgumentsAndHelpArePreserved()
        {
            var options = StartupOptions.Parse(new[] { "--wat", "--help" }, _ => null);
            Assert.IsTrue(options.ShowStartupHelp);
            CollectionAssert.AreEqual(new[] { "--wat" }, options.UnrecognizedArguments.ToArray());
            StringAssert.Contains(StartupOptions.GetHelpText(), "--wpf");
        }
    }
}
#endif
