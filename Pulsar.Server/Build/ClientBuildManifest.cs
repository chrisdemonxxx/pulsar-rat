using Pulsar.Common;
using Pulsar.Server.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Pulsar.Server.Build
{
    public sealed class ClientBuildManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset CreatedUtc { get; set; }
        public string ArtifactType { get; set; }
        public string OutputFile { get; set; }
        public string OutputSha256 { get; set; }
        public string TemplateFile { get; set; }
        public string TemplateSha256 { get; set; }
        public string CertificateThumbprint { get; set; }
        public string ProtocolVersion { get; set; }
        public int SettingsLayoutVersion { get; set; }
        public string EntryPoint { get; set; }
        public ClientBuildFlags Flags { get; set; }

        public static string GetDefaultPath(string outputPath) => outputPath + ".manifest.json";

        public static void Write(
            BuildOptions options,
            string templatePath,
            string certificateThumbprint,
            string artifactType,
            string entryPoint,
            bool obfuscated,
            bool packed)
        {
            var outputPath = Path.GetFullPath(options.OutputPath);
            var manifestPath = string.IsNullOrWhiteSpace(options.ManifestPath)
                ? GetDefaultPath(outputPath)
                : Path.GetFullPath(options.ManifestPath);

            var manifest = new ClientBuildManifest
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                ArtifactType = artifactType,
                OutputFile = Path.GetFileName(outputPath),
                OutputSha256 = ComputeHash(outputPath),
                TemplateFile = Path.GetFileName(templatePath),
                TemplateSha256 = ComputeHash(templatePath),
                CertificateThumbprint = certificateThumbprint,
                ProtocolVersion = Pulsar.Common.ProtocolVersion.Current,
                SettingsLayoutVersion = ClientSettingsLayout.Version,
                EntryPoint = entryPoint,
                Flags = new ClientBuildFlags
                {
                    Install = options.Install,
                    Startup = options.Startup,
                    HideFile = options.HideFile,
                    Keylogger = options.Keylogger,
                    Pastebin = options.Pastebin,
                    AntiVM = options.AntiVM,
                    AntiDebug = options.AntiDebug,
                    UACBypass = options.UACBypass,
                    CriticalProcess = options.CRITICALPROCESS,
                    InstallService = options.INSTALLSERVICE,
                    ShowClientGui = options.SHOWCLIENTGUI,
                    Obfuscated = obfuscated,
                    Packed = packed
                }
            };

            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath));
            File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static string ComputeHash(string path)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }

    public sealed class ClientBuildFlags
    {
        public bool Install { get; set; }
        public bool Startup { get; set; }
        public bool HideFile { get; set; }
        public bool Keylogger { get; set; }
        public bool Pastebin { get; set; }
        public bool AntiVM { get; set; }
        public bool AntiDebug { get; set; }
        public bool UACBypass { get; set; }
        public bool CriticalProcess { get; set; }
        public bool InstallService { get; set; }
        public bool ShowClientGui { get; set; }
        public bool Obfuscated { get; set; }
        public bool Packed { get; set; }
    }
}
