using System;
using System.Diagnostics;
using System.Linq;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// S22 v8 r4 (user UAT: "when I use superpower, the citizen didn't surprise,
/// nothing on webnet. webnet also need to be alive and active") — the WORLD
/// reacts to superpowers: nearby civilians surprise+flee, WEBNET posts the
/// sighting (with timestamps + priority tiers via the shared feed), and a
/// visible public use with witnesses earns small notoriety (the public-image
/// system sees you).
///
/// Rules (design ruling: FULL scope):
/// - WITNESS-GATED: no civilians nearby → no reaction, no post, no notoriety.
///   Nobody saw it, it didn't happen.
/// - INVISIBLE = clean: while invisible the player cannot be witnessed.
/// - Throttled ~30s PER POWER TYPE (feed v5 lesson: separate cadence, no spam).
/// - Time Stop ON is frozen by definition (nobody can react mid-freeze) — the
///   reaction fires on DEACTIVATE (the world wakes up baffled).
/// - Invisible OFF (reveal) near witnesses = confusion (a man appeared).
/// </summary>
public sealed class PowerReactionService
{
    public enum PowerKind { TimeStop, Dash, InvisibleOn, InvisibleOff, Fly, GodMode, MapTeleport }

    private const float WitnessRadiusM = 20f;
    private const float FleeRadiusM = 12f;

    private readonly IPlayerContext _player;
    private readonly IWorldProbe _probe;
    private readonly ReputationService _reputation;
    private readonly MediaService _media;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly Func<bool> _isInvisible;
    private readonly Func<double> _random;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    // Negative init so the FIRST use of each kind always passes the throttle
    // (same trick as MediaService._lastNewsMs — a 0 start throttles the first
    // call because now - 0 < cooldown for the first 30s).
    private long[] _lastUseMs = Enumerable.Repeat(-30000L, Enum.GetValues(typeof(PowerKind)).Length).ToArray();

    public PowerReactionService(
        IPlayerContext player,
        IWorldProbe probe,
        ReputationService reputation,
        MediaService media,
        INotifier notifier,
        ILogSink log,
        Func<bool> isInvisible,
        Func<double>? random = null)
    {
        _player = player;
        _probe = probe;
        _reputation = reputation;
        _media = media;
        _notifier = notifier;
        _log = log;
        _isInvisible = isInvisible;
        _random = random ?? (() => new Random().NextDouble());
    }

    /// <summary>
    /// Report a power use. Witness-gated: needs civilians in radius AND the
    /// player visible (not invisible, not in a vehicle, not dead).
    /// </summary>
    public void Report(PowerKind kind)
    {
        // Invisible = cannot be witnessed (except the REVEAL itself).
        if (kind != PowerKind.InvisibleOff && _isInvisible()) return;
        if (_player.IsInVehicle || _player.IsDead) return;

        int idx = (int)kind;
        long now = _clock.ElapsedMilliseconds;
        if (now - _lastUseMs[idx] < 30000) return;   // per-type throttle (S22 v8 r4)
        _lastUseMs[idx] = now;

        int witnesses = _probe.CountNearbyCivilians(_player.Position, WitnessRadiusM);
        if (witnesses <= 0) return;   // nobody saw it — it didn't happen

        // Crowd surprise: nearby civilians flee the impossible sight.
        _probe.MakeNearbyCiviliansFlee(_player.Position, FleeRadiusM);
        _notifier.Show(ReactionLine(kind));

        // WEBNET: the sighting goes live (viral for the showy powers).
        _media.News(WebnetLine(kind, witnesses));

        // Public image: a visible, witnessed superpower earns small notoriety.
        _reputation.OnPublicPowerUse();

        _log.Info($"Power reaction: {kind} witnessed by {witnesses} civilians");
    }

    private string ReactionLine(PowerKind kind) => kind switch
    {
        PowerKind.TimeStop     => "The world wakes up baffled — what just happened?",
        PowerKind.Dash         => "People scream as you blink across the street!",
        PowerKind.InvisibleOn  => "Eyes go wide — did that man just fade out?",
        PowerKind.InvisibleOff => "Gasps — a man just appeared out of thin air!",
        PowerKind.Fly          => "Civilians stare — is that man FLYING?",
        PowerKind.GodMode      => "Onlookers back away from the unkillable one",
        PowerKind.MapTeleport  => "Wait — he was HERE a second ago...",
        _                      => "People can't believe what they just saw",
    };

    private string WebnetLine(PowerKind kind, int witnesses) => kind switch
    {
        PowerKind.TimeStop     => $"WEBNET: witnesses baffled by a frozen moment in {_player.GetDistrictName()}",
        PowerKind.Dash         => $"VIDEO: impossible dash caught on dash-cam in {_player.GetDistrictName()}",
        PowerKind.InvisibleOn  => $"WEBNET: man vanishes mid-conversation in {_player.GetDistrictName()}",
        PowerKind.InvisibleOff => $"WEBNET: man appears out of thin air in {_player.GetDistrictName()}",
        PowerKind.Fly          => $"VIDEO: man flying over {_player.GetDistrictName()} — drone footage",
        PowerKind.GodMode      => $"WEBNET: suspect shrugs off gunfire in {_player.GetDistrictName()}",
        PowerKind.MapTeleport  => $"WEBNET: man appears across town in seconds — {_player.GetDistrictName()}",
        _                      => $"WEBNET: strange sighting reported in {_player.GetDistrictName()}",
    };
}
