using System;
using System.Numerics;
using Chrono.Application.Ports;

namespace Chrono.Application;

public enum CutsceneKind { None, Confrontation, Arrest, Trial, Intake, Release }

/// <summary>
/// Cinematic justice director (S11): arrest → booking, court session, prison intake
/// and release are presented as scripted camera sequences with cuffed animations,
/// banners and screen feedback — the player is a person in the system, not a
/// conveyor-belt object (SB design anchor: "people in stress need information,
/// pacing and control — IDEO hospital-journey study").
///
/// Phases are camera framings + banner beats around a fixed anchor point (the
/// player's position at cutscene start — NO teleports, so no interior/IPL risks).
/// The completion callback fires after the final beat (sentence application,
/// confinement start, gate teleport).
/// </summary>
public sealed class JusticeCutsceneService
{
    private readonly ICutsceneRenderer _renderer;
    private readonly IPlayerContext _player;
    private readonly ILogSink _log;
    private readonly INotifier? _notifier;   // S21 v3: banners route into the widget feed
    private CutsceneKind _kind = CutsceneKind.None;
    private Vector3 _anchor;
    private long _phaseStartMs;
    private int _phaseIndex;
    private string[] _lines = Array.Empty<string>();
    private Action? _onComplete;

    // Verified anim (DurtyFree dump): cuffed prisoner idle
    private const string CuffedDict = "anim@move_m@prisoner_cuffed";
    private const string CuffedIdle = "idle";

    public JusticeCutsceneService(ICutsceneRenderer renderer, IPlayerContext player, ILogSink log,
        INotifier? notifier = null)   // S21 v3
    {
        _renderer = renderer;
        _player = player;
        _log = log;
        _notifier = notifier;
    }

    public bool IsActive => _kind != CutsceneKind.None;

    /// <summary>Play a cutscene. <paramref name="lines"/> feeds the beat banners
    /// (trial: charge / verdict / sentence).</summary>
    public void Play(CutsceneKind kind, Action? onComplete = null, params string[] lines)
    {
        _kind = kind;
        _lines = lines ?? Array.Empty<string>();
        _onComplete = onComplete;
        _anchor = _player.Position;
        _phaseIndex = 0;
        _phaseStartMs = -1;   // sentinel: first Tick sets the baseline (nowMs may be 0)
        _renderer.Begin();
        _renderer.PlayAnim(CuffedDict, CuffedIdle, true);
        _log.Info($"Cutscene started: {kind}");
    }

    /// <summary>Drive the current cutscene (call every tick from the script).</summary>
    public void Tick(long nowMs)
    {
        if (_kind == CutsceneKind.None) return;

        if (_phaseStartMs < 0)
        {
            _phaseStartMs = nowMs;
            EnterPhase();
            return;
        }

        if (nowMs - _phaseStartMs < PhaseDuration(_kind, _phaseIndex))
            return;   // banner + camera were set on phase entry — steady state until the beat ends

        // Phase finished → advance or end
        _phaseIndex++;
        if (_phaseIndex >= PhaseCount(_kind))
        {
            End();
            return;
        }

        _phaseStartMs = nowMs;
        EnterPhase();
    }

