using System;
using System.Collections.Generic;
using System.Numerics;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Tests;

// ---- Port fakes (DLD §7: never load SHVDN in tests) ----

public sealed class FakeClock : IGameClock
{
    public bool IsPaused { get; private set; }
    public int PauseCount { get; private set; }
    public int ResumeCount { get; private set; }
    public int CurrentGameDay { get; set; } = 100;
    public void Pause() { IsPaused = true; PauseCount++; }
    public void Resume() { IsPaused = false; ResumeCount++; }
}

public sealed class FakeRepository : IEntityRepository
{
    public List<GameEntity> Peds { get; } = new();
    public List<GameEntity> Vehicles { get; } = new();
    public List<GameEntity> Props { get; } = new();

    public IReadOnlyList<GameEntity> GetAllPeds() => Peds;
    public IReadOnlyList<GameEntity> GetAllVehicles() => Vehicles;
    public IReadOnlyList<GameEntity> GetAllProps() => Props;
}

public sealed class FakeFreezer : IEntityFreezer
{
    public HashSet<int> ExistsSet { get; } = new();
    public Dictionary<int, FreezeSnapshot> Frozen { get; } = new();
    public Dictionary<int, FreezeSnapshot> Restored { get; } = new();
    public Dictionary<int, bool> FreezeFlags { get; } = new();

    public FakeFreezer(params int[] handles)
    {
        foreach (var h in handles) ExistsSet.Add(h);
    }

    public bool Exists(GameEntity entity) => ExistsSet.Contains(entity.Handle);

    public FreezeSnapshot Snapshot(GameEntity entity) =>
        new(entity.Handle, entity.Kind, new Vector3(entity.Handle, 0, 0), Vector3.Zero, Vector3.Zero, false);

    public void Freeze(GameEntity entity, FreezeSnapshot snapshot)
    {
        Frozen[entity.Handle] = snapshot;
        FreezeFlags[entity.Handle] = true;
    }

    public void Restore(GameEntity entity, FreezeSnapshot snapshot)
    {
        if (!ExistsSet.Contains(entity.Handle)) return;
        Restored[entity.Handle] = snapshot;
        FreezeFlags[entity.Handle] = false;
    }
}

public sealed class FakePlayer : IPlayerContext
{
    public int PlayerHandle { get; set; } = 1;
    public int? PlayerVehicleHandle { get; set; }
    public Vector3 Position { get; set; } = new(0, 0, 0);
    public bool HasWeapon { get; set; } = false;   // S19
    public float Heading { get; set; } = 0f;
    public bool IsAiming { get; set; }
    public bool IsInVehicle { get; set; }
    public Vector3 AimDirection { get; set; } = Vector3.UnitX;
    public bool WaypointActive { get; set; }
    public Vector3 WaypointPosition { get; set; } = new(100, 100, 10);
    public List<Vector3> TeleportCalls { get; } = new();
    public List<Vector3> VelocityCalls { get; } = new();
    public List<bool> GravityCalls { get; } = new();
    public List<bool> RagdollCalls { get; } = new();
    public List<bool> InvincibleCalls { get; } = new();
    public List<bool> VisibleCalls { get; } = new();
    public int RefillCount { get; private set; }
    public List<float> HeadingCalls { get; } = new();
    public List<string> LoopedAnims { get; } = new();
    public List<string> OneShotAnims { get; } = new();
    public int ClearAnimCount { get; private set; }

