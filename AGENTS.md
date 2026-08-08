# AGENTS.md — Chrono (GTA V Anime Superpower Mod)

## GitNexus
```gitnexus
analysis:
  root: .
  exclude: ["bin", "obj", "lib", "TestResults"]
```
Run `gitnexus analyze .` after meaningful code changes to refresh the index.

## Project Context
- **What:** Single-player GTA V (Enhanced edition) script mod — anime superpowers via F9 menu:
  Time Stop (freeze world + clock, player free), Dash Teleport (aim blink, wall-safe),
  Map Teleport (waypoint warp). Built to learn GTA V modding the Firdaus way.
- **Game:** `D:\games\gtav` — Enhanced, build 3788 (exe 1.0.1013.34), RUNE crack (SP only).
- **Stack:** C# / .NET Framework 4.8, ScriptHookVDotNet Enhanced v1.1.0.6 (v3 API),
  xUnit. Built with `dotnet build` (SDK 10 + ReferenceAssemblies) — no Visual Studio.
- **Runtime chain:** dsound.dll → OpenRPF.asi → ScriptHookV.dll (build-locked) →
  ScriptHookVDotNet.asi → `scripts/Chrono/Chrono.dll`. **ScriptHookV must match the
  game build — never update one without the other.**

## Architecture (Firdaus Engineering Standards — 4-layer DDD)
```
src/
├── Chrono.Domain       PURE — config model+validator, TeleportMath, FreezePolicy, snapshots
├── Chrono.Application  use cases + Ports (interfaces: IGameClock, IEntityRepository, IWorldProbe,
│                       IPlayerContext, INotifier, IGameInput) + VfxService orchestration
├── Chrono.Boundary     SHVDN adapters implementing Ports (the only project touching the game)
├── Chrono.UI           MenuFramework (custom native-style) + PowerMenuScreen/SettingsScreen
├── Chrono.EntryPoint   ChronoScript : Script (composition root, OnTick/KeyDown)
tests/
└── Chrono.Tests        xUnit — Domain + Application with fakes (NEVER load SHVDN in tests)
```
- Dependencies point INWARD: Domain ← Application ← {UI, Boundary} ← EntryPoint.
- **Ports live in Application** (hexagonal) — Boundary adapts, UI consumes.
- Everything game-touching is IMPURE and lives in Boundary; Domain is 100% game-free.

## Golden Rules for This Repo
1. Read `CLAUDE.md` before writing ANY code. It is the full standard.
2. ScriptHookVDotNet3.dll is gitignored — reference via `lib/` (see README).
3. Game APIs are main-thread only — no background threads in Boundary/EntryPoint.
4. Tick budget < 2 ms; entity batching (≤100/tick) for freeze/restore sweeps.
5. VFX is never a blocker — powers must work with `visual.*` all off.
6. Config is JSON, validated by `ConfigValidator` — fail-soft to defaults.
7. No secrets, no absolute paths in code (game path only in deploy script).
8. Commit early, version always: conventional commits (`feat:`, `fix:`, `test:`, `docs:`),
   feature branches, never push to main without review.

## Vault (canonical docs — READ before coding, WRITE after events)
`C:\obsidian\openclaw\1 Projects\Personal\GTA V Modding\`
- `1 - Requirements/` VA + SRS · `2 - Architecture & Design/` HLD, DLD, UIUX, Animation & VFX
- `6 - Decision Logs/` ADRs · `8 - Operations/` runbook (build-lock matrix) · `9 - Issue Log/`

## MCP Selection
- `sb-query` — query/write Second Brain lessons (setup gotchas, native APIs)
- `obsidian-vault` — vault notes (project docs)
- `agentic-memory` — episode recall/record for this project
- No other MCP servers needed for this repo.

## UAT Discipline
Every slice ends with an in-game UAT (scenario → steps → expected → pass/fail) recorded in
the vault `8 - Operations/` before the slice is closed. Hermes reviews; Firdaus approves.
