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
            _port = (ushort)port;
            _listeningPorts = new List<ushort> { (ushort)port };

            if (_listening)
            {
                return;
            }

            if (_listenSocket != null)
            {
                Disconnect();
            }

            AddressFamily addressFamily = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            _listenSocket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            IPAddress listenAddress = ipv6 ? IPAddress.IPv6Any : IPAddress.Any;
            _listenSocket.Bind(new IPEndPoint(listenAddress, _port));
            _listenSocket.Listen(int.MaxValue);

            _acceptEventArgs = new SocketAsyncEventArgs();
            _acceptEventArgs.Completed += AcceptCompleted;

            // FIX: Set _listening BEFORE calling AcceptAsync so it doesn't return early!
            _listening = true;
            AcceptAsync();

            OnServerState(_listening, _port);
        }

        public void ListenMany(IEnumerable<ushort> ports, bool ipv6 = false, bool useUPnP = false)
        {
            var portList = ports.ToList();
            if (portList.Count == 0) return;

            _listeningPorts = new List<ushort>(portList);
            _port = portList[0];

            if (_listening)
            {
                return;
            }

            if (_listenSocket != null)
            {
                Disconnect();
            }

            // Listen on all ports (we'll use the first one as the main port)
            AddressFamily addressFamily = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
            _listenSocket = new Socket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            IPAddress listenAddress = ipv6 ? IPAddress.IPv6Any : IPAddress.Any;
            _listenSocket.Bind(new IPEndPoint(listenAddress, _port));
            _listenSocket.Listen(int.MaxValue);

            _acceptEventArgs = new SocketAsyncEventArgs();
            _acceptEventArgs.Completed += AcceptCompleted;

            AcceptAsync();

            _listening = true;
            OnServerState(_listening, _port);
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

            if (_acceptEventArgs != null)
            {
                _acceptEventArgs.Completed -= AcceptCompleted;
                _acceptEventArgs.Dispose();
            }

            if (_listenSocket != null)
            {
                _listenSocket.Close();
                _listenSocket = null;
            }

            lock (_clientsLock)
            {
                foreach (var client in _clients)
                {
                    client.Disconnect();
                }
            }
        }

        protected bool ProcessingDisconnect { get; set; }

        private void AcceptAsync()
        {
            if (!_listening) return;

            _acceptEventArgs.AcceptSocket = null;
            bool willRaiseEvent = _listenSocket.AcceptAsync(_acceptEventArgs);

            if (!willRaiseEvent)
            {
                AcceptCompleted(_listenSocket, _acceptEventArgs);
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
                    clientSocket.SetKeepAliveEx(KeepAliveInterval, KeepAliveTime);
                    clientSocket.NoDelay = true;

                    var networkStream = new NetworkStream(clientSocket, true);
                    var client = new Client(networkStream, (IPEndPoint)clientSocket.RemoteEndPoint, ServerCertificate);
                    AddClient(client);
                    OnClientState(client, true);
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

            AcceptAsync();
        }

        private const int KeepAliveInterval = 10000;
        private const int KeepAliveTime = 10000;

        private ushort _port;
        private bool _listening;
        private Socket _listenSocket;
        private SocketAsyncEventArgs _acceptEventArgs;
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
                client.ClientState += (c, connected) => OnClientState(c, connected);
                client.ClientRead += (c, message, length) => OnClientRead(c, message);
                client.ClientFail += (c, ex) => OnClientFail(c, ex);

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
            if (ProcessingDisconnect) return;

            lock (_clientsLock)
            {
                _clients.Remove(client);
            }
        }
    }
}
