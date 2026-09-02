[CmdletBinding()]
param(
    [int]$MockApiPort = 8080,
    [int]$WebSocketPort = 8081,
    [int]$Count = 25
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$TwitchCli = Join-Path $RepoRoot "twitch-cli_1.1.24_Windows_x86_64\twitch.exe"

if (-not (Test-Path $TwitchCli)) {
    throw "Twitch CLI was not found at $TwitchCli"
}

& $TwitchCli mock-api generate -c $Count
if ($LASTEXITCODE -ne 0) {
    throw "Twitch CLI mock-api generate failed with exit code $LASTEXITCODE"
}

$CliDirectory = Split-Path -Parent $TwitchCli

$ApiProcess = Start-Process `
    -FilePath $TwitchCli `
    -ArgumentList @("mock-api", "start", "-p", "$MockApiPort") `
    -WorkingDirectory $CliDirectory `
    -WindowStyle Hidden `
    -PassThru

$WebSocketProcess = Start-Process `
    -FilePath $TwitchCli `
    -ArgumentList @("event", "websocket", "start-server", "-p", "$WebSocketPort") `
    -WorkingDirectory $CliDirectory `
    -WindowStyle Hidden `
    -PassThru

Set-Content -Path (Join-Path $ScriptDir "mock-api.pid") -Value $ApiProcess.Id
Set-Content -Path (Join-Path $ScriptDir "websocket.pid") -Value $WebSocketProcess.Id

$ApiBaseUrl = "http://localhost:$MockApiPort"
$ready = $false
for ($i = 0; $i -lt 40; $i++) {
    try {
        Invoke-RestMethod "$ApiBaseUrl/units/clients" | Out-Null
        $ready = $true
        break
    }
    catch {
        Start-Sleep -Milliseconds 500
    }
}

if (-not $ready) {
    throw "Mock API did not become ready at $ApiBaseUrl"
}

Start-Sleep -Seconds 1

Write-Host "Mock API:      $ApiBaseUrl"
Write-Host "WebSocket:     ws://localhost:$WebSocketPort/ws"
Write-Host "PID files:     mock-api.pid, websocket.pid"
