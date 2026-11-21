using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Pulsar.Relay;

/// <summary>
/// Dependency-free CLI TCP relay for Pulsar.
///
/// The Pulsar transport is plain TCP with a 4-byte little-endian length-prefix
/// frame; all confidentiality/integrity lives at the message layer (AES-256-GCM
/// between the client and the REAL server). This relay therefore never parses
/// anything — it blindly pipes bytes in both directions and the end-to-end
/// security envelope passes through untouched.
/// </summary>
internal static class Program
{
    private const int BufferSize = 64 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private const int DefaultMaxConnections = 256;
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(5);

    private static bool _verbose;

    private static int _roundRobin = -1;
    private static int _nextConnectionId;
    private static int _activeConnections;
    private static long _totalConnections;
    private static long _totalBytesClientToUpstream;
    private static long _totalBytesUpstreamToClient;

    private static readonly ConcurrentDictionary<int, ConnectionContext> Active = new();
    private static readonly ConcurrentDictionary<int, Task> ConnectionTasks = new();
    private static readonly object LogLock = new();

    private sealed class ConnectionContext
    {
        public TcpClient Client;
        public TcpClient Upstream;
    }

    private sealed class Config
    {
        public string ListenHost; // null => all interfaces (dual-stack)
        public int ListenPort = 4782;
        public readonly List<(string Host, int Port)> Upstreams = new();
        public int MaxConnections = DefaultMaxConnections;
        public TimeSpan IdleTimeout = DefaultIdleTimeout;
        public bool Verbose;
    }

    private static async Task<int> Main(string[] args)
    {
        if (!TryParseArgs(args, out var config, out var showHelp))
        {
            PrintUsage();
            return 2;
        }
        if (showHelp)
        {
            PrintUsage();
            return 0;
        }

        _verbose = config.Verbose;

        TcpListener listener;
        try
        {
            listener = CreateListener(config);
            listener.Start(backlog: 1000);
        }
        catch (Exception ex)
        {
            Log($"[relay] failed to start listener on {config.ListenHost ?? "*"}:{config.ListenPort}: {ex.Message}");
            return 1;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // graceful: stop accepting, close connections, exit 0
            shutdown.Cancel();
        };

        Log($"[relay] listening on {listener.LocalEndpoint}, forwarding round-robin to: {string.Join(", ", config.Upstreams)}");
        if (_verbose) Log("[relay] verbose logging enabled");

        using var connectionSlots = new SemaphoreSlim(config.MaxConnections, config.MaxConnections);
        try
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break; // Ctrl+C
                }
                catch (Exception ex)
                {
                    // Accept-loop exceptions must not kill the relay.
                    Log($"[relay] accept error (continuing): {ex.GetType().Name}: {ex.Message}");
                    try { await Task.Delay(200, shutdown.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                try
                {
                    await connectionSlots.WaitAsync(shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    client.Dispose();
                    break;
                }

                var id = Interlocked.Increment(ref _nextConnectionId);
                var task = HandleConnectionAsync(client, id, config, connectionSlots, shutdown.Token);
                ConnectionTasks[id] = task;
                _ = task.ContinueWith(_ => ConnectionTasks.TryRemove(id, out _),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        finally
        {
            Log("[relay] shutting down: closing listener and active connections...");
            listener.Stop();
            foreach (var ctx in Active.Values)
            {
                try { ctx.Upstream?.Dispose(); } catch { /* best effort */ }
                try { ctx.Client?.Dispose(); } catch { /* best effort */ }
            }

            var remaining = ConnectionTasks.Values.ToArray();
            try { await Task.WhenAll(remaining).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException) { Log("[relay] warning: some connections did not close within 5s"); }

            Log($"[relay] totals: connections={Interlocked.Read(ref _totalConnections)}, " +
                $"client->upstream={FormatBytes(Interlocked.Read(ref _totalBytesClientToUpstream))}, " +
                $"upstream->client={FormatBytes(Interlocked.Read(ref _totalBytesUpstreamToClient))}");
        }

        return 0;
    }

    private static async Task HandleConnectionAsync(TcpClient client, int id, Config config,
        SemaphoreSlim connectionSlots, CancellationToken shutdownToken)
    {
        Interlocked.Increment(ref _totalConnections);
        Interlocked.Increment(ref _activeConnections);

        var ctx = new ConnectionContext { Client = client };
        Active[id] = ctx;

        string clientEp;
        try { clientEp = client.Client.RemoteEndPoint?.ToString() ?? "unknown"; }
        catch { clientEp = "unknown"; }

        TcpClient upstream = null;
        string upstreamEp = null;
        try
        {
            // Round-robin upstream selection (thread-safe rotating index),
            // falling through to the next upstream on connect failure.
            var count = config.Upstreams.Count;
            var start = (int)(unchecked((uint)Interlocked.Increment(ref _roundRobin)) % (uint)count);

            for (var attempt = 0; attempt < count && upstream == null; attempt++)
            {
                var candidate = config.Upstreams[(start + attempt) % count];
                var tcp = new TcpClient();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
                    timeout.CancelAfter(ConnectTimeout);
                    await tcp.ConnectAsync(candidate.Host, candidate.Port, timeout.Token).ConfigureAwait(false);
                    upstream = tcp;
                    upstreamEp = $"{candidate.Host}:{candidate.Port}";
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or IOException)
                {
                    tcp.Dispose();
                    if (shutdownToken.IsCancellationRequested) break;
                    Log($"[relay] #{id} upstream {candidate.Host}:{candidate.Port} unreachable ({ex.GetType().Name}), trying next");
                }
            }

            if (upstream == null)
            {
                Log($"[relay] #{id} all {count} upstream(s) exhausted for {clientEp}, closing connection");
                return;
            }

            ctx.Upstream = upstream;
            try { client.NoDelay = true; upstream.NoDelay = true; } catch { /* cosmetic only */ }
            Log($"[relay] #{id} opened (active={Volatile.Read(ref _activeConnections)})");

            using var idle = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            idle.CancelAfter(config.IdleTimeout);
            var toUpstream = PumpAsync(client, upstream, id, "c->u", idle, config.IdleTimeout);
            var toClient = PumpAsync(upstream, client, id, "u->c", idle, config.IdleTimeout);
            var results = await Task.WhenAll(toUpstream, toClient).ConfigureAwait(false);

            Interlocked.Add(ref _totalBytesClientToUpstream, results[0]);
            Interlocked.Add(ref _totalBytesUpstreamToClient, results[1]);

            Log($"[relay] #{id} closed: c->u={FormatBytes(results[0])}, u->c={FormatBytes(results[1])} (active={Volatile.Read(ref _activeConnections) - 1})");
        }
        catch (Exception ex)
        {
            Log($"[relay] #{id} error: {ex.GetType().Name}");
        }
        finally
        {
            Active.TryRemove(id, out _);
            try { upstream?.Dispose(); } catch { /* best effort */ }
            try { client.Dispose(); } catch { /* best effort */ }
            Interlocked.Decrement(ref _activeConnections);
            connectionSlots.Release();
        }
    }

    /// <summary>
    /// Copies bytes source -> destination until EOF, error or shutdown.
    /// On exit, half-closes the destination's send side (FIN) so the opposite
    /// pump can drain until EOF and terminate cleanly.
    /// </summary>
    private static async Task<long> PumpAsync(TcpClient source, TcpClient destination, int id, string direction,
        CancellationTokenSource idle, TimeSpan idleTimeout)
    {
        long total = 0;
        var buffer = new byte[BufferSize];
        try
        {
            var input = source.GetStream();
            var output = destination.GetStream();
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), idle.Token).ConfigureAwait(false);
                if (read <= 0) break; // EOF: peer closed its send side
                idle.CancelAfter(idleTimeout);
                await output.WriteAsync(buffer.AsMemory(0, read), idle.Token).ConfigureAwait(false);
                total += read;
                if (_verbose) Log($"[relay] #{id} {direction} {read} B (total {FormatBytes(total)})");
            }
        }
        catch (OperationCanceledException) { /* shutdown requested */ }
        catch (ObjectDisposedException) { /* socket disposed during shutdown */ }
        catch (IOException) { /* connection reset */ }
        catch (SocketException) { /* connection reset */ }
        finally
        {
            try { destination.Client.Shutdown(SocketShutdown.Send); } catch { /* already closed */ }
        }
        return total;
    }