    public Vector3 GetAimDirection() => AimDirection;
    public bool IsWaypointActive() => WaypointActive;
    public Vector3 GetWaypointPosition() => WaypointPosition;
    public void Teleport(Vector3 position)
    {
        Position = position;
        TeleportCalls.Add(position);
    }
    public void SetVelocity(Vector3 velocity) => VelocityCalls.Add(velocity);
    public void SetGravityEnabled(bool enabled) => GravityCalls.Add(enabled);
    public void SetRagdollEnabled(bool enabled) => RagdollCalls.Add(enabled);
    public void SetInvincible(bool enabled) => InvincibleCalls.Add(enabled);
    public void RefillHealth() => RefillCount++;
    public void SetVisible(bool visible) => VisibleCalls.Add(visible);
    public void SetHeading(float headingDegrees) => HeadingCalls.Add(headingDegrees);
    public void PlayLoopedAnimation(string dict, string anim) => LoopedAnims.Add($"{dict}/{anim}");
    public void PlayAnimationOnce(string dict, string anim, int durationMs) => OneShotAnims.Add($"{dict}/{anim}/{durationMs}");
    public void ClearCurrentAnimation() => ClearAnimCount++;
    public int PlaceOnGroundCount { get; private set; }
    public void PlaceOnGround() => PlaceOnGroundCount++;
    public List<bool> AwarenessCalls { get; } = new();
    public void SetNpcAwareness(bool enabled) => AwarenessCalls.Add(enabled);
    public List<bool> LawIgnoreCalls { get; } = new();   // S23
    public void SetLawEnforcementIgnore(bool enabled) => LawIgnoreCalls.Add(enabled);
    public bool IsVisible { get; set; } = true;
    public string DistrictName { get; set; } = "Vinewood";
    public string GetDistrictName() => DistrictName;
    public string CharacterName { get; set; } = "Franklin";
    public string GetCharacterName() => CharacterName;
    public int Money { get; set; } = 100000;
    public List<int> MoneyCalls { get; } = new();
    public void AddMoney(int delta) { Money += delta; MoneyCalls.Add(delta); }
    public int GetMoney() => Money;
    public bool IsDead { get; set; }
    public List<bool> ControlCalls { get; } = new();
    public void SetControlEnabled(bool enabled) => ControlCalls.Add(enabled);

    // ── v0.10 survivor-need effects (SRS FR-C4..C7) ──
    public List<float> HealthRechargeCalls { get; } = new();
    public void SetHealthRechargeMultiplier(float multiplier) => HealthRechargeCalls.Add(multiplier);
    public List<float> RunSpeedCalls { get; } = new();
    public void SetRunSpeedMultiplier(float multiplier) => RunSpeedCalls.Add(multiplier);
    public List<bool> DrunkCalls { get; } = new();
    public void SetDrunkVisual(bool enabled) => DrunkCalls.Add(enabled);
    public float DamageTaken { get; private set; }
    public void ApplyHealthDamage(float amount) => DamageTaken += amount;

    // v0.13: fresh-air mood passive (ADR 09) — default FALSE so older tests
    // don't silently gain mood; mood tests opt in explicitly.
    public bool Outdoors { get; set; } = false;
    public bool IsOutdoors() => Outdoors;
}

public sealed class FakeProbe : IWorldProbe
{
    public RaycastSample? RaycastResult { get; set; }
    public float? GroundHeight { get; set; }

    public RaycastSample Raycast(Vector3 origin, Vector3 direction, float maxDistance)
        => RaycastResult ?? new RaycastSample(origin, origin + direction * maxDistance, false, Vector3.Zero);

    public float? GetGroundHeight(Vector3 position) => GroundHeight;
    public int NearbyCivilians { get; set; }
    public float LastCiviliansRadius { get; private set; } = float.MaxValue;   // S22 v6
    public int CountNearbyCivilians(Vector3 position, float radius)
    {
        LastCiviliansRadius = radius;
        return NearbyCivilians;
    }
    public int FleeCalls { get; private set; }
    public void MakeNearbyCiviliansFlee(Vector3 position, float radius) => FleeCalls++;
}

/// <summary>S20 — fake act sampler for CrimeDetectionService tests.</summary>
public sealed class FakeCrimeProbe : ICrimeProbe
{
    public PlayerActContext Context { get; set; } = new(DeathCauseKind.None, false, false, 0f);
    public Queue<DeathCauseKind> Kills { get; } = new();
    public bool PedDamage { get; set; }
    public bool VehicleDamage { get; set; }
    public float CrosshairDistance { get; set; } = float.MaxValue;
    public int PoliceCount { get; set; }
    public float NearestPoliceDistance { get; set; } = float.MaxValue;   // S21
    public List<bool> HoldFireCalls { get; } = new();
    public bool HoldActive => HoldFireCalls.Count > 0 && HoldFireCalls[HoldFireCalls.Count - 1];

