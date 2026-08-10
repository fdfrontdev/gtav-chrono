using Chrono.Application.Ports;

namespace Chrono.Application;

/// <summary>
/// Crowd reactions (S9): high notoriety + burned → civilians flee in fear;
/// high fame + clean → warm recognition. Reactions throttle to ~8s and notify
/// once per state change (no spam).
/// </summary>
public sealed class CrowdReactionService
{
    private const double ReactionIntervalMs = 8000;
    private const int ScaredNotoriety = 30;
    private const int GreetedFame = 40;
    private const float ReactionRadiusM = 25f;

    private readonly IPlayerContext _player;
    private readonly IWorldProbe _probe;
    private readonly IdentityService _identity;
    private readonly ReputationService _reputation;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private long _lastReactionMs = long.MinValue / 2;
    private bool _fleeNotified, _greetNotified;

    /// <summary>
    /// S22 v3: set true while a story mission/cutscene is active — crowd
    /// reactions freeze (scripted NPCs are the story's, not the mod's).
    /// </summary>
    public bool Standby { get; set; }

    public CrowdReactionService(
        IPlayerContext player,
        IWorldProbe probe,
        IdentityService identity,
        ReputationService reputation,
        INotifier notifier,
        ILogSink log)
    {
        _player = player;
        _probe = probe;
        _identity = identity;
        _reputation = reputation;
        _notifier = notifier;
        _log = log;
    }

    public void Tick(long nowMs)
    {
        // S22 v3 (user UAT: "people run because of me during the story — this
        // should not happen"): the crowd reaction is part of the JUSTICE world
        // simulation — it must freeze during missions/cutscenes too (scripted
        // NPCs belong to the story, not to the mod).
        if (Standby) return;
        if (nowMs - _lastReactionMs < ReactionIntervalMs) return;
        _lastReactionMs = nowMs;
        if (_player.IsInVehicle || _player.IsDead) return;

        int notoriety = _reputation.Notoriety;
        int fame = _reputation.Fame;
        bool burned = _identity.IsBurned;

        // Scared: infamous + recognized + visible
        if (notoriety >= ScaredNotoriety && burned && _player.IsVisible)
        {
            _probe.MakeNearbyCiviliansFlee(_player.Position, ReactionRadiusM);
            if (!_fleeNotified)
            {
                _fleeNotified = true;
                _notifier.Show("People scatter as you approach...");
                _log.Info("Crowd reaction: civilians fleeing (notoriety)");
            }
        }
        else
        {
            _fleeNotified = false;
        }

        // Respected: beloved + clean identity
        if (fame >= GreetedFame && !burned)
        {
            if (!_greetNotified)
            {
                _greetNotified = true;
                _notifier.Show("Citizens nod as you pass — you're known around here");
                _log.Info("Crowd reaction: warm recognition (fame)");
            }
        }
        else
        {
            _greetNotified = false;
        }
    }
}
