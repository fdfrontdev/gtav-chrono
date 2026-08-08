using System;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// Prison outfit swap (S13). Saves the player's model + full wardrobe, swaps to
/// the vanilla prisoner model (u_m_m_prisoner_01 — jumpsuit by model, the same
/// trick prison mods like Jailbreak use), restores everything on release.
/// Verified natives: GET_PED_DRAWABLE_VARIATION / GET_PED_PROP_INDEX (SHVDN dump).
/// </summary>
public sealed class PrisonOutfit : IPrisonOutfit
{
    private const string PrisonerModelName = "u_m_m_prisoner_01";
    private readonly Action<string> _log;
    private Model _originalModel;
    private readonly int[] _drawables = new int[12];
    private readonly int[] _textures = new int[12];
    private readonly int[] _palettes = new int[12];
    private readonly int[] _propIndex = new int[7];
    private readonly int[] _propTextures = new int[7];
    private bool _saved;
    private bool _prisonApplied;

    public PrisonOutfit(Action<string> log) => _log = log;

    public void ApplyPrison()
    {
        try
        {
            var ped = Game.Player.Character;
            if (ped == null || !ped.Exists()) return;

            SaveOutfit(ped);
            _originalModel = ped.Model;

            var prisoner = new Model(PrisonerModelName);
            if (!prisoner.IsValid || !prisoner.IsInCdImage)
            {
                _log($"Prisoner model {PrisonerModelName} not found — skipping outfit swap");
                return;
            }
            prisoner.Request();
            for (int i = 0; i < 200 && !prisoner.IsLoaded; i++)
                Script.Wait(0);
            if (!prisoner.IsLoaded)
            {
                _log("Prisoner model failed to load — skipping outfit swap");
                prisoner.MarkAsNoLongerNeeded();
                return;
            }

            // SET_PLAYER_MODEL is the supported way to swap the PLAYER's model
            // (Ped.Model is read-only in SHVDN 3.9)
            Function.Call(Hash.SET_PLAYER_MODEL, Game.Player, prisoner.Hash);
            _prisonApplied = true;
            _log($"Prison outfit applied ({PrisonerModelName})");
        }
        catch (Exception ex)
        {
            _log($"Prison outfit apply failed: {ex.Message}");
        }
    }

    public void Restore()
    {
        try
        {
            var ped = Game.Player.Character;
            if (ped == null || !ped.Exists()) return;

            if (_prisonApplied && _originalModel.IsValid && _originalModel.IsInCdImage)
            {
                _originalModel.Request();
                for (int i = 0; i < 200 && !_originalModel.IsLoaded; i++)
                    Script.Wait(0);
                if (_originalModel.IsLoaded)
                    Function.Call(Hash.SET_PLAYER_MODEL, Game.Player, _originalModel.Hash);
            }
            _prisonApplied = false;

            if (_saved)
            {
                RestoreOutfit(ped);
                _saved = false;
            }
            _log("Prison outfit restored");
        }
        catch (Exception ex)
        {
            _log($"Prison outfit restore failed: {ex.Message}");
        }
    }

    private void SaveOutfit(Ped ped)
    {
        for (int i = 0; i < 12; i++)
        {
            _drawables[i] = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, ped, i);
            _textures[i] = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, ped, i);
            _palettes[i] = Function.Call<int>(Hash.GET_PED_PALETTE_VARIATION, ped, i);
        }
        for (int i = 0; i < 7; i++)
        {
            _propIndex[i] = Function.Call<int>(Hash.GET_PED_PROP_INDEX, ped, i);
            _propTextures[i] = Function.Call<int>(Hash.GET_PED_PROP_TEXTURE_INDEX, ped, i);
        }
        _saved = true;
    }

    private void RestoreOutfit(Ped ped)
    {
        for (int i = 0; i < 12; i++)
            Function.Call(Hash.SET_PED_COMPONENT_VARIATION, ped, i, _drawables[i], _textures[i], _palettes[i]);
        for (int i = 0; i < 7; i++)
        {
            if (_propIndex[i] >= 0)
                Function.Call(Hash.SET_PED_PROP_INDEX, ped, i, _propIndex[i], _propTextures[i], true);
            else
                Function.Call(Hash.CLEAR_PED_PROP, ped, i);
        }
    }
}
