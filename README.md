# CHRONO · FIRDAUS BUILDS

**GTA V Superpower + Justice System** — dash, time stop, invisibility, fly, god mode, map teleport, plus a complete justice system: wanted levels, physical arrests, police escorts, court rulings, prison sentences, and city-wide manhunts.

[![Release](https://img.shields.io/badge/release-v1.0.0-blue)](https://github.com/fdfrontdev/gtav-chrono/releases) · [![Platform](https://img.shields.io/badge/GTA%20V-Enhanced%20%7C%20Legacy-green)]() · [![License](https://img.shields.io/badge/license-free%20for%20personal%20use-orange)]()

---

## ⚡ Features

- **Superpowers** — Dash (X), Time Stop (Z), Invisibility (B), Fly, God Mode, Map Teleport — all toggleable in the CHRONO menu (Shift+0)
- **Justice system** — commit crimes and the world reacts: wanted stars, physical capture (you get cuffed), police escort to Bolingbroke Penitentiary, court rulings, prison sentences
- **Manhunts** — prison breaks trigger city-wide manhunts; stay ahead of the heat
- **Live WEBNET feed** — a news ticker reacts to your actions in real time; civilians recognize you and call the police
- **Reputation** — notoriety, fame, warrants, and identities persist between sessions (SQLite database)
- **Witness reactions** — use superpowers in public and civilians flee — or post about it on WEBNET
- **Mission-safe** — justice pauses during story missions automatically
- **New-game safe** — a fresh story archives your old record, never deletes it

## 📦 Download

| File | What it is |
|------|-----------|
| [**Chrono-Setup-1.0.0.exe**](https://github.com/fdfrontdev/gtav-chrono/releases/download/v1.0.0/Chrono-Setup-1.0.0.exe) | **One-click installer** — auto-detects your GTA V folder, installs everything including ScriptHookV/ScriptHookVDotNet if missing |
| [**Chrono-v1.0.0.zip**](https://github.com/fdfrontdev/gtav-chrono/releases/download/v1.0.0/Chrono-v1.0.0.zip) | Manual install — extract and copy the `Chrono` folder into `<GTA V folder>\scripts\` |

## 🛠️ Requirements

- GTA V (Enhanced or Legacy)
- ScriptHookV + ScriptHookVDotNet 3 — **bundled in the one-click installer**

## 🎮 Default Keys

| Action | Key |
|--------|-----|
| CHRONO menu | Shift+0 |
| Dash | X |
| Time Stop | Z |
| Invisibility | B |
| Interact (bail / skip transport) | G |

Rebind everything in `scripts\Chrono\config.json`.

## ❤️ Support the Build

CHRONO is and always will be **free**. If it makes your game better:

- ☕ [Buy me a coffee](https://buymeacoffee.com/) — a one-time thank you
- 💳 [Join the Patreon](https://patreon.com/FirdausBuilds) — credits, feature votes, dev-logs ($3 / $7 / $15)
- 📺 [Subscribe on YouTube](https://www.youtube.com/@firdausbuilds) — dev-logs and tutorials

Every supporter funds a solo builder doing this full-time.

---

## 🔧 For Developers

### Build
```bash
dotnet build Chrono.sln
```
Output: `src/Chrono.EntryPoint/bin/Debug/net48/Chrono.dll` → copy to `<GTA V>\scripts\Chrono\`.

### Release pipeline
```bash
bash scripts/release.sh v1.0.0   # obfuscated zip + one-click installer
```

### Architecture
4-layer DDD (Domain / Application / Boundary / EntryPoint) under Firdaus Engineering Standards.
455 unit tests · 0 warnings. SQLite for the criminal record, hexagonal ports for GTA interop.

## 📜 Disclaimer

Free for personal use. Not affiliated with or endorsed by Rockstar Games or Take-Two. GTA V modding is subject to Rockstar's modding policy — use at your own risk.

**Made by Firdaus — FIRDAUS BUILDS** · [YouTube](https://www.youtube.com/@firdausbuilds) · [Patreon](https://patreon.com/FirdausBuilds)
