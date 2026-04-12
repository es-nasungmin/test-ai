#!/usr/bin/env zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"

"$ROOT_DIR/scripts/run-backend.sh" > /tmp/aideskapi.log 2>&1 &
BACKEND_PID=$!

echo "[all] backend started in background (pid: $BACKEND_PID)"

"$ROOT_DIR/scripts/run-frontend.sh" > /tmp/aideskclient.log 2>&1 &
FRONTEND_PID=$!

echo "[all] frontend started in background (pid: $FRONTEND_PID)"

echo "[all] backend log: /tmp/aideskapi.log"
echo "[all] frontend log: /tmp/aideskclient.log"
echo "[all] open: http://localhost:5173"
