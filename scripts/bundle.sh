#!/usr/bin/env bash
# Bundle Chrono into a distributable ZIP for public release (S22 v8 r2).
# Usage: bash scripts/bundle.sh [version-tag]
#   e.g.  bash scripts/bundle.sh v1.0.0
# Output: dist/Chrono-<version>.zip
#
# The bundle contains ONLY what a player needs:
#   Chrono/Chrono.dll + all runtime DLLs + x64/x86 SQLite interop
#   Chrono/config.json (fresh defaults from config.example.json)
#   README.txt (install + keys + disclaimer)
# EXCLUDED on purpose (personal data — NEVER ship these):
#   chrono.db, chrono.archive-*.db, chrono.log, profile.json.bak,
#   record.json.bak, status.json.bak, any dev-only files.
set -euo pipefail

VERSION="${1:-v1.0.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/src/Chrono.EntryPoint/bin/Debug/net48"
STAGE="$ROOT/dist/stage"
OUT="$ROOT/dist/Chrono-$VERSION.zip"

echo "== Building (clean) =="
export PATH="$PATH:/c/Program Files/dotnet"
(cd "$ROOT" && dotnet build Chrono.sln --nologo -v q -c Debug)

echo "== Staging $VERSION =="
rm -rf "$STAGE" && mkdir -p "$STAGE/Chrono"
# Runtime DLLs only (all NuGet deps included); NEVER ScriptHookVDotNet3.dll
# (the game loads its own from the game root).
for dll in "$SRC"/*.dll; do
    [ "$(basename "$dll")" = "ScriptHookVDotNet3.dll" ] && continue
    cp "$dll" "$STAGE/Chrono/"
done
# SQLite native interop (64-bit game; ship both for safety)
cp -r "$SRC"/x64 "$STAGE/Chrono/" 2>/dev/null || true
cp -r "$SRC"/x86 "$STAGE/Chrono/" 2>/dev/null || true
# Fresh config from the example (never the dev machine's live config)
cp "$ROOT/scripts/config.example.json" "$STAGE/Chrono/config.json"

# README.txt — install + keys + disclaimer
cat > "$STAGE/Chrono/README.txt" <<'EOF'
CHRONO JUSTICE — GTA V Superpower Justice System
=================================================
A ScriptHookVDotNet (SHVDN) mod: superpowers (dash, time stop, invisibility,
fly, god mode, map teleport) + a full justice system (wanted level, arrests,
courts, prison, manhunts) + a live WEBNET news feed.

REQUIREMENTS
------------
- GTA V (Enhanced or Legacy), latest version
- ScriptHookV:       http://www.dev-c.com/gta/scripthookv/
- ScriptHookVDotNet: https://github.com/scripthookvdotnet/scripthookvdotnet/releases
  (install BOTH into your GTA V game root folder)

INSTALL (2 steps)
-----------------
1. Copy the whole "Chrono" folder into:  <GTA V folder>\scripts\
   (create the "scripts" folder if it doesn't exist)
   Final layout: <GTA V folder>\scripts\Chrono\Chrono.dll
2. Start the game. The mod loads automatically.

KEYS (default — change in scripts\Chrono\config.json)
----------------------------------------------------
- Menu:            Shift+0        (all settings, toggles, WEBNET)
- Dash:            X
- Time Stop:       Z
- Invisibility:    B
- Interact (bail / escape / skip transport): G

GAME NOTE
---------
- A fresh story (≤5 missions passed) resets the criminal record automatically
  (your old one is archived, never deleted).
- Story missions pause the justice system automatically ("MISSION — ON STANDBY").

DISCLAIMER
----------
This mod is free for personal use. It is not affiliated with or endorsed by
Rockstar Games or Take-Two. GTA V modding is subject to Rockstar's modding
policy — use at your own risk. The author is not responsible for any
in-game or account effects.

Made by Firdaus — github.com/fdfrontdev | youtube.com/@firdausbuilds
EOF

echo "== Zipping =="
rm -f "$OUT"
# cygpath: convert MSYS paths to Windows paths for native python
WIN_STAGE="$(cygpath -w "$STAGE")"
WIN_OUT="$(cygpath -w "$OUT")"
WIN_OUT_NOEXT="${WIN_OUT%.zip}"
python - "$WIN_STAGE" "$WIN_OUT_NOEXT" <<'PYEOF'
import shutil, sys
stage, out_noext = sys.argv[1], sys.argv[2]
shutil.make_archive(out_noext, "zip", stage)
print("zipped ->", out_noext + ".zip")
PYEOF
rm -rf "$STAGE"

echo "== Bundle ready: $OUT =="
WIN_OUT="$(cygpath -w "$OUT")"
python - "$WIN_OUT" <<'PYEOF'
import sys, zipfile
z = zipfile.ZipFile(sys.argv[1])
names = z.namelist()
size = sum(i.file_size for i in z.infolist())
print(f"entries: {len(names)}, uncompressed: {size//1024} KB")
for n in names[:8]: print(" ", n)
print("  ...")
for n in names[-3:]: print(" ", n)
PYEOF
