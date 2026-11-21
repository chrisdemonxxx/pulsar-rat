using Pulsar.Common.Extensions;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Networking;
using Pulsar.Server.Forms;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace Pulsar.Server.Networking
{
    public class Server
    {
        /// <summary>
        /// Occurs when the state of the server changes.
        /// </summary>
        public event ServerStateEventHandler ServerState;

        /// <summary>
        /// Represents a method that will handle a change in the server's state.
        /// </summary>
        public delegate void ServerStateEventHandler(Server s, bool listening, ushort port);

        /// <summary>
        /// Fires an event that informs subscribers that the state of the server has changed.
        /// </summary>
        protected virtual void OnServerState(bool listening, ushort port)
        {
            var handler = ServerState;
            handler?.Invoke(this, listening, port);
        }

        /// <summary>
        /// Occurs when a client connects to the server.
        /// </summary>
        public event ClientConnectedEventHandler ClientConnected;

        /// <summary>
        /// Represents a method that will handle a client connection to the server.
        /// </summary>
        public delegate void ClientConnectedEventHandler(Server s, Client c);

        /// <summary>
        /// Fires an event that informs subscribers that a client has connected to the server.
        /// </summary>
        protected virtual void OnClientConnected(Client c)
        {
            var handler = ClientConnected;
            handler?.Invoke(this, c);
        }

        /// <summary>
        /// Occurs when a client disconnects from the server.
        /// </summary>
        public event ClientDisconnectedEventHandler ClientDisconnected;

        /// <summary>
        /// Represents a method that will handle a client disconnection from the server.
        /// </summary>
        public delegate void ClientDisconnectedEventHandler(Server s, Client c);

        /// <summary>
        /// Fires an event that informs subscribers that a client has disconnected from the server.
        /// </summary>
        protected virtual void OnClientDisconnected(Client c)
        {
            var handler = ClientDisconnected;
            handler?.Invoke(this, c);
        }

        /// <summary>
        /// Occurs when the state of a client changes.
        /// </summary>
        public event ClientStateEventHandler ClientState;

        /// <summary>
        /// Represents a method that will handle a change in the state of a client.
        /// </summary>
        public delegate void ClientStateEventHandler(Server s, Client c, bool connected);

        /// <summary>
        /// Fires an event that informs subscribers that the state of a client has changed.
        /// </summary>
        protected virtual void OnClientState(Client c, bool connected)
        {
            var handler = ClientState;
            handler?.Invoke(this, c, connected);
        }

        /// <summary>
        /// Occurs when a message is received by a client.
        /// </summary>
        public event ClientReadEventHandler ClientRead;

        /// <summary>
        /// Represents a method that will handle a message received by a client.
        /// </summary>
        public delegate void ClientReadEventHandler(Server s, Client c, IMessage message);

        /// <summary>
        /// Fires an event that informs subscribers that a message has been
        /// received from the client.
        /// </summary>
        protected virtual void OnClientRead(Client c, IMessage message)
        {
            var handler = ClientRead;
            handler?.Invoke(this, c, message);
        }

        /// <summary>
        /// Occurs when a client experiences an unrecoverable error.
        /// </summary>
        public event ClientFailEventHandler ClientFail;

        /// <summary>
        /// Represents a method that will handle a client failure.
        /// </summary>
        public delegate void ClientFailEventHandler(Server s, Client c, Exception ex);

        /// <summary>
        /// Fires an event that informs subscribers that a client has failed.
        /// </summary>
        protected virtual void OnClientFail(Client c, Exception ex)
        {
            var handler = ClientFail;
            handler?.Invoke(this, c, ex);
        }

        public bool Listening
        {
            get { return _listening; }
        }

        public int Port
        {
            get { return (int)_port; }
        }

        public int ClientsCount
        {
            get
            {
                lock (_clientsLock)
                {
                    return _clients.Count;
                }
            }
        }

        public Client[] Clients
        {
            get
            {
                lock (_clientsLock)
                {
                    return _clients.ToArray();
                }
            }
        }

        protected X509Certificate2 ServerCertificate { get; set; }

        public Server(X509Certificate2 serverCertificate)
        {
            ServerCertificate = serverCertificate;
        }

        public void Listen(int port, bool ipv6 = false, bool useUPnP = false)
        {
            ListenMany(new[] { (ushort)port }, ipv6, useUPnP);
        }

        public void ListenMany(IEnumerable<ushort> ports, bool ipv6 = false, bool useUPnP = false)
        {
            var portList = ports?.Where(port => port != 0).Distinct().ToList() ?? new List<ushort>();
            if (portList.Count == 0) return;

            if (_listening)
            {
                return;
            }

            if (_listenSockets.Count > 0 || _acceptEventArgs.Count > 0)
            {
                Disconnect();
            }

            ProcessingDisconnect = false;
            var sockets = new List<Socket>();
            var acceptEventArgs = new List<SocketAsyncEventArgs>();
            try
            {
                AddressFamily addressFamily = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
                IPAddress listenAddress = ipv6 ? IPAddress.IPv6Any : IPAddress.Any;

                foreach (var port in portList)
                {
                    var socket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.Bind(new IPEndPoint(listenAddress, port));
                    socket.Listen(1024);
                    sockets.Add(socket);

                    var eventArgs = new SocketAsyncEventArgs { UserToken = socket };
                    eventArgs.Completed += AcceptCompleted;
                    acceptEventArgs.Add(eventArgs);
                }

                _listenSockets.AddRange(sockets);
                _acceptEventArgs.AddRange(acceptEventArgs);
                _listeningPorts = new List<ushort>(portList);
                _port = portList[0];
                _listening = true;

                for (int i = 0; i < sockets.Count; i++)
                    AcceptAsync(sockets[i], acceptEventArgs[i]);

                OnServerState(_listening, _port);
            }
            catch
            {
                foreach (var eventArgs in acceptEventArgs)
                {
                    eventArgs.Completed -= AcceptCompleted;
                    eventArgs.Dispose();
                }
                foreach (var socket in sockets)
                    socket.Dispose();
                _listenSockets.Clear();
                _acceptEventArgs.Clear();
                _listening = false;
                throw;
            }
        }

        public ushort[] GetListeningPorts()
        {
            return _listeningPorts?.ToArray() ?? new ushort[] { (ushort)_port };
        }

        public void Disconnect()
        {
            ProcessingDisconnect = true;

            _listening = false;
            OnServerState(_listening, _port);

            var acceptEventArgs = _acceptEventArgs.ToArray();
            _acceptEventArgs.Clear();
            foreach (var eventArgs in acceptEventArgs)
            {
                eventArgs.Completed -= AcceptCompleted;
                eventArgs.Dispose();
            }

            var sockets = _listenSockets.ToArray();
            _listenSockets.Clear();
            foreach (var socket in sockets)
                socket.Dispose();

            Client[] clients;
            lock (_clientsLock)
            {
                clients = _clients.ToArray();
                _clients.Clear();
            }

            foreach (var client in clients)
            {
                DetachClient(client);
                client.Disconnect();
            }
        }

        protected bool ProcessingDisconnect { get; set; }

        private void AcceptAsync(Socket socket, SocketAsyncEventArgs eventArgs)
        {
            if (!_listening || socket == null || eventArgs == null) return;

            eventArgs.AcceptSocket = null;
            bool willRaiseEvent;
            try
            {
                willRaiseEvent = socket.AcceptAsync(eventArgs);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!willRaiseEvent)
            {
                AcceptCompleted(socket, eventArgs);
            }
        }

        private void AcceptCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (!_listening)
                return;

            if (e.SocketError == SocketError.Success)
            {
                try
                {
                    Socket clientSocket = e.AcceptSocket;
                    if (ClientsCount >= MaxConnections)
                    {
                        clientSocket.Close();
                        var limitSocket = e.UserToken as Socket;
                        if (limitSocket != null)
                            AcceptAsync(limitSocket, e);
                        return;
                    }

                    clientSocket.SetKeepAliveEx(KeepAliveInterval, KeepAliveTime);
                    clientSocket.NoDelay = true;

                    var networkStream = new NetworkStream(clientSocket, true);
                    var client = new Client(networkStream, (IPEndPoint)clientSocket.RemoteEndPoint, ServerCertificate);
                    if (client.Connected)
                    {
                        AddClient(client);
                        OnClientState(client, true);
                    }
                    else
                    {
                        client.Disconnect();
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        e.AcceptSocket?.Close();
                    }
                    catch { }
                    System.Diagnostics.Debug.WriteLine($"AcceptCompleted error: {ex.Message}");
                }
            }

            var listeningSocket = e.UserToken as Socket;
            if (listeningSocket != null)
                AcceptAsync(listeningSocket, e);
        }

        private const int KeepAliveInterval = 10000;
        private const int KeepAliveTime = 10000;
        private static readonly int MaxConnections = ReadLimit("PULSAR_MAX_SERVER_CONNECTIONS", 512, 1, 16384);

        private ushort _port;
        private bool _listening;
        private readonly List<Socket> _listenSockets = new List<Socket>();
        private readonly List<SocketAsyncEventArgs> _acceptEventArgs = new List<SocketAsyncEventArgs>();
        private List<ushort> _listeningPorts = new List<ushort>();
        private readonly object _clientsLock = new object();
        private readonly List<Client> _clients = new List<Client>();

        /// <summary>
        /// Adds a connected client to the list of clients,
        /// subscribes to the client's events, and notifies listeners.
        /// </summary>
        private void AddClient(Client client)
        {
            lock (_clientsLock)
            {
                client.ClientState += OnClientConnectionState;
                client.ClientRead += OnClientReadMessage;
                client.ClientFail += OnClientFailure;

                _clients.Add(client);
            }
            
            // Fire the ClientConnected event to notify subscribers that a new client has connected
            OnClientConnected(client);
        }

        /// <summary>
        /// Removes a disconnected client from the list of clients,
        /// unsubscribes from the client's events.
        /// </summary>
        private void RemoveClient(Client client)
        {
            bool removed;
            lock (_clientsLock)
            {
                removed = _clients.Remove(client);
            }

            if (!removed)
                return;

            DetachClient(client);
            OnClientDisconnected(client);
        }

        private void OnClientConnectionState(Client client, bool connected)
        {
            if (!connected)
                RemoveClient(client);

            OnClientState(client, connected);
        }

        private void OnClientFailure(Client client, Exception ex)
        {
            OnClientFail(client, ex);
            client.Disconnect();
            RemoveClient(client);
        }

        private void OnClientReadMessage(Client client, IMessage message, int length)
        {
            OnClientRead(client, message);
        }

        private void DetachClient(Client client)
        {
            if (client == null)
                return;

            client.ClientState -= OnClientConnectionState;
            client.ClientRead -= OnClientReadMessage;
            client.ClientFail -= OnClientFailure;
        }

        private static int ReadLimit(string name, int fallback, int minimum, int maximum)
        {
            var text = Environment.GetEnvironmentVariable(name);
            return int.TryParse(text, out var value) && value >= minimum && value <= maximum ? value : fallback;
        }
    }
}
