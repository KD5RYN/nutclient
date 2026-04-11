# NutClient Install Script for Windows
# Run as Administrator: powershell -File install.ps1
#
# Installs NutClient to C:\NutClient and sets up a Windows service.
# If nutclient.json already exists at the destination, it will NOT be overwritten.

#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"

$InstallDir = "C:\NutClient"
$ScriptsDir = "C:\Scripts"
$ServiceName = "NutUpsMonitor"
$DisplayName = "NUT UPS Monitor"
$Description = "Monitors NUT UPS server and initiates graceful shutdown on power loss"
$SourceDir = $PSScriptRoot

Write-Host "Installing NutClient to $InstallDir..."

# Stop and remove existing service if present
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing service..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Create directories
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
New-Item -ItemType Directory -Force -Path $ScriptsDir | Out-Null

# Copy binary
Copy-Item -Path "$SourceDir\NutClient.exe" -Destination "$InstallDir\NutClient.exe" -Force

# Copy config — don't overwrite existing
$NeedsConfig = $false
if (-not (Test-Path "$InstallDir\nutclient.json")) {
    Copy-Item -Path "$SourceDir\nutclient.json" -Destination "$InstallDir\nutclient.json"
    Write-Host ""
    Write-Host "Created $InstallDir\nutclient.json from template." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "*** IMPORTANT: Edit $InstallDir\nutclient.json before starting the service ***" -ForegroundColor Red
    Write-Host "    Set your NUT server host, UPS name, and credentials."
    Write-Host ""
    $NeedsConfig = $true
} else {
    Write-Host "Keeping existing $InstallDir\nutclient.json"
}

# Copy shutdown script — don't overwrite existing
if (-not (Test-Path "$ScriptsDir\graceful-shutdown.ps1")) {
    Copy-Item -Path "$SourceDir\scripts\graceful-shutdown.ps1" -Destination "$ScriptsDir\graceful-shutdown.ps1"
    Write-Host "Installed example shutdown script to $ScriptsDir\graceful-shutdown.ps1"
} else {
    Write-Host "Keeping existing $ScriptsDir\graceful-shutdown.ps1"
}

# Install Windows service
$exePath = "$InstallDir\NutClient.exe"
sc.exe create $ServiceName binPath= "$exePath" start= auto DisplayName= "$DisplayName" | Out-Null
sc.exe description $ServiceName "$Description" | Out-Null

# Failure actions: never give up.
#   1st failure → restart after 10 seconds
#   2nd failure → restart after 30 seconds
#   3rd failure → restart after 5 minutes
#   4th+ failure → keeps using the last action (restart every 5 minutes) forever
# reset=86400 means the failure counter resets after 24 hours of running successfully,
# so a fresh crash gets the quick 10s restart again.
# failureflag 1 ensures the failure actions also fire on clean exits (exit code 0),
# not just crashes.
sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/300000 | Out-Null
sc.exe failureflag $ServiceName 1 | Out-Null

Write-Host ""
Write-Host "Installation complete."
Write-Host ""
Write-Host "  Binary:  $InstallDir\NutClient.exe"
Write-Host "  Config:  $InstallDir\nutclient.json"
Write-Host "  Script:  $ScriptsDir\graceful-shutdown.ps1"
Write-Host "  Service: $DisplayName ($ServiceName)"
Write-Host ""

if ($NeedsConfig) {
    Write-Host "Edit the config first, then start the service:" -ForegroundColor Yellow
    Write-Host "  notepad $InstallDir\nutclient.json"
    Write-Host "  Start-Service $ServiceName"
} else {
    Write-Host "Starting service..."
    Start-Service -Name $ServiceName
    $svc = Get-Service -Name $ServiceName
    Write-Host "Service status: $($svc.Status)"
}

Write-Host ""
Write-Host "Commands:"
Write-Host "  Get-Service $ServiceName                             # check status"
Write-Host "  Restart-Service $ServiceName                         # restart"
Write-Host "  Get-Content C:\Scripts\nutclient.log -Tail 20        # view logs"
Write-Host "  Get-Content C:\Scripts\nutclient-status.json         # quick status"
