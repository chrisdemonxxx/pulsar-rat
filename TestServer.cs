using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

class TestServer
{
    static string logFile = Path.Combine(Path.GetTempPath(), "pulsar_test_server.log");
    
    static void Main()
    {
        Log("=== TEST SERVER STARTED ===");
        Log($"Process ID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
        
        try
        {
            Log("Creating TcpListener on 127.0.0.1:4783");
            TcpListener listener = new TcpListener(IPAddress.Loopback, 4783);
            
            Log("Starting listener...");
            listener.Start();
            Log("Listener started successfully");
            
            // Check if listening
            Socket s = listener.Server;
            Log($"Socket bound: {s.IsBound}, LocalEndPoint: {s.LocalEndPoint}");
            
            Log("Waiting for connections...");
            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                Log($"Client connected: {client.Client.RemoteEndPoint}");
                
                Task.Run(() => HandleClient(client));
            }
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Log($"Stack: {ex.StackTrace}");
        }
    }
    
    static void HandleClient(TcpClient client)
    {
        try
        {
            using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                Log($"Received {bytesRead} bytes from {client.Client.RemoteEndPoint}");
                
                if (bytesRead > 0)
                {
                    Log($"Data: {BitConverter.ToString(buffer, 0, Math.Min(bytesRead, 64))}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Client error: {ex.Message}");
        }
        finally
        {
            client.Close();
            client.Dispose();
        }
    }
    
    static void Log(string message)
    {
        string logMsg = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        try { File.AppendAllText(logFile, logMsg + "\r\n"); } catch { }
        Console.WriteLine(logMsg);
    }
}
