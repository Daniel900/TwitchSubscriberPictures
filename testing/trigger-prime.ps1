[CmdletBinding()]
param(
    [string]$SessionId = "",
    [string]$BroadcasterId = "",
    [string]$FromUserId = "",
    [int]$MockApiPort = 8080
)

& (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "trigger-subscribe.ps1") `
    -SessionId $SessionId `
    -BroadcasterId $BroadcasterId `
    -FromUserId $FromUserId `
    -Tier "1000" `
    -MockApiPort $MockApiPort