    private static TcpListener CreateListener(Config config)
    {
        if (config.ListenHost == null || config.ListenHost is "*" or "0.0.0.0" or "::")
        {
            // Dual-stack: accepts IPv4 and IPv6 on all interfaces.
            var dual = new TcpListener(IPAddress.IPv6Any, config.ListenPort);
            dual.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            dual.Server.DualMode = true;
            return dual;
        }

        if (!IPAddress.TryParse(config.ListenHost, out var address))
        {
            var addresses = Dns.GetHostAddresses(config.ListenHost);
            if (addresses.Length == 0)
                throw new ArgumentException($"cannot resolve listen host '{config.ListenHost}'");
            address = addresses[0];
        }

        var listener = new TcpListener(address, config.ListenPort);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        return listener;
    }

    private static bool TryParseArgs(string[] args, out Config config, out bool showHelp)
    {
        config = new Config();
        showHelp = false;
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"missing value for {arg}");
                return args[++i];
            }

            try
            {
                switch (arg.ToLowerInvariant())
                {
                    case "-h":
                    case "--help":
                    case "-?":
                    case "/?":
                        showHelp = true;
                        return true;
                    case "-v":
                    case "--verbose":
                        config.Verbose = true;
                        break;
                    case "--max-connections":
                    {
                        if (!int.TryParse(Next(), out var limit) || limit < 1 || limit > 16384)
                            return Fail($"invalid --max-connections value '{args[i]}'");
                        config.MaxConnections = limit;
                        break;
                    }
                    case "--idle-seconds":
                    {
                        if (!int.TryParse(Next(), out var seconds) || seconds < 5 || seconds > 86400)
                            return Fail($"invalid --idle-seconds value '{args[i]}'");
                        config.IdleTimeout = TimeSpan.FromSeconds(seconds);
                        break;
                    }
                    case "-l":
                    case "--listen":
                    {
                        if (!TryParseEndpoint(Next(), 4782, out var h, out var p))
                            return Fail($"invalid --listen value '{args[i]}'");
                        config.ListenHost = h;
                        config.ListenPort = p;
                        break;
                    }
                    case "-f":
                    case "--forward":
                    {
                        foreach (var part in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (!TryParseEndpoint(part, 4782, out var h, out var p) || h == null)
                                return Fail($"invalid --forward entry '{part}'");
                            config.Upstreams.Add((h, p));
                        }
                        break;
                    }
                    default:
                        if (arg.StartsWith("-", StringComparison.Ordinal))
                            return Fail($"unknown option '{arg}'");
                        positionals.Add(arg);
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                return Fail(ex.Message);
            }
        }

        // Positional form: <listen-endpoint> <forward-endpoint>[,<forward-endpoint>...]
        if (positionals.Count > 0)
        {
            if (positionals.Count > 2)
                return Fail("too many positional arguments");
            if (!TryParseEndpoint(positionals[0], 4782, out var h, out var p))
                return Fail($"invalid listen endpoint '{positionals[0]}'");
            config.ListenHost = h;
            config.ListenPort = p;

            if (positionals.Count == 2)
            {
                foreach (var part in positionals[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!TryParseEndpoint(part, 4782, out var fh, out var fp) || fh == null)
                        return Fail($"invalid forward endpoint '{part}'");
                    config.Upstreams.Add((fh, fp));
                }
            }
        }

        if (config.Upstreams.Count == 0)
            return Fail("at least one --forward upstream is required");

        return true;

        bool Fail(string message)
        {
            Log($"[relay] error: {message}");
            return false;
        }
    }

    /// <summary>
    /// Parses "host:port", ":port" / bare port (all interfaces), "[v6]:port",
    /// bare IPv6 literal, or bare host (default port). host == null means "any".
    /// </summary>
    private static bool TryParseEndpoint(string text, int defaultPort, out string host, out int port)
    {
        host = null;
        port = defaultPort;
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0) return false;

        if (text[0] == '[') // [IPv6]:port
        {
            var close = text.IndexOf(']');
            if (close <= 1) return false;
            host = text.Substring(1, close - 1);
            if (close + 1 < text.Length &&
                (text[close + 1] != ':' || !int.TryParse(text.Substring(close + 2), out port)))
                return false;
            return IsValidPort(port);
        }

        var colon = text.LastIndexOf(':');
        if (colon < 0)
        {
            // Bare port ("4782") => all interfaces; otherwise bare host with default port.
            if (int.TryParse(text, out port)) return IsValidPort(port);
            host = text;
            return true;
        }
        if (colon == 0) // ":4782" => all interfaces
        {
            return int.TryParse(text.Substring(1), out port) && IsValidPort(port);
        }
        if (text.IndexOf(':') != colon)
        {
            // Multiple colons, no brackets: bare IPv6 literal with default port.
            host = text;
            return true;
        }

        host = text.Substring(0, colon);
        return int.TryParse(text.Substring(colon + 1), out port) && IsValidPort(port);
    }

    private static bool IsValidPort(int port) => port > 0 && port <= 65535;

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024L => $"{bytes} B",
        < 1024L * 1024L => $"{bytes / 1024.0:0.##} KiB",
        _ => $"{bytes / (1024.0 * 1024.0):0.##} MiB",
    };

    private static void Log(string message)
    {
        lock (LogLock)
        {
            Console.Out.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");
            Console.Out.Flush();
        }
    }

    private static void PrintUsage()
    {
        lock (LogLock)
        {
            Console.Out.WriteLine("""
                Pulsar.Relay - CLI TCP relay for Pulsar servers (plain byte-pipe, no protocol parsing)

                Usage:
                  Pulsar.Relay [--listen <endpoint>] [--forward <endpoint>[,<endpoint>...]] [--verbose]
                  Pulsar.Relay <listen-endpoint> <forward-endpoint>[,<forward-endpoint>...]

                Options:
                  -l, --listen    Endpoint to accept Pulsar clients on (default: 0.0.0.0:4782).
                                  Forms: 0.0.0.0:4782 | 127.0.0.1:4782 | [::]:4782 | :4782 | 4782
                  -f, --forward   Upstream Pulsar server(s) to relay to, round-robin per connection.
                                  Repeatable and/or comma-separated. Required.
                  --max-connections  Maximum simultaneous relayed connections (default: 256).
                  --idle-seconds     Disconnect a connection after this many idle seconds (default: 300).
                  -v, --verbose   Log every relayed chunk.
                  -h, --help      Show this help.

                The relay pipes bytes blindly in both directions; Pulsar's message-layer
                AES-256-GCM security envelope stays end-to-end between the client and the
                real server, so the relay cannot and does not inspect anything.
                """);
            Console.Out.Flush();
        }
    }
}
