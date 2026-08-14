using System;
using Chrono.Application;
using Chrono.Application.Ports;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// S21 v3 — segmented Material card HUD widget (user UAT: readable font 0,
/// proper widget with segment/partition). Geometry + colors from the pure
/// <see cref="HudLayoutEngine"/> — same engine drives the HTML preview tool.
/// </summary>
public sealed class MaterialHudRenderer : IHudRenderer
{
    private static readonly (int R, int G, int B) Primary = (24, 103, 192);
    private static readonly (int R, int G, int B) Surface = (30, 30, 30);
    private static readonly (int R, int G, int B) SurfaceVariant = (44, 44, 44);
    private static readonly (int R, int G, int B) Divider = (122, 122, 122);

    public void DrawJusticeHud(JusticeHudState state)
    {
        if (!state.Visible) return;

        var layout = HudLayoutEngine.Compute(
            state.StatusLine, state.CountdownLine, state.SecondLine,
            state.Feed ?? Array.Empty<HudFeedItem>(),
            state.Kind, state.Progress, state.Stars, Measure,
            hasCountdown: !string.IsNullOrEmpty(state.CountdownLine),
            hasIdentity: !string.IsNullOrEmpty(state.SecondLine),
            countdownUrgent: state.CountdownLine.Contains("YARD OPEN") || state.StatusLine.Contains("MANHUNT")
                || state.CountdownLine.Contains("TRANSPORT"),   // S22 v8: ride = action prompt (E to skip)
            kpis: state.Kpis,   // S22 v8: dashboard KPI tiles — MUST forward (game bug: preview passed them, renderer didn't)
            energy: state.Energy, energyMax: state.EnergyMax,   // v0.10: combat energy
            needs: state.Needs);                                // v0.10: survivor bars

        // ── Elevation: shadow → surface ──
        var s = layout.Shadow;
        Rect(s.X + s.W / 2f, s.Y + s.H / 2f, s.W, s.H, 0, 0, 0, 160);
        var c = layout.Card;
        Rect(c.X + c.W / 2f, c.Y + c.H / 2f, c.W, c.H, Surface.R, Surface.G, Surface.B, 242);

        // ── Header strip (primary) + title + stars ──
        var h = layout.Header;
        Rect(h.X + h.W / 2f, h.Y + h.H / 2f, h.W, h.H, Primary.R, Primary.G, Primary.B, 255);
        DrawSpan(layout.HeaderTitle, 235, 235, 235);
        DrawSpan(layout.HeaderStars, 255, 193, 7);   // amber stars

        // ── Dividers ──
        var d1 = layout.Divider1;
        Rect(d1.X + d1.W / 2f, d1.Y + d1.H / 2f, d1.W, d1.H, Divider.R, Divider.G, Divider.B, 140);
        var d2 = layout.Divider2;
        Rect(d2.X + d2.W / 2f, d2.Y + d2.H / 2f, d2.W, d2.H, Divider.R, Divider.G, Divider.B, 140);

        // ── Status / countdown / identity rows ──
        DrawRow(layout.Status);
        DrawRow(layout.Countdown);

        // ── S22 v8: dashboard KPI tiles (enclosed groups — gestalt principle) ──
        foreach (var kpi in layout.Kpis)
        {
            var t = kpi.Tile;
            Rect(t.X + t.W / 2f, t.Y + t.H / 2f, t.W, t.H, SurfaceVariant.R, SurfaceVariant.G, SurfaceVariant.B, 230);
            DrawSpan(kpi.Label, 160, 160, 160);
            // BAN value inherits the status alert color — the eye lands on it
            var ban = HudLayoutEngine.KindColor(state.Kind);
            DrawSpan(kpi.Value, ban.R, ban.G, ban.B);
        }

        // ── v0.10: combat energy bar (thin; amber when low) ──
        if (state.EnergyMax > 0)
        {
            var eTrack = layout.EnergyTrack;
            Rect(eTrack.X + eTrack.W / 2f, eTrack.Y, eTrack.W, eTrack.H, SurfaceVariant.R, SurfaceVariant.G, SurfaceVariant.B, 255);
            var eFill = layout.EnergyFill;
            if (eFill.W > 0.001f)
            {
                bool low = state.Energy < 30;
                (int R, int G, int B) ec = low ? (255, 179, 64) : (76, 175, 80);
                Rect(eFill.X + eFill.W / 2f, eFill.Y, eFill.W, eFill.H, ec.R, ec.G, ec.B, 255);
            }
        }

        // ── v0.10: survivor need bars (tier-colored fills) ──
        if (state.Needs != null && layout.NeedBars.Count == state.Needs.Count)
        {
            for (int i = 0; i < state.Needs.Count; i++)
            {
                var (track, fill) = layout.NeedBars[i];
                Rect(track.X + track.W / 2f, track.Y, track.W, track.H, SurfaceVariant.R, SurfaceVariant.G, SurfaceVariant.B, 255);
                if (fill.W > 0.001f)
                {
                    (int R, int G, int B) tierColor = state.Needs[i].Tier switch
                    {
                        Chrono.Domain.NeedsTier.Critical => (239, 83, 80),
                        Chrono.Domain.NeedsTier.Bad => (255, 179, 64),
                        _ => (76, 175, 80)
                    };
                    Rect(fill.X + fill.W / 2f, fill.Y, fill.W, fill.H, tierColor.R, tierColor.G, tierColor.B, 255);
                }
                // tiny label above the bar
                Text(state.Needs[i].Label, track.X, track.Y - 0.010f, 0.13f, 170, 170, 170, 255, bold: false, font: (int)HudLayoutEngine.Font);
            }
        }

        DrawRow(layout.Identity);

        // ── Countdown progress bar ──
        if (state.CourtCountdown || state.PrisonCountdown)
        {
            var track = layout.ProgressTrack;
            Rect(track.X + track.W / 2f, track.Y, track.W, track.H, SurfaceVariant.R, SurfaceVariant.G, SurfaceVariant.B, 255);
            var fill = layout.ProgressFill;
            if (fill.W > 0.001f)
            {
                // S21 v3: manhunt heat bar in crimson, court/prison in primary blue
                var kc = state.Kind == JusticeStatusKind.Manhunt
                    ? HudLayoutEngine.KindColor(JusticeStatusKind.Manhunt) : Primary;
                Rect(fill.X + fill.W / 2f, fill.Y, fill.W, fill.H, kc.R, kc.G, kc.B, 255);
            }
        }

        // ── Feed block ──
        DrawSpan(layout.FeedLabel, 190, 190, 190);
        foreach (var row in layout.FeedRows)
            DrawRow(row);
    }

