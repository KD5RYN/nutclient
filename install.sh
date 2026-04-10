#!/bin/bash
# NutClient Install Script for Linux
# Run as root: sudo ./install.sh
#
# Installs NutClient to /opt/nutclient and sets up a systemd service.
# If nutclient.json already exists at the destination, it will NOT be overwritten.

set -e

INSTALL_DIR="/opt/nutclient"
SCRIPT_DIR="$INSTALL_DIR/scripts"
SERVICE_FILE="/etc/systemd/system/nutclient.service"
SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"

if [ "$EUID" -ne 0 ]; then
    echo "Please run as root: sudo ./install.sh"
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
