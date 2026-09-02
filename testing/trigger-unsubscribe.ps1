[CmdletBinding()]
param(
    [string]$SessionId = "",
    [string]$BroadcasterId = "",
    [string]$FromUserId = "",
    [int]$MockApiPort = 8080
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$TwitchCli = Join-Path $RepoRoot "twitch-cli_1.1.24_Windows_x86_64\twitch.exe"
$ApiBaseUrl = "http://localhost:$MockApiPort"

if (-not $SessionId) {
    $SessionId = & (Join-Path $ScriptDir "Get-EventSubSession.ps1")
}

if (-not $BroadcasterId) {
    $BroadcasterId = $env:TSP_MOCK_BROADCASTER_ID
}

if (-not $FromUserId) {
    $Users = Invoke-RestMethod "$ApiBaseUrl/units/users"
    $FromUserId = $Users.data[0].id
}

& $TwitchCli event trigger unsubscribe `
    --transport=websocket `
    --session=$SessionId `
    --to-user=$BroadcasterId `
    --from-user=$FromUserId
