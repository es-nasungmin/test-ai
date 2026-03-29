#!/usr/bin/env zsh
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CLIENT_DIR="$ROOT_DIR/CrmClient"
PORT=5173

echo "[frontend] checking port $PORT"
if lsof -tiTCP:$PORT -sTCP:LISTEN >/dev/null 2>&1; then
  echo "[frontend] port $PORT is in use. stopping existing process"
  lsof -tiTCP:$PORT -sTCP:LISTEN | xargs kill -9
  sleep 1
fi

echo "[frontend] starting CrmClient"
cd "$CLIENT_DIR"
exec npm run dev -- --host