    private void DrawRow(HudLayoutEngine.Row row)
    {
        var t = row.Text;
        Text(t.Text, t.X, CenterY(t.Y, t.Scale, (int)HudLayoutEngine.Font), t.Scale,
            row.Color.R, row.Color.G, row.Color.B, 255, bold: t.Bold, font: (int)HudLayoutEngine.Font);
    }

    private void DrawSpan(HudLayoutEngine.TextSpan span, int r, int g, int b)
    {
        Text(span.Text, span.X, CenterY(span.Y, span.Scale, (int)HudLayoutEngine.Font), span.Scale,
            r, g, b, 255, bold: span.Bold, font: (int)HudLayoutEngine.Font);
    }

    // ── Text measurement: the game's own width command (injected into the engine) ──
    private static float Measure(string text, float scale, int font)
    {
        Function.Call(Hash.SET_TEXT_FONT, font);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        return Function.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, 0);
    }

    // ── DRAW_RECT x/y = CENTER: caller passes the true center ──
    private static void Rect(float x, float y, float w, float h, int r, int g, int b, int a)
        => Function.Call(Hash.DRAW_RECT, x, y, w, h, r, g, b, a);

    // ── Text y = TOP of the text box; centered with the game's own line-height ──
    private static float CenterY(float rectCenterY, float scale, int font)
    {
        float h = Function.Call<float>((Hash)0xDB88A37483346780, scale, font);
        return rectCenterY - h / 2f;
    }

    private static void Text(string text, float x, float y, float scale, int r, int g, int b, int a, bool bold = false, int font = 0)
    {
        // S23 (user UAT 2026-08-13: "the star symbol shows as a rectangle"):
        // the ★ glyph (U+2605) is NOT in GTA's font 0 — it renders as a box.
        // Draw real wanted-star sprites instead (the game's own HUD dict).
        if (text.IndexOf('★') >= 0)
        {
            DrawTextWithStars(text, x, y, scale, r, g, b, a, bold, font);
            return;
        }

        DrawTextCore(text, x, y, scale, r, g, b, a, bold, font);
    }

    /// <summary>S23: draw text segment-by-segment, replacing every ★ with a
    /// procedurally drawn star (the text cursor advances by measured width;
    /// the star by its own footprint). No textures, no font glyphs — the
    /// game's wanted stars are scaleform vector art (verified: zero
    /// 'wanted*' / '*star*.ytd' strings across every .rpf in the install),
    /// so a texture-based approach can never work here.</summary>
    private static void DrawTextWithStars(string text, float x, float y, float scale, int r, int g, int b, int a, bool bold, int font)
    {
        float cursor = x;
        string seg = "";
        foreach (char ch in text)
        {
            if (ch == '★')
            {
                if (seg.Length > 0)
                {
                    DrawTextCore(seg, cursor, y, scale, r, g, b, a, bold, font);
                    cursor += Measure(seg, scale, font);
                    seg = "";
                }

                // S23 r4 (user UAT: "the star is too big"): star height = ~70%
                // of the text box (scale × 0.08) → it matches the DIGIT height
                // instead of towering over it (was scale × 0.085 ≈ 1.0-1.3×).
                float starH = scale * 0.055f;
                float starW = starH * 1.3f;   // 13 cells × 10-cell rows
                DrawStar(cursor + starW / 2f, y + starH / 2f, starH, r, g, b, a);
                cursor += starW + scale * 0.006f;   // footprint + gap
            }
            else seg += ch;
        }
        if (seg.Length > 0)
            DrawTextCore(seg, cursor, y, scale, r, g, b, a, bold, font);
    }

    /// <summary>S23 r3 — real 5-point star (13×11 cells, supersampled from a
    /// 10-vertex star polygon: 5 outer points + 5 inner notches). 12 rects per
    /// star: top point, wide arms, tapered waist, and TWO legs with a notch
    /// between them — the notches are what make it READ as a star at HUD size
    /// (r2's notch-less fat silhouette rendered as a blob).</summary>
    private static readonly (int Row, int X, int W)[] StarCells =
    {
        (1, 6, 1),   // top point
        (2, 6, 1),
        (3, 5, 3),
        (4, 1, 11),  // arms (widest row)
        (5, 2, 9),
        (6, 3, 7),   // waist taper
        (7, 4, 5),
        (8, 4, 5),
        (9, 3, 3),   // left leg
        (9, 7, 3),   // right leg  (notch between the legs)
        (10, 3, 1),  // leg tips
        (10, 9, 1),
    };

    /// <summary>Draw a filled star centered at (cx, cy) with the given height.</summary>
    private static void DrawStar(float cx, float cy, float height, int r, int g, int b, int a)
    {
        float cell = height / 10f;   // rows 1..10
        float totalW = 13f * cell;
        float yTop = cy - height / 2f;
        float xLeft = cx - totalW / 2f;
        foreach (var (row, x0, w) in StarCells)
        {
            float x = xLeft + (x0 + w / 2f) * cell;
            float y = yTop + (row - 1) * cell + cell / 2f;
            Rect(x, y, w * cell, cell, r, g, b, a);
        }
    }

    private static void DrawTextCore(string text, float x, float y, float scale, int r, int g, int b, int a, bool bold = false, int font = 0)
    {
        Function.Call(Hash.SET_TEXT_FONT, font);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_EDGE, 1, 0, 0, 0, 120);
        Function.Call(Hash.SET_TEXT_CENTRE, false);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
    }
}
