using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulsar.Server
{
    public sealed class StartupOptions
    {
        public bool UseWpfShell { get; }
        public bool ListenImmediately { get; }
        public bool DisableAutoListen { get; }
        public bool DisableWebApi { get; }
        public bool ShowStartupHelp { get; }
        public IReadOnlyList<string> UnrecognizedArguments { get; }

        private StartupOptions(
            bool useWpfShell,
            bool listenImmediately,
            bool disableAutoListen,
            bool disableWebApi,
            bool showStartupHelp,
            IReadOnlyList<string> unrecognizedArguments)
        {
            UseWpfShell = useWpfShell;
            ListenImmediately = listenImmediately;
            DisableAutoListen = disableAutoListen;
            DisableWebApi = disableWebApi;
            ShowStartupHelp = showStartupHelp;
            UnrecognizedArguments = unrecognizedArguments ?? Array.Empty<string>();
        }

        public static StartupOptions Parse(IEnumerable<string> args, Func<string, string> environmentProvider = null)
        {
            var normalized = (args ?? Array.Empty<string>()).Select(a => a?.Trim()).Where(a => !string.IsNullOrEmpty(a)).ToArray();
            environmentProvider ??= Environment.GetEnvironmentVariable;

            bool useWpfShell = IsTrue(environmentProvider("PULSAR_USE_WPF"));
            bool listenImmediately = IsTrue(environmentProvider("PULSAR_AUTO_LISTEN"));
            bool disableAutoListen = false;
            bool disableWebApi = false;
            bool showHelp = false;
            var unrecognized = new List<string>();

            foreach (var raw in normalized)
            {
                var arg = raw.ToLowerInvariant();
                switch (arg)
                {
                    case "--wpf":
                    case "--wpf-shell":
                        useWpfShell = true;
                        break;
                    case "--winforms":
                    case "--legacy-ui":
                        useWpfShell = false;
                        break;
                    case "--listen":
                    case "--auto-listen":
                        listenImmediately = true;
                        disableAutoListen = false;
                        break;
                    case "--no-listen":
                    case "--no-auto-listen":
                        listenImmediately = false;
                        disableAutoListen = true;
                        break;
                    case "--no-webapi":
                    case "--disable-webapi":
                        disableWebApi = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        showHelp = true;
                        break;
                    default:
                        unrecognized.Add(raw);
                        break;
                }
            }

            return new StartupOptions(useWpfShell, listenImmediately, disableAutoListen, disableWebApi, showHelp, unrecognized);
        }

        public static string GetHelpText()
        {
            return "Pulsar Server startup options:\n" +
                   "  --wpf, --wpf-shell        Launch the premium WPF operator shell (opt-in).\n" +
                   "  --winforms, --legacy-ui   Force the WinForms shell (default).\n" +
                   "  --listen, --auto-listen   Start listeners immediately, regardless of saved AutoListen.\n" +
                   "  --no-listen               Keep listeners stopped for this launch.\n" +
                   "  --no-webapi               Disable Web API startup for this launch.\n" +
                   "  --help                    Show this help text.\n\n" +
                   "Environment:\n" +
                   "  PULSAR_USE_WPF=1          Enable the WPF shell without command-line flags.\n" +
                   "  PULSAR_AUTO_LISTEN=1      Start listeners immediately.\n";
        }

        private static bool IsTrue(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("on", StringComparison.OrdinalIgnoreCase));
        }
    }
}
