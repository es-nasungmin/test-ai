#!/usr/bin/env zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
BACKEND_HEALTH_URL="http://localhost:8080/api/knowledgebase/platforms"
MAX_WAIT_SECONDS=60

"$ROOT_DIR/scripts/run-backend.sh" > /tmp/aideskapi.log 2>&1 &
BACKEND_PID=$!

echo "[all] backend started in background (pid: $BACKEND_PID)"

echo "[all] waiting for backend to become ready..."
elapsed=0
while ! curl -sSf "$BACKEND_HEALTH_URL" >/dev/null 2>&1; do
	if ! kill -0 "$BACKEND_PID" 2>/dev/null; then
		echo "[all] backend process exited before becoming ready"
		echo "[all] check backend log: /tmp/aideskapi.log"
		exit 1
	fi

	if [ "$elapsed" -ge "$MAX_WAIT_SECONDS" ]; then
		echo "[all] backend readiness timeout after ${MAX_WAIT_SECONDS}s"
		echo "[all] check backend log: /tmp/aideskapi.log"
		exit 1
	fi

	sleep 1
	elapsed=$((elapsed + 1))
done

echo "[all] backend is ready"

"$ROOT_DIR/scripts/run-frontend.sh" > /tmp/aideskclient.log 2>&1 &
FRONTEND_PID=$!

echo "[all] frontend started in background (pid: $FRONTEND_PID)"

echo "[all] backend log: /tmp/aideskapi.log"
echo "[all] frontend log: /tmp/aideskclient.log"
echo "[all] open: http://localhost:5173"
