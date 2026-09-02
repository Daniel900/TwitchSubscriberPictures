# Twitch CLI mock testing scripts

These scripts start the bundled Twitch CLI mock API/EventSub server, launch the
app against that mock server, and trigger subscription events.

## Quick start

From PowerShell:

```powershell
cd testing
.\start-mock-api.ps1
.\start-app-mock.ps1
```

Then select the `AllPhotos` and `ActivePhotos` folders in the app, or pass them
when starting:

```powershell
.\start-app-mock.ps1 `
    -AllPhotosPath "C:\Test\AllPhotos" `
    -ActivePhotosPath "C:\Test\ActivePhotos"
```

## Trigger events

The scripts read the current EventSub session ID from the app log automatically
when you do not pass `-SessionId`.

```powershell
.\trigger-subscribe.ps1
.\trigger-prime.ps1
.\trigger-gift.ps1
.\trigger-unsubscribe.ps1
```

You can also pass explicit values:

```powershell
.\trigger-subscribe.ps1 -SessionId "abc123_xyz" -BroadcasterId "44955106" -FromUserId "123456"
```

## Stop mock servers

```powershell
.\stop-mock-api.ps1
```
