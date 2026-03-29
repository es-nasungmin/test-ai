#!/usr/bin/env zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
API_DIR="$ROOT_DIR/CrmApi"
PORT=8080

echo "[backend] checking port $PORT"
if lsof -tiTCP:$PORT -sTCP:LISTEN >/dev/null 2>&1; then
  echo "[backend] port $PORT is in use. stopping existing process"
  lsof -tiTCP:$PORT -sTCP:LISTEN | xargs kill -9
  sleep 1
fi

echo "[backend] starting CrmApi"
cd "$API_DIR"
exec dotnet run
