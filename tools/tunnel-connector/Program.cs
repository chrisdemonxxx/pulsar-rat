using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Tunnel connector v2 (file-logging build): bridges the Railway relay to the local Pulsar listener.
//   relay (public) <--framed mux--> this process <--raw TCP--> 127.0.0.1:<LocalPort>
// Frame: [chanId:u8][len:u32 LE][payload]. len==0 => close channel. chan 255 => ping/keepalive.

const string RelayHost = "metro.proxy.rlwy.net";
const int RelayPort = 32326;
const string Magic = "TUNNEL";
const int LocalPort = 4782;

void Log(string m)
{
    try { File.AppendAllText(@"C:\Users\chris\Downloads\c2panel\pulsar\tools\tunnel-connector\tunnel.log", DateTime.Now.ToString("HH:mm:ss") + " " + m + Environment.NewLine); } catch { }
}

using var instanceMutex = new Mutex(true, @"Local\PulsarTunnelConnector", out var createdNew);
if (!createdNew)
{
    Log("another instance is running, exiting");
    return;
}

while (true)
{
    using var cts = new CancellationTokenSource();
    try { await RunSession(cts); }
    catch (Exception e) { Log($"session error: {e.GetType().Name}: {e.Message}"); }
    cts.Cancel();
    Log("reconnecting in 3s...");
    await Task.Delay(3000);
}

async Task RunSession(CancellationTokenSource cts)
{
    using var relay = new TcpClient();
    await relay.ConnectAsync(RelayHost, RelayPort);
    NetworkStream rs = relay.GetStream();
    var magic = Encoding.ASCII.GetBytes(Magic);
    await rs.WriteAsync(magic, 0, magic.Length);
    Log($"connected to relay {RelayHost}:{RelayPort}");

    var chans = new ConcurrentDictionary<byte, TcpClient>();
    object writeLock = new object();

    _ = Task.Run(async () =>
    {
        var ping = new byte[5]; ping[0] = 255;
        while (!cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(15000, cts.Token);
                lock (writeLock) { rs.Write(ping, 0, 5); }
                Log("keepalive ping sent");
            }
            catch { return; }
        }
    }, cts.Token);

    try
    {
        var header = new byte[5];
        while (true)
        {
            await ReadExact(rs, header, 5, cts.Token);
            byte chan = header[0];
            int len = BitConverter.ToInt32(header, 1);
            if (len < 0 || len > 1_000_000) throw new IOException($"bad frame len {len} chan {chan}");
            var payload = new byte[len];
            if (len > 0) await ReadExact(rs, payload, len, cts.Token);

            if (chan == 255) continue;
            if (len == 0)
            {
                if (chans.TryRemove(chan, out var old)) { try { old.Close(); } catch { } }
                continue;
            }
            if (!chans.TryGetValue(chan, out var local))
            {
                local = new TcpClient();
                await local.ConnectAsync(IPAddress.Loopback, LocalPort, cts.Token);
                chans[chan] = local;
                byte captured = chan;
                _ = PumpLocalToRelayAsync(captured, local, rs, writeLock, cts.Token);
                Log($"channel {captured} opened ({chans.Count} active)");
            }
            await local.GetStream().WriteAsync(payload, 0, payload.Length, cts.Token);
        }
    }
    finally
    {
        foreach (var kv in chans) { try { kv.Value.Close(); } catch { } }
        chans.Clear();
    }
}

async Task PumpLocalToRelayAsync(byte chan, TcpClient local, NetworkStream rs, object writeLock, CancellationToken ct)
{
    try
    {
        var stream = local.GetStream();
        var buf = new byte[64 * 1024];
        while (true)
        {
            int n = await stream.ReadAsync(buf, 0, buf.Length, ct);
            if (n <= 0) break;
            var f = new byte[5 + n];
            f[0] = chan;
            BitConverter.GetBytes(n).CopyTo(f, 1);
            Buffer.BlockCopy(buf, 0, f, 5, n);
            lock (writeLock) { rs.Write(f, 0, f.Length); }
        }
    }
    catch { }
    finally
    {
        try
        {
            var f = new byte[5]; f[0] = chan;
            lock (writeLock) { rs.Write(f, 0, 5); }
        }
        catch { }
        try { local.Close(); } catch { }
    }
}

async Task ReadExact(NetworkStream s, byte[] buf, int count, CancellationToken ct)
{
    int off = 0;
    while (off < count)
    {
        int n = await s.ReadAsync(buf, off, count - off, ct);
        if (n <= 0) throw new IOException("relay closed");
        off += n;
    }
}
