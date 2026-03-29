#!/usr/bin/env zsh
set -euo pipefail

echo "[stop] stopping backend (8080)"
if lsof -tiTCP:8080 -sTCP:LISTEN >/dev/null 2>&1; then
  lsof -tiTCP:8080 -sTCP:LISTEN | xargs kill -9
fi

echo "[stop] stopping frontend (5173)"
if lsof -tiTCP:5173 -sTCP:LISTEN >/dev/null 2>&1; then
  lsof -tiTCP:5173 -sTCP:LISTEN | xargs kill -9
fi

echo "[stop] done"