    private void EnterPhase()
    {
        string banner = PhaseBanner(_kind, _phaseIndex);
        // S21 v3 (user UAT: "mid-screen white text on black — what is that?
        // we already have the widget"): banners route into the WIDGET feed;
        // the mid-screen band is no longer drawn.
        _notifier?.Show(banner);
        _renderer.ShowBanner("");
        switch (_kind)
        {
            case CutsceneKind.Confrontation:
                _renderer.SetCamera(_anchor + new Vector3(0f, 2.0f, 1.1f), _anchor + new Vector3(0f, 0f, 0.5f), 44f);
                break;

            case CutsceneKind.Arrest:
                if (_phaseIndex == 0)
                    _renderer.SetCamera(_anchor + new Vector3(0f, 1.4f, 0.7f), _anchor + new Vector3(0f, 0f, 0.5f), 45f);
                else if (_phaseIndex == 1)
                    _renderer.SetCamera(_anchor + new Vector3(1.1f, 0.9f, 0.5f), _anchor + new Vector3(0f, 0f, 0.5f), 50f);
                else
                    _renderer.SetCamera(_anchor + new Vector3(0f, -1.6f, 1.0f), _anchor + new Vector3(0f, 0f, 0.5f), 42f);
                break;

            case CutsceneKind.Trial:
                if (_phaseIndex == 0)
                    _renderer.SetCamera(_anchor + new Vector3(0f, 2.6f, 1.7f), _anchor + new Vector3(0f, 0f, 0.4f), 38f);   // judge's bench
                else
                    _renderer.SetCamera(_anchor + new Vector3(-1.6f, 0.4f, 1.1f), _anchor + new Vector3(0f, 0f, 0.4f), 42f);  // gallery
                break;

            case CutsceneKind.Intake:
                if (_phaseIndex == 0)
                    _renderer.SetCamera(_anchor + new Vector3(0f, -2.2f, 1.2f), _anchor + new Vector3(0f, 0f, 0.3f), 44f);
                else
                    _renderer.SetCamera(_anchor + new Vector3(0f, 1.6f, 1.0f), _anchor + new Vector3(0f, 0f, 0.4f), 48f);
                break;

            case CutsceneKind.Release:
                _renderer.SetCamera(_anchor + new Vector3(0f, -1.8f, 1.0f), _anchor + new Vector3(0f, 0f, 0.5f), 46f);
                break;
        }
    }

    /// <summary>
    /// S22 (user UAT: "mod makes a mess on main story events"): force-end an
    /// active cutscene when a story mission takes over — restore the camera and
    /// player control WITHOUT running the completion callback (the mission owns
    /// the flow now). No-op when nothing is playing.
    /// </summary>
    public void Abort()
    {
        if (_kind == CutsceneKind.None) return;
        _log.Info($"Cutscene {_kind} aborted — mission takeover");
        _kind = CutsceneKind.None;
        _onComplete = null;
        _renderer.End();
        _player.ClearCurrentAnimation();   // never leave the cuffed idle locking movement
    }

    private void End()
    {
        CutsceneKind finished = _kind;
        _kind = CutsceneKind.None;
        _renderer.End();
        var complete = _onComplete;
        _onComplete = null;
        _log.Info("Cutscene finished");
        complete?.Invoke();
        // S21 v3 fix (user UAT: "FREE but still handcuffed, can't move"): the
        // cuffed idle is played as a LOOP at cutscene start; the release cutscene
        // must STOP it or the player stays in the cuffed pose with no movement
        // control (End() restores the camera, not the animation task).
        if (finished == CutsceneKind.Release)
            _player.ClearCurrentAnimation();
    }
    private int PhaseCount(CutsceneKind kind) => kind switch
    {
        CutsceneKind.Confrontation => 1,
        CutsceneKind.Arrest => 3,
        CutsceneKind.Trial => 3,
        CutsceneKind.Intake => 2,
        CutsceneKind.Release => 1,
        _ => 0
    };

    private long PhaseDuration(CutsceneKind kind, int index) => kind switch
    {
        CutsceneKind.Confrontation => 3000,
        CutsceneKind.Arrest => index == 0 ? 2400 : index == 1 ? 3000 : 2400,
        CutsceneKind.Trial => index == 0 ? 2600 : index == 1 ? 3000 : 3200,
        CutsceneKind.Intake => index == 0 ? 2600 : 2400,
        CutsceneKind.Release => 2600,
        _ => 1000
    };

    private string PhaseBanner(CutsceneKind kind, int index)
    {
        switch (kind)
        {
            case CutsceneKind.Confrontation:
                return "POLICE! HANDS WHERE I CAN SEE THEM — DON'T MOVE";
            case CutsceneKind.Arrest:
                return index switch
                {
                    0 => "ARRESTED",
                    1 => "POLICE CUSTODY — you have the right to remain silent",
                    _ => "BOOKING — face on file · personal effects confiscated"
                };
            case CutsceneKind.Trial:
                return index switch
                {
                    0 => "THE COURT IS NOW IN SESSION",
                    1 => _lines.Length > 0 ? $"THE CHARGE: {_lines[0]}" : "THE CHARGE",
                    _ => _lines.Length > 1 ? _lines[1] : "THE VERDICT"
                };
            case CutsceneKind.Intake:
                return index == 0 ? "BOLINGBROKE STATE PENITENTIARY" : "INMATE — welcome to your new home";
            case CutsceneKind.Release:
                return "RELEASED — justice served";
            default:
                return "";
        }
    }
}
