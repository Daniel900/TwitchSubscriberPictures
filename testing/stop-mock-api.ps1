[CmdletBinding()]
param()

$ErrorActionPreference = "SilentlyContinue"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

foreach ($PidFile in @("mock-api.pid", "websocket.pid")) {
    $Path = Join-Path $ScriptDir $PidFile
    if (Test-Path $Path) {
        $ProcessId = [int](Get-Content $Path)
        Stop-Process -Id $ProcessId -Force
        Remove-Item $Path -Force
    }
}

Write-Host "Mock API and EventSub WebSocket processes stopped."
