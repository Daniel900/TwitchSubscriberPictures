[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$LogPath = Join-Path $env:LOCALAPPDATA "TwitchSubscriberPictures\logs\app.log"
if (-not (Test-Path $LogPath)) {
    throw "App log was not found at $LogPath"
}

$SessionId = Get-Content $LogPath |
    ForEach-Object {
        if ($_ -match "EventSub WebSocket connected \((?<id>[^)]+)\)") {
            $matches["id"]
        }
    } |
    Select-Object -Last 1

if (-not $SessionId) {
    throw "No EventSub session ID found in app.log. Connect the app in mock mode first."
}

$SessionId
