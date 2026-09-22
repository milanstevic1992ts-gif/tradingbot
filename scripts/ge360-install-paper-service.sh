#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVICE_NAME="ge360-trading-paper"
UNIT_PATH="/etc/systemd/system/${SERVICE_NAME}.service"
ENV_DIR="/etc/ge360"
ENV_PATH="${ENV_DIR}/trading-paper.env"

if [ "${EUID}" -ne 0 ]; then
  SERVICE_USER="${GE360_SERVICE_USER:-$(id -un)}"
  exec sudo GE360_SERVICE_USER="$SERVICE_USER" bash "$0" "$@"
fi

SERVICE_USER="${GE360_SERVICE_USER:-${SUDO_USER:-root}}"

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  echo "GE360 install failed: service user '$SERVICE_USER' does not exist." >&2
  exit 2
fi

install -d -m 0750 "$ENV_DIR"
install -d -m 0750 -o "$SERVICE_USER" -g "$SERVICE_USER" "$ROOT/ge360-state"

if [ ! -f "$ENV_PATH" ]; then
  cat >"$ENV_PATH" <<EOF
# GE360 forward-paper runtime secrets.
# Keep this file root-owned and never commit its contents.
APCA_API_KEY_ID=
APCA_API_SECRET_KEY=
GE360_FORWARD_PAPER_STORE=$ROOT/ge360-state/forward-paper.json
EOF
fi

chown root:root "$ENV_PATH"
chmod 0600 "$ENV_PATH"

cat >"$UNIT_PATH" <<EOF
[Unit]
Description=GE360 Trading LEAN Forward Paper
Wants=network-online.target
After=network-online.target
ConditionPathExists=$ROOT/scripts/ge360-live-paper-alpaca.sh

[Service]
Type=simple
User=$SERVICE_USER
WorkingDirectory=$ROOT
EnvironmentFile=$ENV_PATH
ExecStartPre=/usr/bin/test -n "\${APCA_API_KEY_ID}"
ExecStartPre=/usr/bin/test -n "\${APCA_API_SECRET_KEY}"
ExecStart=/usr/bin/env bash $ROOT/scripts/ge360-live-paper-alpaca.sh
Restart=on-failure
RestartSec=20
TimeoutStopSec=45
KillSignal=SIGINT
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=false
ReadWritePaths=$ROOT

[Install]
WantedBy=multi-user.target
EOF

chmod 0644 "$UNIT_PATH"
systemctl daemon-reload
systemctl enable "$SERVICE_NAME.service" >/dev/null

api_key="$(sed -n 's/^APCA_API_KEY_ID=//p' "$ENV_PATH" | tail -n1)"
api_secret="$(sed -n 's/^APCA_API_SECRET_KEY=//p' "$ENV_PATH" | tail -n1)"

echo "GE360 paper service installed."
echo "  service: $SERVICE_NAME"
echo "  user: $SERVICE_USER"
echo "  env: $ENV_PATH"
echo "  repo: $ROOT"

if [ -n "$api_key" ] && [ -n "$api_secret" ]; then
  systemctl restart "$SERVICE_NAME.service"
  echo "GE360 paper service started."
else
  echo "Credentials are empty; service intentionally not started."
  echo "Edit $ENV_PATH, then run:"
  echo "  sudo systemctl start $SERVICE_NAME"
fi
