using Pulsar.Common;
using Pulsar.Server.Models;
using System;
using System.IO;

namespace Pulsar.Server.Build
{
    public static class BuildOptionsValidator
    {
        public const int MinimumReconnectDelay = 0;
        public const int MaximumReconnectDelay = 600000;

        public static void Validate(BuildOptions options, bool shellcode)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            RequireText(options.Tag, nameof(options.Tag));
            RequireText(options.Mutex, nameof(options.Mutex));
            RequireText(options.RawHosts, nameof(options.RawHosts));
            RequireText(options.Version, nameof(options.Version));
            RequireValue(options.InstallSub, nameof(options.InstallSub));
            RequireValue(options.InstallName, nameof(options.InstallName));
            RequireValue(options.StartupName, nameof(options.StartupName));
            RequireValue(options.LogDirectoryName, nameof(options.LogDirectoryName));

            if (!string.Equals(options.Version, ProtocolVersion.Current, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Version must match protocol version {ProtocolVersion.Current}.",
                    nameof(options.Version));

            if (options.Delay < MinimumReconnectDelay || options.Delay > MaximumReconnectDelay)
                throw new ArgumentOutOfRangeException(
                    nameof(options.Delay),
                    $"Delay must be between {MinimumReconnectDelay} and {MaximumReconnectDelay} milliseconds.");

            if (options.InstallPath < 1 || options.InstallPath > 3)
                throw new ArgumentOutOfRangeException(nameof(options.InstallPath));

            if (options.Pastebin &&
                (!Uri.TryCreate(options.RawHosts, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                throw new ArgumentException("Pastebin hosts must be an absolute HTTP or HTTPS URL.", nameof(options.RawHosts));
            }

            if (options.Startup && !options.Install && string.IsNullOrWhiteSpace(options.StartupName))
                throw new ArgumentException("Startup requires a startup name.", nameof(options.StartupName));

            if (options.AssemblyInformation != null)
            {
                if (options.AssemblyInformation.Length != 8)
                    throw new ArgumentException("Assembly information must contain exactly eight values.");

                if (!System.Version.TryParse(options.AssemblyInformation[6], out _) ||
                    !System.Version.TryParse(options.AssemblyInformation[7], out _))
                {
                    throw new ArgumentException("Assembly information contains an invalid product or file version.");
                }
            }

            if (!string.IsNullOrWhiteSpace(options.IconPath) && !File.Exists(options.IconPath))
                throw new FileNotFoundException("Icon file not found.", options.IconPath);

            var outputPath = Path.GetFullPath(options.OutputPath ?? string.Empty);
            if (shellcode && !string.Equals(Path.GetExtension(outputPath), ".bin", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Shellcode output must use the .bin extension.", nameof(options.OutputPath));
            if (!shellcode && !string.Equals(Path.GetExtension(outputPath), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Client output must use the .exe extension.", nameof(options.OutputPath));

            var manifestPath = string.IsNullOrWhiteSpace(options.ManifestPath)
                ? ClientBuildManifest.GetDefaultPath(outputPath)
                : Path.GetFullPath(options.ManifestPath);
            if (string.Equals(outputPath, manifestPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The manifest path cannot replace the generated artifact.");
        }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{name} cannot be empty.", name);
        }

        private static void RequireValue(string value, string name)
        {
            if (value == null)
                throw new ArgumentNullException(name);
        }
    }
}
