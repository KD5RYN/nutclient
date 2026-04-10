#!/bin/bash
# Graceful Shutdown Script for NUT UPS Monitor (Linux)
# Called by NutClient when UPS power loss triggers shutdown.
#
# Arguments passed by NutClient:
#   $1  Reason: timer_expired, low_battery, forced_shutdown, battery_charge, battery_runtime
#   $2  Battery charge % (-1 if unknown)
#   $3  Battery runtime in seconds (-1 if unknown)
#   $4  Raw UPS status string (e.g., "OB LB")
#
# Customize this script for your server:
#   - Stop Docker containers
#   - Stop specific systemd services
#   - Unmount network shares
#   - Send notifications
#
# Default location: /opt/nutclient/scripts/graceful-shutdown.sh
# Configured in nutclient.json under Monitoring.ShutdownArguments
# Must be executable: chmod +x graceful-shutdown.sh

REASON="${1:-unknown}"
BATTERY_CHARGE="${2:--1}"
BATTERY_RUNTIME="${3:--1}"
UPS_STATUS="${4:-}"

LOGFILE="/var/log/nutclient-shutdown.log"
TIMESTAMP=$(date "+%Y-%m-%d %H:%M:%S")

echo "$TIMESTAMP - UPS shutdown initiated (reason: $REASON, charge: ${BATTERY_CHARGE}%, runtime: ${BATTERY_RUNTIME}s, status: $UPS_STATUS)" >> "$LOGFILE"

# --- Stop Docker containers (uncomment if using Docker) ---
# if command -v docker &> /dev/null; then
#     echo "$TIMESTAMP - Stopping all Docker containers" >> "$LOGFILE"
#     docker stop $(docker ps -q) 2>/dev/null
#     echo "$TIMESTAMP - Docker containers stopped" >> "$LOGFILE"
# fi

# --- Stop specific services (uncomment and edit as needed) ---
# echo "$TIMESTAMP - Stopping myapp service" >> "$LOGFILE"
# systemctl stop myapp
# echo "$TIMESTAMP - myapp stopped" >> "$LOGFILE"

# --- Add custom shutdown tasks here ---

# --- Shut down ---
echo "$TIMESTAMP - Shutting down" >> "$LOGFILE"
/sbin/poweroff
