#!/bin/bash
# NutClient Install Script for Linux
# Run as root: sudo ./install.sh
#
# Installs NutClient to /opt/nutclient and sets up a systemd service.
# If nutclient.json already exists at the destination, it will NOT be overwritten.
#
# TESTED ON: Debian 12, Raspberry Pi OS (Debian-based)
#
# SHOULD WORK ON any systemd-based Linux distro:
#   Debian, Ubuntu, Raspberry Pi OS, RHEL, CentOS, Fedora, Rocky, Alma,
#   openSUSE, Arch — the script only uses systemctl and standard FHS paths.
#
# NOTES FOR NON-DEBIAN DISTROS:
#
#   RHEL / CentOS / Fedora / Rocky / Alma:
#     - SELinux may block the shutdown script execution. If the service fails
#       to start or run the shutdown command, check with: sudo ausearch -m avc
#       To temporarily allow: sudo setenforce 0 (test only, not for production).
#       For a permanent fix, create an SELinux policy module.
#     - If the log directory /var/log is locked down, set LogFile in
#       nutclient.json to a writable path like /opt/nutclient/nutclient.log.
#
#   Synology DSM:
#     - Synology has its own init system and pre-installed NUT. This script
#       won't work as-is. Manual install: copy NutClient to /usr/local/bin,
#       create an upstart/systemd unit, or use Task Scheduler to start it.
#
#   Alpine / OpenRC-based distros:
#     - This script requires systemd. Alpine and other OpenRC distros need
#       a manual install: copy binary + config, create an OpenRC init script.
#
#   Void / runit:
#     - Similar to Alpine — needs a manual runit service directory setup.

set -e

INSTALL_DIR="/opt/nutclient"
SCRIPT_DIR="$INSTALL_DIR/scripts"
SERVICE_FILE="/etc/systemd/system/nutclient.service"
SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"

if [ "$EUID" -ne 0 ]; then
    echo "Please run as root: sudo ./install.sh"
    exit 1
fi

if ! command -v systemctl >/dev/null 2>&1; then
    echo "ERROR: systemctl not found — this script requires systemd."
    echo ""
    echo "For non-systemd distros (Alpine, Void, Synology, old Gentoo, etc.)"
    echo "you'll need to install manually. See the header of this script for notes."
    exit 1
fi

echo "Installing NutClient to $INSTALL_DIR..."

# Stop existing service if running
if systemctl is-active --quiet nutclient 2>/dev/null; then
    echo "Stopping existing nutclient service..."
    systemctl stop nutclient
fi

# Create directories
mkdir -p "$INSTALL_DIR"
mkdir -p "$SCRIPT_DIR"

# Copy binary
cp "$SOURCE_DIR/NutClient" "$INSTALL_DIR/NutClient"
chmod +x "$INSTALL_DIR/NutClient"

# Copy config — don't overwrite existing
if [ ! -f "$INSTALL_DIR/nutclient.json" ]; then
    cp "$SOURCE_DIR/nutclient.json.linux-example" "$INSTALL_DIR/nutclient.json"
    echo "Created $INSTALL_DIR/nutclient.json from template."
    echo ""
    echo "*** IMPORTANT: Edit $INSTALL_DIR/nutclient.json before starting the service ***"
    echo "    Set your NUT server host, UPS name, and credentials."
    echo ""
    NEEDS_CONFIG=true
else
    echo "Keeping existing $INSTALL_DIR/nutclient.json"
fi

# SECURITY: lock down config file permissions — it contains the NUT password
# in plaintext. Only root should be able to read it.
# Applied unconditionally so upgrading from an older install also fixes perms.
chown root:root "$INSTALL_DIR/nutclient.json"
chmod 600 "$INSTALL_DIR/nutclient.json"

# Copy shutdown script — don't overwrite existing
if [ ! -f "$SCRIPT_DIR/graceful-shutdown.sh" ]; then
    cp "$SOURCE_DIR/scripts/graceful-shutdown.sh" "$SCRIPT_DIR/graceful-shutdown.sh"
    chmod +x "$SCRIPT_DIR/graceful-shutdown.sh"
    echo "Installed example shutdown script to $SCRIPT_DIR/graceful-shutdown.sh"
else
    echo "Keeping existing $SCRIPT_DIR/graceful-shutdown.sh"
fi

# Install systemd service
cp "$SOURCE_DIR/nutclient.service" "$SERVICE_FILE"
systemctl daemon-reload
systemctl enable nutclient

echo ""
echo "Installation complete."
echo ""
echo "  Binary:  $INSTALL_DIR/NutClient"
echo "  Config:  $INSTALL_DIR/nutclient.json"
echo "  Script:  $SCRIPT_DIR/graceful-shutdown.sh"
echo "  Service: nutclient.service"
echo ""

if [ "$NEEDS_CONFIG" = true ]; then
    echo "Edit the config first, then start the service:"
    echo "  sudo nano $INSTALL_DIR/nutclient.json"
    echo "  sudo systemctl start nutclient"
else
    echo "Starting service..."
    systemctl start nutclient
    systemctl status nutclient --no-pager
fi

echo ""
echo "Commands:"
echo "  sudo systemctl status nutclient    # check status"
echo "  sudo systemctl restart nutclient   # restart"
echo "  journalctl -u nutclient -f         # view logs"
echo "  cat /var/log/nutclient-status.json # quick status"
