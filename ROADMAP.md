# Pulsar Roadmap

## Web API
A web API (e.g. REST-like) allows to interact with the clients in more flexible ways and can be used to build a web interface for Pulsar.
✅ Implemented (Oct 2026): `Pulsar.Server/Networking/WebApi/WebApiServer.cs` — Kestrel-hosted REST API inside the server process. Enable in Settings → Web API (port + API key; refuses to start without a key). Endpoints under `/api`: version, stats, clients (list/detail), per-client actions (shutdown/restart/standby/lockscreen/reconnect/disconnect/uninstall), quickcommand, messagebox. Auth: `X-Api-Key` header, constant-time compared. Smoke-tested end-to-end via `tools/webapi-smoke`.
✅ v2 (Oct 2026): live event stream (`GET /api/events`, SSE: client connect/disconnect + server log lines + heartbeat), recent-log endpoint (`/api/log`), known/offline clients from the SQLite repository (`/api/knownclients`, `/api/offline`), autotasks listing (`/api/autotasks`), and an embedded dark-theme web dashboard at `/` (key prompt in-browser; all data still requires the key). Smoke: 15/15.

## Transparent communication protocol
To open up the way for Pulsar clients in different programming languages the communication protocol needs to be clearly specified and documented.
✅ Implemented (Oct 2026): see `PROTOCOL.md` — wire-exact spec (framing, MessagePack envelope, AES-256-GCM security envelope, handshake, message inventory).

## Cross-platform support
A long-term goal is to support operating systems such as MacOS and Linux. The new .NET Core framework will help achieve this goal.

## Command line (CLI) version of the server
It should be possible to use the server as a simple CLI tool to accept and forward (proxy) connections to other servers.
✅ Implemented (Oct 2026): `Pulsar.Relay` — dependency-free CLI TCP relay (accept + round-robin forward to upstream servers, byte-pipe, no protocol parsing).

## GUI overhaul
The GUI needs to be reworked in a more modern way, such as WPF or a web-based interface. WPF as GUI framework would drastically improve rendering performance of the remote desktop with the hardware accelerated rendering, similar to a web-based GUI depending on the used browser.

## Allow different types of clients (permissioned clients)
Allow clients with higher privileges (i.e. ability to administrate other lower privileged clients) connect to the server. This change would allow administrators to manage clients from their own computers with a lightweight client without having to run the server.
✅ Implemented v1 (Oct 2026): `Pulsar.AdminClient` (net9 console) authenticates with the server's configured admin key (`adminClientKey` in settings, FrmSettings → Admin key; empty = feature off), then lists managed clients, follows live connect/disconnect events and relays whitelisted commands (shutdown/restart/standby/lockscreen/reconnect/disconnect/uninstall/messagebox/quickcommand) through the server. Wire-additive only (8 new message types, zero changes to existing ones) — old clients/servers interop. Admin relay: `Pulsar.Server/Messages/AdminRelayHandler.cs`. Smoke: `tools/admin-relay-smoke` (7/7 live, real server + real exe).

## Allow client installation as Windows service
Currently the client is installed on a per-user basis and this makes it unflexible to remotly manage the machine when the user is not logged in. it also requires the client to be installed for every account who uses the machine. Machines which are used by multiple users would greatly benefit when Pulsar could be run as a Windows service.
✅ Implemented (Oct 2026): builder flag "Windows Service" (`INSTALLSERVICE`) registers an auto-start service via sc.exe pointing at the installed copy with `-service`; `Pulsar.Client/Setup/PulsarService.cs` runs the client headless in session 0. Requires an elevated install.

## Basic client GUI
Add a basic GUI to the client to allow the user at any time to check the status if a specific feature is active. Additionally it can show a notification when the client gets installed or when someone requests permission to use remote desktop (similar to teamviewer).
✅ Implemented v1 (Oct 2026): builder flag "Client GUI" (`SHOWCLIENTGUI`, default off) — tray icon with status window (connection, hosts, tag, version, keylogger, install path), connect/disconnect notifications.
✅ v2 (Oct 2026): remote-desktop consent prompt — on a user-facing build, an operator's session start triggers a topmost Allow/Deny dialog on the client (`Pulsar.Client/GUI/FrmRemoteDesktopPermissionPrompt.cs`, 30s auto-deny so unattended machines never stream without consent); mid-session reconfigures do not re-prompt. Denial notifies the operator via the wire-additive `DoRemoteDesktopPermissionResponse` (old servers drop it silently — no migration needed).


# KDOT NOTES

## Password Recovery
Add Browser Scraping so I don't have to have predefined paths for each file.
✅ Implemented (Oct 2026): `Recovery/Crawler/Crawl.cs` crawls `%LocalAppData%`/`%AppData%` depth-3 for both `User Data`-layout and flat Opera-style Chromium profiles — no predefined paths needed.
