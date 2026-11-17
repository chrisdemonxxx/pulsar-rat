using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Pulsar.Client.Setup
{
    /// <summary>
    /// Registers and unregisters the client as a Windows service by driving sc.exe.
    /// Requires administrative privileges; callers are expected to check before use.
    /// </summary>
    public static class ServiceInstallerHelper
    {
        /// <summary>
        /// Derives a stable, valid service name from the target executable path so the
        /// installer and the service host agree on it without depending on decrypted settings.
        /// </summary>
        public static string GetServiceNameForPath(string exePath)
        {
            string baseName = "PulsarSvc";
            try
            {
                if (!string.IsNullOrEmpty(exePath))
                {
                    baseName = Path.GetFileNameWithoutExtension(exePath);
                }
            }
            catch
            {
            }

            // Keep only characters valid in a service name (letters, digits, space, dot).
            var sb = new StringBuilder(baseName.Length);
            foreach (char c in baseName)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '.')
                {
                    sb.Append(c);
                }
            }

            string name = sb.ToString().Trim();
            if (string.IsNullOrEmpty(name))
            {
                name = "PulsarSvc";
            }

            return name;
        }

        public static string GetServiceNameForCurrentProcess()
        {
            return GetServiceNameForPath(GetCurrentProcessPath());
        }

        private static string GetCurrentProcessPath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch
            {
                return null;
            }
        }

        private static int RunScCommand(string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                process.Start();
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit(15000);
                return process.ExitCode;
            }
        }

        /// <summary>
        /// Checks whether a service with the given name is already registered.
        /// </summary>
        public static bool ServiceExists(string serviceName)
        {
            if (string.IsNullOrEmpty(serviceName))
            {
                return false;
            }

            try
            {
                return RunScCommand($"query \"{serviceName}\"") == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Registers the given executable as an auto-start Windows service running with the '-service'
        /// argument. Idempotent: skips creation when the service is already registered.
        /// </summary>
        /// <returns>True when the service is registered afterwards (created now or already present).</returns>
        public static bool InstallService(string exePath, string description = null)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return false;
                }

                string serviceName = GetServiceNameForPath(exePath);
                if (ServiceExists(serviceName))
                {
                    return true; // already installed
                }

                // sc.exe syntax requires a space after 'binPath='; the value itself must keep
                // the executable path in embedded quotes because paths may contain spaces.
                int exitCode = RunScCommand(
                    $"create \"{serviceName}\" binPath= \"\\\"{exePath}\\\" -service\" start= auto");
                if (exitCode != 0)
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(description))
                {
                    // best-effort; a missing description does not affect functionality
                    RunScCommand($"description \"{serviceName}\" \"{description}\"");
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Stops (when running) and removes the service registered for the given executable.
        /// </summary>
        public static bool UninstallService(string exePath)
        {
            try
            {
                string serviceName = GetServiceNameForPath(exePath);
                if (!ServiceExists(serviceName))
                {
                    return true;
                }

                // ignore failure: the service may not be running
                RunScCommand($"stop \"{serviceName}\"");
                return RunScCommand($"delete \"{serviceName}\"") == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
