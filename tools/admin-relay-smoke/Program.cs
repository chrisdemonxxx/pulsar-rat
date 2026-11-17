using Pulsar.AdminClient;
using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.Actions;
using Pulsar.Server.Messages;
using Pulsar.Server.Models;
using Pulsar.Server.Networking;
using System.Diagnostics;
using System.Text;

// End-to-end smoke for permissioned clients (admin relay): hosts a real PulsarServer
// (DummyCertificate, Debug = plaintext transport), registers the AdminRelayHandler,
// simulates an implant in-process over the real wire, and drives the real AdminClient
// executable through every flow: feature-off denial, wrong-key denial, list, command
// relay (verified received by the simulated implant) and live disconnect events.

const int port = 18480;
const string adminKey = "smoke-key-123";

var failures = new List<string>();

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}");
    if (!ok) failures.Add(name);
}

string adminExe = FindAdminExe();
Console.WriteLine($"[smoke] admin exe: {adminExe} (exists: {File.Exists(adminExe)})");
if (!File.Exists(adminExe))
{
    Console.WriteLine("FAIL: AdminClient exe not built (run: dotnet build Pulsar.AdminClient -c Debug)");
    return 1;
}

// real server on a scratch port
var server = new PulsarServer(new DummyCertificate());
server.Listen((ushort)port, false, false);
Console.WriteLine($"[smoke] server listening on {port}");

var handler = new AdminRelayHandler(() => server, (m, l) => Console.WriteLine($"[srv:{l}] {m}"));
MessageHandler.Register(handler);

// Check 1: feature effectively off when no admin key is configured
Settings.AdminClientKey = "";
var (offCode, _, offErr) = RunAdmin($"--host 127.0.0.1:{port} --key whatever --list");
Check("feature-off denial (exit 1, no admin key configured)",
    offCode == 1 && offErr.Contains("no admin key configured", StringComparison.OrdinalIgnoreCase));

// enable the feature
Settings.AdminClientKey = adminKey;

// simulated implant over the real wire (in-process, regular identification)
var sim = new AdminConnection(null); // Debug build: plaintext, no signature
sim.Connect("127.0.0.1", (ushort)port);
var identification = await sim.IdentifyAsync("IMPLANT-SIM", "simuser", "SIMPC", "SimOS x64", "2.4.5", TimeSpan.FromSeconds(10));
Console.WriteLine($"[smoke] sim identified: {identification.Id}");
Check("sim identification over the wire", identification.Id.Length == 64);

// Check 2: wrong admin key
var (badCode, _, badErr) = RunAdmin($"--host 127.0.0.1:{port} --key wrong-key --list");
Check("wrong-key denial (exit 1, invalid admin key)",
    badCode == 1 && badErr.Contains("invalid admin key", StringComparison.OrdinalIgnoreCase));

// Check 3: correct key + list (must contain the sim)
var (listCode, listOut, _) = RunAdmin($"--host 127.0.0.1:{port} --key {adminKey} --list");
Check("list (exit 0, ADMIN OK, sim visible)",
    listCode == 0
    && listOut.Contains("ADMIN OK", StringComparison.OrdinalIgnoreCase)
    && listOut.Contains("IMPLANT-SIM", StringComparison.OrdinalIgnoreCase)
    && listOut.Contains(identification.Id, StringComparison.OrdinalIgnoreCase));

// Check 4: relay a command to the sim — the sim must actually receive it
var relayWatcher = sim.WaitForAsync<DoShutdownAction>(TimeSpan.FromSeconds(8));
var (relayCode, relayOut, _) = RunAdmin($"--host 127.0.0.1:{port} --key {adminKey} --target {identification.Id} --action shutdown");
var received = await relayWatcher;
Check("relay (exit 0, RELAY OK, sim received DoShutdownAction Shutdown)",
    relayCode == 0
    && relayOut.Contains("RELAY OK", StringComparison.OrdinalIgnoreCase)
    && received != null
    && received.Action == ShutdownAction.Shutdown);

// Check 4b: relay to an unknown target
var (ghostCode, _, ghostErr) = RunAdmin($"--host 127.0.0.1:{port} --key {adminKey} --target {new string('f', 64)} --action shutdown");
Check("unknown-target relay rejected (exit 1, not found)",
    ghostCode == 1 && ghostErr.Contains("not found", StringComparison.OrdinalIgnoreCase));

// Check 5: live events — a watching admin sees the sim disconnect
var watchPsi = new ProcessStartInfo
{
    FileName = adminExe,
    Arguments = $"--host 127.0.0.1:{port} --key {adminKey} --watch --duration 8",
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true,
    StandardOutputEncoding = Encoding.UTF8,
    StandardErrorEncoding = Encoding.UTF8
};
watchPsi.EnvironmentVariables["DOTNET_ROLL_FORWARD"] = "Major";
using var watcher = Process.Start(watchPsi);
await Task.Delay(1500); // let the watcher authenticate
sim.Disconnect();
var stdoutTask = watcher.StandardOutput.ReadToEndAsync();
var stderrTask = watcher.StandardError.ReadToEndAsync();
bool watcherExited = watcher.WaitForExit(25000);
var watchOut = watcherExited ? await stdoutTask : "(watcher still running)";
if (!watcherExited)
{
    Console.WriteLine("FAIL watcher did not exit within 25s — killing");
    watcher.Kill();
    failures.Add("live events (watcher did not exit)");
}
else
{
    Console.WriteLine($"[watcher out]\n{watchOut.Trim()}");
    Console.WriteLine($"[watcher stderr]\n{(await stderrTask).Trim()}");
    Check("live events (watcher saw the disconnect)",
        watcher.ExitCode == 0 && watchOut.Contains("EVENT - simuser@SIMPC", StringComparison.OrdinalIgnoreCase));
}

MessageHandler.Unregister(handler);
handler.Dispose();

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "ALL PASS"
    : $"FAILED: {failures.Count} — {string.Join(", ", failures)}");
return failures.Count == 0 ? 0 : 1;

static string FindAdminExe()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Pulsar.AdminClient")))
    {
        dir = dir.Parent;
    }

    return dir == null
        ? "Pulsar.AdminClient.exe"
        : Path.Combine(dir.FullName, "Pulsar.AdminClient", "bin", "Debug", "net9.0-windows", "Pulsar.AdminClient.exe");
}

static (int code, string stdout, string stderr) RunAdmin(string args)
{
    var psi = new ProcessStartInfo
    {
        FileName = FindAdminExe(),
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };
    psi.EnvironmentVariables["DOTNET_ROLL_FORWARD"] = "Major";

    using var p = Process.Start(psi);
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit(30000);
    Console.WriteLine($"[admin exit={p.ExitCode}] {args}");
    return (p.ExitCode, stdout, stderr);
}
