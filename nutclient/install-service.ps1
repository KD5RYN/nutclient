# Install NutClient as a Windows Service
# Run this script as Administrator

$serviceName = "NutUpsMonitor"
$displayName = "NUT UPS Monitor"
$description = "Monitors NUT UPS server and initiates graceful shutdown on power loss"
$exePath = Join-Path $PSScriptRoot "NutClient.exe"

if (-not (Test-Path $exePath)) {
    Write-Error "NutClient.exe not found at $exePath"
    Write-Host "Build first: dotnet publish -c Release"
    exit 1
}

# Stop and remove existing service if present
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing service..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Installing service..."
sc.exe create $serviceName binPath= "$exePath" start= auto DisplayName= "$displayName"
sc.exe description $serviceName "$description"
sc.exe failure $serviceName reset= 86400 actions= restart/10000/restart/30000/restart/60000

# Start it
Write-Host "Starting service..."
Start-Service -Name $serviceName

$svc = Get-Service -Name $serviceName
Write-Host "Service status: $($svc.Status)"
Write-Host ""
Write-Host "Manage with:"
Write-Host "  Start-Service $serviceName"
Write-Host "  Stop-Service $serviceName"
Write-Host "  Get-Service $serviceName"
Write-Host ""
Write-Host "View logs: Get-Content C:\Scripts\nutclient.log -Tail 20"
