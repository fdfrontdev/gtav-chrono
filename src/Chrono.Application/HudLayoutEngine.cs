using System;
using System.Collections.Generic;

namespace Chrono.Application;

/// <summary>
/// S21 v2 — pure HUD-widget layout engine (the "component model" for the widget,
/// same idea as <see cref="MenuLayoutEngine"/>). Computes ALL widget geometry:
/// card, accent rail, status row, countdown row, identity row, feed rows.
/// Game-free — text width from an injected measurer (native in-game, font table
/// in the preview tool). One layout drives the renderer AND the HTML preview.
///
/// FIXES from user UAT (screenshot 2026-08-09): balanced vertical padding
/// (rows were top-heavy, bottom line hugged the edge), wider accent rail,
/// text no longer crowds the left with dead right space.
/// </summary>
public sealed class HudLayoutEngine
{
    // ── Tokens (normalized) ──
    public const float CardW = 0.30f;            // wider than v1 (0.21) — feed needs room
    public const float RightMargin = 0.020f;     // from right screen edge
    public const float BottomY = 0.985f;         // card BOTTOM anchor (v2: taller card with feed)
    public const float AccentW = 0.009f;         // accent rail width (v1 was 0.006 — too thin)
    public const float RowH = 0.026f;
    public const int MaxFeedRows = 4;

    public readonly record struct Rect(float X, float Y, float W, float H);
    public readonly record struct TextSpan(string Text, float X, float Y, float Scale, int Font, bool Bold);

    public sealed record Row(TextSpan Text, (int R, int G, int B) Color);

    public sealed record Layout(
        Rect Card,          // surface
        Rect Shadow,        // elevation shadow
        Rect Accent,        // primary rail on the left edge
        IReadOnlyList<Row> Rows,          // status / countdown / identity / feed lines
        float CardHeight);

    /// <summary>
    /// Compute widget layout. status/countdown/identity are the v1 rows; feed is
    /// the live message tail (messages + WEBNET headlines). measure(text, scale,
    /// font) → normalized width.
    /// </summary>
    public static Layout Compute(
        string status, string countdown, string identity,
        IReadOnlyList<HudFeedItem> feed,
        Func<string, float, int, float> measure,
        bool hasCountdown = true, bool hasIdentity = true)
    {
        float cardH = 0.012f + RowH + RowH + RowH + 0.006f + MaxFeedRows * 0.022f + 0.012f;
        float right = 1f - RightMargin;          // card RIGHT edge (left-edge semantics)
        float x = right - CardW;                 // card left edge — v2 bug fix: right edge was at 1.13 (off-screen)
        float y = BottomY - cardH;               // v2: BottomY anchors the card BOTTOM (not center)

        var card = new Rect(x, y, CardW, cardH);
        var shadow = new Rect(x - 0.006f, y - 0.006f, CardW + 0.012f, cardH + 0.012f);
        var accent = new Rect(x + 0.004f, y, AccentW, cardH - 0.008f);

        var rows = new List<Row>(3 + MaxFeedRows);
        float textX = x + 0.018f;
        float maxW = CardW - 0.036f;

        // Row 1 — status (bold, near-white)
        rows.Add(new Row(new TextSpan(Truncate(status, maxW, measure, 0.24f, 4), textX, y + 0.006f + RowH / 2f, 0.24f, 4, true), (235, 235, 235)));

        // Row 2 — countdown (amber for court, blue for prison, dim otherwise)
        if (hasCountdown && !string.IsNullOrEmpty(countdown))
            rows.Add(new Row(new TextSpan(Truncate(countdown, maxW, measure, 0.26f, 4), textX, y + 0.006f + RowH * 1.5f, 0.26f, 4, false), (160, 160, 160)));

        // Row 3 — identity (dim)
        if (hasIdentity && !string.IsNullOrEmpty(identity))
            rows.Add(new Row(new TextSpan(Truncate(identity, maxW, measure, 0.22f, 4), textX, y + 0.006f + RowH * 2.5f, 0.22f, 4, false), (120, 120, 120)));

        // Feed — up to MaxFeedRows, oldest first; WEBNET/viral get the blue tint
        float feedTop = y + 0.006f + RowH * 3 + 0.006f;
        int shown = Math.Min(feed.Count, MaxFeedRows);
        for (int i = 0; i < shown; i++)
        {
            var item = feed[i];
            string label = item.Kind == FeedKind.Webnet ? "W " + item.Text : item.Text;
            if (item.Kind == FeedKind.Viral) label = "V " + item.Text;
            var color = item.Kind == FeedKind.Webnet ? (100, 181, 246)
                : item.Kind == FeedKind.Viral ? (239, 83, 80) : (140, 140, 140);
            rows.Add(new Row(new TextSpan(Truncate(label, maxW, measure, 0.19f, 4), textX, feedTop + i * 0.022f, 0.19f, 4, false), color));
        }

        return new Layout(card, shadow, accent, rows, cardH);
    }

    public static string Truncate(string text, float maxW, Func<string, float, int, float> measure,
        float scale, int font)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (measure(text, scale, font) <= maxW) return text;
        for (int i = text.Length - 1; i > 0; i--)
        {
            string t = text.Substring(0, i) + "...";
            if (measure(t, scale, font) <= maxW) return t;
        }
        return "...";
    }
}
