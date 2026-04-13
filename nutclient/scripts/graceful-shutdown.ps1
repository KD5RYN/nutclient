# Graceful Shutdown Script for NUT UPS Monitor (Windows)
# Called by NutClient.exe when UPS power loss triggers shutdown.
#
# Arguments passed by NutClient:
#   -Reason         Why shutdown was triggered (timer_expired, low_battery,
#                   forced_shutdown, battery_charge, battery_runtime)
#   -BatteryCharge  Battery % at time of shutdown (-1 if unknown)
#   -BatteryRuntime Estimated runtime in seconds (-1 if unknown)
#   -UpsStatus      Raw UPS status string (e.g., "OB LB")
#
# Customize this script for your server:
#   - Stop specific services before shutdown
#   - Save application state
#   - Stop Hyper-V VMs (handled automatically below)
#   - Send notifications
#
# Default location: C:\Scripts\graceful-shutdown.ps1
# Configured in nutclient.json under Monitoring.ShutdownArguments

param(
    [string]$Reason = "unknown",
    [int]$BatteryCharge = -1,
    [int]$BatteryRuntime = -1,
    [string]$UpsStatus = ""
)

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$logFile = "C:\Scripts\shutdown-log.txt"

Add-Content -Path $logFile -Value "$timestamp - UPS shutdown initiated (reason: $Reason, charge: $BatteryCharge%, runtime: ${BatteryRuntime}s, status: $UpsStatus)"

# --- Stop Hyper-V VMs (if this is a Hyper-V host) ---
if (Get-Command Get-VM -ErrorAction SilentlyContinue) {
    $vms = Get-VM | Where-Object { $_.State -eq 'Running' }
    foreach ($vm in $vms) {
        Add-Content -Path $logFile -Value "$timestamp - Stopping VM: $($vm.Name)"
        Stop-VM -Name $vm.Name -Force -AsJob
    }
    # Wait for all VMs to finish shutting down (up to 3 minutes)
    Get-Job | Wait-Job -Timeout 180 | Out-Null
    Get-Job | Remove-Job -Force
    Add-Content -Path $logFile -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - All VMs stopped"
}

# --- Add custom shutdown tasks here ---
# Example: Stop a specific service
# Stop-Service -Name "MyAppService" -Force
# Add-Content -Path $logFile -Value "$timestamp - Stopped MyAppService"

# --- Shut down Windows ---
Add-Content -Path $logFile -Value "$timestamp - Shutting down Windows"
Stop-Computer -Force
