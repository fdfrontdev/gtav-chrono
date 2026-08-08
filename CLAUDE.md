# CLAUDE.md — Chrono (GTA V Anime Superpower Mod)

**Firdaus Engineering Standards v5 — C# / ScriptHookVDotNet variant.**
This is the full standard for this repo. Read it completely before writing or reviewing code.

---

## 1. Provider-Agnostic LLM Rules (all 15 — non-negotiable)

| # | Rule |
|---|------|
| 1 | Never write code without reading AGENTS.md + CLAUDE.md first |
| 2 | Follow the Golden Sequence: Interface/Contract → Implementation → Wiring (DI) |
| 3 | 4-Layer DDD: Domain (pure) ← Application ← Boundary/UI ← EntryPoint; dependencies point inward |
| 4 | SRP: one class, one reason to change; a function does one thing |
| 5 | Contract First: define interfaces/ports before implementations |
| 6 | Vertical slices: one feature end-to-end per iteration, not horizontal layers |
| 7 | Dependency Injection: compose at the root; no service locators, no new-ing services inside services |
| 8 | TDD: tests before implementation (RED → GREEN → REFACTOR) |
| 9 | Error Handling: classify Bug vs Operational; fail-soft with typed results |
| 10 | Verb+Noun naming: `SpawnVehicle()`, `CalculateDashTarget()`, not `Process()` |
| 11 | Small functions: < 20 lines; extract nesting; no boolean params as API |
| 12 | Pure vs Impure: business logic pure & deterministic; impurity at the boundary, injected |
| 13 | Open/Closed: extend via new classes/interfaces, not by modifying working code |
| 14 | No magic numbers: named constants or config; config values validated |
| 15 | Never deploy code without tests passing + UAT evidence recorded (vault) |

## 2. Golden Sequence (every feature)

```
1. Write the CONTRACT (interface / record / signature)      ← what it must do
2. Write the TESTS for the contract (xUnit, fakes)           ← how it's proven
3. Implement (small functions, pure where possible)          ← how it's done
4. Wire via DI at the composition root (EntryPoint)          ← where it lives
5. In-game UAT → vault evidence → close slice
```
Never reverse this order. Contract changes = contract tests change first.

## 3. Layer Architecture (this repo)

```
Chrono.Domain      — PURE. Config model + ConfigValidator, TeleportMath (vector math),
                     FreezePolicy, snapshot records. Zero game references. 100% unit-tested.
Chrono.Application — Use cases (TimeStopService, TeleportService, VfxService, PowerMenuService)
                     + Ports (IGameClock, IEntityRepository, IPlayerContext, IWorldProbe,
                     INotifier, IGameInput). Orchestrates; holds state machines.
Chrono.Boundary    — SHVDN adapters ONLY (GameClockAdapter, EntityRepository, WorldProbe,
                     PlayerContext, Notifier, GameInput, EntityFreeze, VfxBoundary).
                     The ONLY project that references ScriptHookVDotNet3.dll.
Chrono.UI          — MenuFramework (generic, reusable) + PowerMenuScreen + SettingsScreen.
Chrono.EntryPoint  — ChronoScript : Script — composition root; OnTick pipeline; KeyDown.
Chrono.Tests       — xUnit net48. References Domain + Application ONLY (never SHVDN).
```
Rules:
- Domain must compile in isolation (prove it: `dotnet build src/Chrono.Domain` alone).
- Ports (interfaces) belong to their CONSUMERS → defined in Application.
- Boundary classes are thin: no business rules, only translation to/from game APIs.
- UI never calls the game directly — only Application services through ports.

## 4. Game-Boundary Guardrails (SHVDN-specific)

1. **Main thread only** — game natives can only be called on the game thread (SHVDN Tick/
   KeyDown callbacks). Never spawn `Task.Run`/`Thread` around game calls.
2. **Tick budget** — OnTick must return < 2 ms average. Batch entity work (≤100/tick).
3. **Entity lifecycle** — entity handles can die between frames (ped removed, car exploded).
   Every restore/use checks `Exists()` first; dead entities → skip + WARN (never throw).
4. **Build-lock** — SHV/SHVDN/game versions are coupled. See vault runbook matrix.
5. **VFX non-blocking** — particle/timecycle failures → WARN + continue; powers never depend
   on cosmetics. All VFX gated by `visual.*` config.
6. **Reload-safe** — must survive SHVDN `Insert` reload: release nothing global, re-read config.
7. **Memory** — no static caches of Entity objects across ticks; hold handles (ints) only.

## 5. Error Handling (Bug vs Operational)

| Kind | Example | Handling |
|------|---------|----------|
| Bug | Null ref, contract violation | `ERROR` log + single notification `Chrono error — see chrono.log`; never crash the game |
| Operational | Invalid config, no waypoint, blocked dash, dead entity | Typed result (`TeleportResult.NoWaypoint`), notification, no exception, no log spam |
| Boundary | Native call failure | Catch in adapter, wrap in typed exception, log once, return safe default |

- `ChronoLogger` (Application): timestamps, levels DEBUG/INFO/WARN/ERROR, throttled (≤5 WARN/s).
- Root `try/catch` in `ChronoScript.OnTick` — catches everything, logs, continues next tick.

## 6. Testing (xUnit)

- **Pure logic** (Domain): `ConfigValidatorTests`, `TeleportMathTests`, `FreezePolicyTests`.
- **Use cases** (Application): fake ports (`FakeClock`, `FakeRepository`, `FakeProbe`) —
  `TimeStopServiceTests` (batching, restore order, dead-entity skip, maintenance, cap),
  `TeleportServiceTests` (aim/forward, wall refusal, no-waypoint, vehicle).
- **Never** load ScriptHookVDotNet in tests. Boundary = no unit tests (in-game UAT only).
- Run: `dotnet test tests/Chrono.Tests` — must be green before any commit.
- Test naming: `Method_Scenario_ExpectedResult` (e.g. `CalculateForwardTarget_Heading90_ReturnsEast`).

## 7. C# Conventions

- LangVersion latest; nullable enabled; file-scoped namespaces; `var` where type is obvious.
- Records for value data (`FreezeSnapshot`, `TeleportResult`); sealed classes by default.
- Properties for config; methods for behavior. No public fields.
- Exceptions: typed (e.g. `ConfigValidationException`), never `throw new Exception()`.
- Log messages: `Level: Class.Method — message {context}`.

## 8. Git Standards

- Conventional commits: `feat:`, `fix:`, `test:`, `docs:`, `refactor:`, `chore:`.
- Feature branches: `feat/slice-3-time-stop`; PR/review before merge to `main`.
- **Never push to main directly.** Commit early, version always — commit after each milestone
  BEFORE risky operations (resets, rebases).
- One commit = one logical change; message explains WHY (not what).
- Changelog notes in commit body for UAT-visible changes.

## 9. Vault Documentation Workflow

- READ: vault docs before coding a slice (`1 Projects/Personal/GTA V Modding/`).
- WRITE: after each slice UAT → evidence note in `8 - Operations/`; ADR for new decisions;
  lesson learned → `9 - Issue Log/` + Second Brain (`sb_add`).
- If it's not in the vault, it didn't happen.

## 10. Definition of Done (every slice)

- [ ] Contract written first (ports/records)
- [ ] Unit tests green (`dotnet test`)
- [ ] Build clean: `dotnet build` 0 warnings 0 errors
- [ ] In-game UAT executed with evidence (screenshot/log → vault)
- [ ] AGENTS/CLAUDE still accurate (update if architecture changed)
- [ ] Committed with conventional message on a feature branch
- [ ] No secrets, no magic numbers, no TODOs left behind