    public PlayerActContext SampleContext() => Context;
    public DeathCauseKind PollKillSinceLastPoll() => Kills.Count > 0 ? Kills.Dequeue() : DeathCauseKind.None;
    public bool PollPedDamageSinceLastPoll()
    {
        bool v = PedDamage;
        PedDamage = false;
        return v;
    }
    public bool PollVehicleDamageSinceLastPoll()
    {
        bool v = VehicleDamage;
        VehicleDamage = false;
        return v;
    }
    public float CrosshairPedDistanceM => CrosshairDistance;
    public int CountNearbyPolice(float radius) => PoliceCount;
    public float NearestPoliceDistanceM => NearestPoliceDistance;
    public void SetPoliceHoldFire(bool hold) => HoldFireCalls.Add(hold);
}

public sealed class FakeCutsceneRenderer : ICutsceneRenderer
{
    public int BeginCount { get; private set; }
    public int EndCount { get; private set; }
    public List<string> Banners { get; } = new();
    public List<string> Anims { get; } = new();
    public List<string> Cameras { get; } = new();

    public void Begin() => BeginCount++;
    public void End() => EndCount++;
    public void SetCamera(Vector3 position, Vector3 lookAt, float fov) => Cameras.Add($"{position.X:F0},{position.Y:F0},{position.Z:F0}→{fov:F0}");
    public void ShowBanner(string text) => Banners.Add(text);
    public void PlayAnim(string dict, string anim, bool loop) => Anims.Add($"{dict}/{anim}");
}

public sealed class FakePrisonOutfit : IPrisonOutfit
{
    public int ApplyCount { get; private set; }
    public int RestoreCount { get; private set; }
    public void ApplyPrison() => ApplyCount++;
    public void Restore() => RestoreCount++;
}

public sealed class FakeNotifier : INotifier
{
    public List<string> Messages { get; } = new();
    public void Show(string message) => Messages.Add(message);
}

public sealed class FakeLog : ILogSink
{
    public List<string> Lines { get; } = new();
    public void Debug(string m) => Lines.Add("DEBUG " + m);
    public void Info(string m) => Lines.Add("INFO " + m);
    public void Warn(string m) => Lines.Add("WARN " + m);
    public void Error(string m) => Lines.Add("ERROR " + m);
}

public sealed class FakeRenderer : IMenuRenderer
{
    public int RenderCount { get; private set; }
    public List<string> Hints { get; } = new();
    public void Render(MenuScreen screen) => RenderCount++;
    public void DrawHint(string text) => Hints.Add(text);
}

public sealed class FakeInput : IGameInput
{
    public bool MenuKeyPressed { get; set; }
    public bool MenuUp { get; set; }
    public bool MenuDown { get; set; }
    public bool MenuAccept { get; set; }
    public bool MenuCancel { get; set; }
    public bool DashHotkey { get; set; }

    // Simulated edge detection: setting MenuKeyPressed to true produces ONE just-pressed edge
    // (subsequent true stays held); set to false then true again for another press.
    private bool _wasPressed;

    public void Update()
    {
        IsMenuKeyJustPressed = MenuKeyPressed && !_wasPressed;
        _wasPressed = MenuKeyPressed;
        UpdateHotkeys();
    }

    public bool IsMenuKeyJustPressed { get; private set; }
    public bool IsMenuKeyPressed => MenuKeyPressed;
    public bool IsMenuUpJustPressed => MenuUp;
    public bool IsMenuDownJustPressed => MenuDown;
    public bool IsMenuAcceptJustPressed => MenuAccept;
    public bool IsMenuCancelJustPressed => MenuCancel;
    public bool IsDashHotkeyPressed => DashHotkey;
    public bool IsDashKeyJustPressed { get; set; }   // S13: escape-plan picker (X)

    // --- hotkey edges (Z = time stop, B = invisible, G = interact) ---
    public bool TimeStopHotkey { get; set; }
    public bool InvisibleHotkey { get; set; }
    public bool InteractHotkey { get; set; }
    private bool _tsWasPressed, _invWasPressed, _interactWasPressed;

