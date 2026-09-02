[CmdletBinding()]
param(
    [int]$MockApiPort = 8080,
    [int]$WebSocketPort = 8081,
    [string]$AllPhotosPath = "",
    [string]$ActivePhotosPath = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$ProjectPath = Join-Path $RepoRoot "src\TwitchSubscriberPictures\TwitchSubscriberPictures.csproj"
$ApiBaseUrl = "http://localhost:$MockApiPort"

$Clients = Invoke-RestMethod "$ApiBaseUrl/units/clients"
$Subs = Invoke-RestMethod "$ApiBaseUrl/units/subscriptions"

$TopBroadcaster = $Subs.data |
    Group-Object broadcaster_id |
    Sort-Object Count -Descending |
    Select-Object -First 1

$ClientId = $Clients.data[0].ID
$ClientSecret = $Clients.data[0].Secret
$BroadcasterId = $TopBroadcaster.Name

$TokenUrl = "$ApiBaseUrl/auth/authorize" +
    "?client_id=$ClientId" +
    "&client_secret=$ClientSecret" +
    "&grant_type=user_token" +
    "&user_id=$BroadcasterId" +
    "&scope=channel:read:subscriptions"

$Token = Invoke-RestMethod -Method Post $TokenUrl

$env:TSP_MOCK_API_BASE_URL = $ApiBaseUrl
$env:TSP_MOCK_ACCESS_TOKEN = $Token.access_token
$env:TSP_MOCK_BROADCASTER_ID = $BroadcasterId
$env:TSP_MOCK_CLIENT_ID = $ClientId
$env:TSP_MOCK_WEBSOCKET_URL = "ws://localhost:$WebSocketPort/ws"

if ($AllPhotosPath) {
    New-Item -ItemType Directory -Force -Path $AllPhotosPath | Out-Null
    Write-Host "AllPhotos folder:  $AllPhotosPath"
}

if ($ActivePhotosPath) {
    New-Item -ItemType Directory -Force -Path $ActivePhotosPath | Out-Null
    Write-Host "ActivePhotos folder: $ActivePhotosPath"
}

Write-Host "Starting app against mock broadcaster $BroadcasterId"

dotnet run --project $ProjectPath
