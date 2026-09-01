# Twitch Subscriber Pictures

Windows desktop application for managing subscriber photos shown during a Twitch
stream. It keeps an `ActivePhotos` folder synchronized with the current Twitch
subscriber list and a master `AllPhotos` folder where one photo per subscriber is
stored by Twitch login name.

The app is built with .NET 10, WPF, TwitchLib (Helix API and EventSub WebSocket
client), `Hardcodet.NotifyIcon.Wpf`, and xUnit.

## Features

- Selectable `AllPhotos` and `ActivePhotos` folders, persisted between runs.
- Twitch Helix subscriber sync on startup, manual "Update now", and EventSub.
- Configurable polling fallback (minimum 10 minutes, default 15).
- Encrypted Twitch token storage with Windows DPAPI.
- In-app status and error log instead of message boxes or popups.
- Close prompt with Minimize to tray / Close application / Cancel.
- Tray icon that reflects connection status; left-click restores the window,
  right-click opens a context menu with Open and Close.

## Repository layout

```
src/TwitchSubscriberPictures.Core     Non-UI services and models
src/TwitchSubscriberPictures          WPF application, tray icon, UI
tests/TwitchSubscriberPictures.Tests  xUnit unit tests
tests/TwitchSubscriberPictures.IntegrationTests  xUnit Twitch CLI integration tests
twitch-cli_1.1.24_Windows_x86_64      Bundled Twitch CLI used by integration tests
```

## Requirements

- Windows 10 or later
- .NET 10 SDK (the included `global.json` targets 10.0.400)
- A Twitch application configured as a **Public** application
  - The public app provides a Client ID but no Client Secret.
  - Public apps use Twitch Device Code Flow, which is appropriate for distributed
    desktop apps because no secret has to be embedded in the client.

## Registering the Twitch application

1. Go to [Twitch Developers Console](https://dev.twitch.tv/console/apps).
2. Register an application, or open an existing one.
3. Note the Client ID.
4. Copy the Client ID.
5. Enter the Client ID in the app and click **Connect Twitch**.

The only requested scope is `channel:read:subscriptions`.

Because this is a public application, there is no Client Secret. The app uses
Twitch's Device Code Flow:

1. The app requests a device code from Twitch.
2. It opens `https://www.twitch.tv/activate` and shows the code in the log.
3. You log into Twitch and approve the requested scope.
4. The app polls Twitch in the background until authorization is complete.

## Folder behavior

- `AllPhotos` is the master folder. A subscriber's filename without its extension
  must equal their Twitch login name, for example `alice.png` or `alice.jpg`.
  Any image format is supported.
- `ActivePhotos` is managed automatically. Matching photos are copied there when
  a subscriber is active and removed from there only when they become inactive.
  Files are never deleted from `AllPhotos`.
- If more than one file exists for a login, the app uses the first match and logs
  the ambiguity.
- Subscribers without a photo appear in the active-subscriber grid with the
  **Missing photo** checkbox checked.

## Build and run

```powershell
dotnet restore TwitchSubscriberPictures.slnx
dotnet build TwitchSubscriberPictures.slnx
dotnet run --project src/TwitchSubscriberPictures/TwitchSubscriberPictures.csproj
```

The first launch starts with no token. Enter the Twitch Client ID, then click
**Connect Twitch**. The app opens the Twitch activation page and displays the
device code in the in-app log. You only need to log into Twitch and approve the
requested scope manually.

## Token storage

`TokenFileStore` serializes the token as JSON and protects those bytes with
`DpapiTokenProtector` using Windows DPAPI (`ProtectedData`) before writing the file.
Access tokens are never written to disk as plaintext. The file is stored under
`%LOCALAPPDATA%\TwitchSubscriberPictures\twitch-token.bin`.

## No-popup error handling

While the main window is open, errors are written to the in-app activity/error log.
The app does not use `MessageBox` for errors or status changes. The only modal
dialog is the user-initiated close prompt; tray **Close** exits immediately.

## Unit tests

```powershell
dotnet test tests/TwitchSubscriberPictures.Tests/TwitchSubscriberPictures.Tests.csproj
```

The unit tests cover file sync/reconciliation, encrypted token storage, and settings
persistence.

## Twitch CLI mock API integration tests

The integration tests use the bundled Twitch CLI in
`twitch-cli_1.1.24_Windows_x86_64`.

They:

- generate mock Twitch users/subscriptions with `twitch mock-api generate`;
- start `twitch mock-api start` for Helix API calls;
- start `twitch event websocket start-server` for EventSub events;
- exercise subscriber fetching and a
  `channel.subscribe` WebSocket event.

Run them with:

```powershell
dotnet test tests/TwitchSubscriberPictures.IntegrationTests/TwitchSubscriberPictures.IntegrationTests.csproj
```

The fixture chooses free localhost ports automatically, so it does not conflict
with a manually running Twitch CLI instance. If you already have a Twitch CLI
database or mock servers running, the tests still create additional mock data;
that is expected for a local mock API.

## Twitch CLI setup for manual testing

The bundled CLI can also be used manually:

```powershell
.\twitch-cli_1.1.24_Windows_x86_64\twitch.exe mock-api generate -c 10
.\twitch-cli_1.1.24_Windows_x86_64\twitch.exe mock-api start -p 8080
```

In another terminal:

```powershell
.\twitch-cli_1.1.24_Windows_x86_64\twitch.exe event websocket start-server -p 8081
```

The mock Helix API is available at `http://localhost:8080/mock`. The mock
WebSocket server is available at `ws://localhost:8081/ws`.

## Connection states

- **Disconnected** — no active connection has been established.
- **Token invalid** — no token is stored, the token is invalid, or credentials are missing.
- **Connected** — token is valid and Helix sync is working.
- **Connected — EventSub live** — the EventSub WebSocket is connected and real-time
  subscription changes are being received.

## Implementation notes

- `TwitchEventSubService` uses bounded exponential backoff when the EventSub
  WebSocket drops, and re-registers subscriptions after a non-reconnect connection.
- `TrayIconController` creates the taskbar icon once and disposes it only on
  application exit, preventing duplicate icons after minimize/restore cycles.
- `PhotoSyncEngine` catches per-subscriber copy/delete failures and returns them
  as loggable errors, so one bad file cannot block the rest of the reconciliation.