    // v0.10: combat hotkeys (N/K/V/U)
    public bool PushHotkey { get; set; }
    public bool BlastHotkey { get; set; }
    public bool BulletTimeHotkey { get; set; }
    public bool RegenHotkey { get; set; }
    private bool _pushWasPressed, _blastWasPressed, _btWasPressed, _regenWasPressed;

    public void UpdateHotkeys()
    {
        IsTimeStopHotkeyJustPressed = TimeStopHotkey && !_tsWasPressed;
        _tsWasPressed = TimeStopHotkey;
        IsInvisibleHotkeyJustPressed = InvisibleHotkey && !_invWasPressed;
        _invWasPressed = InvisibleHotkey;
        IsInteractKeyJustPressed = InteractHotkey && !_interactWasPressed;
        _interactWasPressed = InteractHotkey;
        IsPushHotkeyJustPressed = PushHotkey && !_pushWasPressed;
        _pushWasPressed = PushHotkey;
        IsBlastHotkeyJustPressed = BlastHotkey && !_blastWasPressed;
        _blastWasPressed = BlastHotkey;
        IsBulletTimeHotkeyJustPressed = BulletTimeHotkey && !_btWasPressed;
        _btWasPressed = BulletTimeHotkey;
        IsRegenHotkeyJustPressed = RegenHotkey && !_regenWasPressed;
        _regenWasPressed = RegenHotkey;
    }

    public bool IsTimeStopHotkeyJustPressed { get; private set; }
    public bool IsInvisibleHotkeyJustPressed { get; private set; }
    public bool IsInteractKeyJustPressed { get; private set; }
    public bool IsPushHotkeyJustPressed { get; private set; }
    public bool IsBlastHotkeyJustPressed { get; private set; }
    public bool IsBulletTimeHotkeyJustPressed { get; private set; }
    public bool IsRegenHotkeyJustPressed { get; private set; }

    // --- flight controls ---
    public bool FlyForward { get; set; }
    public bool FlyBack { get; set; }
    public bool FlyLeft { get; set; }
    public bool FlyRight { get; set; }
    public bool FlyAscend { get; set; }
    public bool FlyDescend { get; set; }

    public bool IsFlyForward => FlyForward;
    public bool IsFlyBack => FlyBack;
    public bool IsFlyLeft => FlyLeft;
    public bool IsFlyRight => FlyRight;
    public bool IsFlyAscend => FlyAscend;
    public bool IsFlyDescend => FlyDescend;
}

public sealed class FakeConfigStore : IConfigStore
{
    public ChronoConfig Config { get; set; } = new();
    public int SaveCount { get; private set; }
    public ChronoConfig Load() => Config;
    public void Save(ChronoConfig config) { Config = config; SaveCount++; }
}

public sealed class FakeVfx : IVfxBoundary
{
    public List<string> Calls { get; } = new();
    public void Tick() => Calls.Add("tick");
    public void SetTimecycleModifier(string name, float strength) => Calls.Add($"timecycle:{name}:{strength}");
    public void ClearTimecycleModifier() => Calls.Add("timecycle:clear");
    public void SpawnParticle(string asset, string effect, System.Numerics.Vector3 pos, float scale) => Calls.Add($"particle:{effect}");
    public void ShakeCamera(float amplitude) => Calls.Add("shake");
    public void StopCameraShake() => Calls.Add("shake:stop");
    public void ScreenFlash(int fadeInMs) => Calls.Add($"flash:{fadeInMs}");
    public void ScreenFadeOut(int fadeOutMs) => Calls.Add($"fadeout:{fadeOutMs}");
    public void ScreenFadeIn(int fadeInMs) => Calls.Add($"fadein:{fadeInMs}");
    public void FlashColor(int r, int g, int b, int alpha, int frames) => Calls.Add($"flashcolor:{r},{g},{b},{alpha},{frames}");
    public void SetPlayerAlpha(int alpha) => Calls.Add($"alpha:{alpha}");
    public void ResetPlayerAlpha() => Calls.Add("alpha:reset");
    public void DrawMarker(System.Numerics.Vector3 pos, float scale, int r, int g, int b, int a) => Calls.Add($"marker:{r},{g},{b}");
}
