# Chrono — Anime Superpower Mod for GTA V

Single-player GTA V **Enhanced** script mod: **Time Stop**, **Dash Teleport**, **Map Teleport**.
Built with C# + ScriptHookVDotNet (Enhanced fork) under **Firdaus Engineering Standards**.

## Requirements
| Component | Version |
|-----------|---------|
| GTA V Enhanced | build 3788 (exe 1.0.1013.34) |
| ScriptHookV | 3788.0/1013.34 (must match game build!) |
| ScriptHookVDotNet Enhanced | ≥ 1.1.0.6 |
| .NET SDK | 10.0.x (builds net48 via reference assemblies) |
| .NET Framework 4.8 runtime | in-game requirement (SHVDN) |
| d3dx11_43.dll | DirectX End-User Runtime (SHV UI requirement) |

## Build
```bash
dotnet build Chrono.sln
```
Output: `src/Chrono.EntryPoint/bin/Debug/net48/Chrono.dll` → copy to `D:\games\gtav\scripts\Chrono\`.

## Deploy (dev loop)
```powershell
# scripts/deploy.ps1 (adjust game path)
Copy-Item src/Chrono.EntryPoint/bin/Debug/net48/Chrono.dll "D:\games\gtav\scripts\Chrono\Chrono.dll" -Force
```
Then in-game: SHVDN reload key `Insert` to hot-reload scripts.

## lib/ note
`ScriptHookVDotNet3.dll` is gitignored — copy it from the game folder:
`copy D:\games\gtav\ScriptHookVDotNet3.dll lib\`

## Docs
All project docs live in the Obsidian vault: `1 Projects/Personal/GTA V Modding/`
(SRS, HLD, DLD, UIUX, Animation & VFX, ADRs).
