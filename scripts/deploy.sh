#!/usr/bin/env bash
# Deploy Chrono to the GTA V game folder (dev loop).
# Usage: bash scripts/deploy.sh
set -euo pipefail

GAME_DIR="${1:-D:/games/gtav}"
SRC="src/Chrono.EntryPoint/bin/Debug/net48"
TARGET="$GAME_DIR/scripts/Chrono"

echo "== Building =="
export PATH="$PATH:/c/Program Files/dotnet"
dotnet build Chrono.sln --nologo -v q

echo "== Deploying to $TARGET =="
mkdir -p "$TARGET"
cp "$SRC/Chrono.dll" "$TARGET/"
cp "$SRC/Chrono.Domain.dll" "$TARGET/"
cp "$SRC/Chrono.Application.dll" "$TARGET/"
cp "$SRC/Chrono.Boundary.dll" "$TARGET/"
[ -f "$TARGET/config.json" ] || cp scripts/config.example.json "$TARGET/config.json"

echo "== Done. In-game: SHVDN reload key = Insert (or restart game). F9 = menu. =="
ls -la "$TARGET"
