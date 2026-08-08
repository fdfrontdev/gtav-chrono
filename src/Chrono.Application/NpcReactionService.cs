using System.Diagnostics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Realistic NPC reactions (user request v0.6.0): after a power use, NPCs and police
/// cannot instantly track the player (no "superpower instinct"). During the grace
/// period the game's own perception system is suppressed via SET_*_IGNORE_PLAYER;
/// when it ends, normal line-of-sight perception resumes — NPCs only react to what
/// they can actually SEE (surprise → digest → search behavior comes from the game AI).
/// </summary>
public sealed class NpcReactionService
{
    private readonly IPlayerContext _player;
    private readonly ILogSink _log;
    private readonly NpcConfig _config;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _ignoreUntilMs;

    public NpcReactionService(IPlayerContext player, ILogSink log, NpcConfig config)
    {
        _player = player;
        _log = log;
        _config = config;
    }

    public bool IsGraceActive => _ignoreUntilMs > 0;

    /// <summary>Start/extend the grace period (NPCs stop perceiving the player).</summary>
    public void TriggerGracePeriod()
    {
        if (_config.ReactionDelayMs <= 0) return;   // disabled

        long until = _clock.ElapsedMilliseconds + _config.ReactionDelayMs;
        if (until > _ignoreUntilMs) _ignoreUntilMs = until;
        _player.SetNpcAwareness(false);
        _log.Debug("NPC reaction grace started");
    }

    /// <summary>Per-tick: end the grace period when the delay elapses.</summary>
    public void Tick()
    {
        if (!IsGraceActive) return;
        if (_clock.ElapsedMilliseconds >= _ignoreUntilMs)
        {
            _ignoreUntilMs = 0;
            _player.SetNpcAwareness(true);   // normal perception resumes
            _log.Debug("NPC reaction grace ended");
        }
    }
}
