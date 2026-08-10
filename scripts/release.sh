#!/usr/bin/env bash
# Build the full release: bundle.zip + one-click installer (S22 v8 r2).
# Usage: bash scripts/release.sh [version]
#   e.g.  bash scripts/release.sh v1.0.0
# Outputs:
#   dist/Chrono-<version>.zip         — manual install (any platform)
#   dist/Chrono-Setup-<version>.exe   — one-click installer (auto deps)
set -euo pipefail

VERSION="${1:-v1.0.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ISCC="$LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe"

echo "== 1/3 Bundle (obfuscated ZIP) =="
bash "$ROOT/scripts/bundle.sh" "$VERSION"

echo "== 2/3 One-click installer =="
# Deploy deps are bundled INTO the installer (auto-install when missing)
mkdir -p "$ROOT/dist/deps"
for f in ScriptHookV.dll ScriptHookVDotNet.asi ScriptHookVDotNet.ini \
         ScriptHookVDotNet2.dll ScriptHookVDotNet3.dll; do
    [ -f "$ROOT/dist/deps/$f" ] || cp "D:/games/gtav/$f" "$ROOT/dist/deps/" 2>/dev/null || true
done
"$ISCC" "$ROOT/scripts/ChronoInstaller.iss" 2>&1 | grep -E "Successful|Error" | tail -1

echo "== 3/3 Done =="
ls -la "$ROOT"/dist/Chrono-"$VERSION".zip "$ROOT"/dist/Chrono-Setup-*.exe 2>/dev/null
