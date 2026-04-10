# Uninstall NutClient Windows Service
# Run this script as Administrator

$serviceName = "NutUpsMonitor"

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$serviceName' is not installed."
    exit 0
}

Write-Host "Stopping service..."
Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "Removing service..."
sc.exe delete $serviceName

Write-Host "Service removed."
