using System;
using System.Collections.Generic;
using Chrono.Application.Ports;

namespace Chrono.Application;

/// <summary>
/// S21 v3 — pure HUD-widget layout engine (the "component model" for the widget).
/// Segmented Material card (user UAT: "readable font, proper widget with
/// segment/partition — beautiful, practical, usable"):
///
///   ┌──────────────────────────────────┐
///   │ CHRONO · FIRDAUS BUILDS ★★★ │  header strip (primary, full width)
///   ├──────────────────────────────────┤  divider 1
///   │ WANTED 3★                        │  status row (big, color-coded)
///   │ ▓▓▓▓▓▓▓▓░░░░  COURT IN 0:34     │  countdown row + progress bar
///   │ ┌────┐ ┌─────┐ ┌─────────┐      │  KPI tiles (S22 v8: dashboard BANs —
///   │ │ 3★ │ │DAY  │ │HEAT     │      │  big numerals, enclosed groups)
///   │ └────┘ └─────┘ └─────────┘      │
///   │ FACE ON FILE (BURNED)            │  identity row
///   ├──────────────────────────────────┤  divider 2
///   │ LIVE FEED                        │  section label (overline)
///   │ W BREAKING: suspect taken...     │  feed rows (blue webnet / gray msg)
///   │ A civilian recognized you...     │
///   └──────────────────────────────────┘
///
/// Font 0 (Chalet London — the vanilla HUD font) for readability; the v1/v2
/// font 4 (thin Chalet Comprimé) was the readability problem.
///
/// S22 v8 (user: "the HUD can be improved further — check dashboard design"):
/// applied The Big Book of Dashboards principles — BANs (big-ass numbers:
/// the KPI tiles carry the critical numerals), KPI row near the top (eye
/// scans the top row first), gestalt enclosure (each KPI is a bounded tile),
/// and alert colors for state (status row stays the biggest, color-coded).
/// </summary>
public sealed class HudLayoutEngine
{
    // ── Tokens (normalized) ──
    public const float CardW = 0.32f;            // a touch wider — bigger type needs room
    public const float RightMargin = 0.020f;     // from right screen edge
    public const float BottomY = 0.985f;         // card BOTTOM anchor
    public const float Font = 0;                 // Chalet London — the readable HUD font
    public const float HeaderH = 0.034f;
    public const float RowH = 0.030f;
    public const float DividerH = 0.002f;
    public const int MaxFeedRows = 4;            // S22 v8 r3: more room — duplicates killed, ambient added
    public const int MaxKpis = 3;                // S22 v8: dashboard KPI tiles

    public readonly record struct Rect(float X, float Y, float W, float H);
    public readonly record struct TextSpan(string Text, float X, float Y, float Scale, bool Bold);

    public sealed record Row(TextSpan Text, (int R, int G, int B) Color);

    /// <summary>S22 v8 — a dashboard KPI tile: label (small) over a BAN numeral.</summary>
    public sealed record KpiTile(Rect Tile, TextSpan Label, TextSpan Value);

    public sealed record Layout(
        Rect Card,           // surface
        Rect Shadow,         // elevation shadow
        Rect Header,         // primary strip (full card width, top)
        Rect Divider1,       // header / status
        Rect Divider2,       // status block / feed block
        Rect ProgressTrack,  // countdown bar track
        Rect ProgressFill,   // countdown bar fill (w = track.W * progress)
        Rect EnergyTrack,    // v0.10: combat energy bar (FR-B3)
        Rect EnergyFill,
        TextSpan HeaderTitle,   // "CHRONO" (brand — S22 v8 r2: CHRONO · FIRDAUS BUILDS)
        TextSpan HeaderStars,   // "★★★" right-aligned, amber
        Row Status,             // big, color-coded
        Row Countdown,          // smaller, dim
        IReadOnlyList<KpiTile> Kpis,   // S22 v8: dashboard KPI tiles
        IReadOnlyList<(Rect Track, Rect Fill)> NeedBars,   // v0.10: survivor bars
        Row Identity,           // dimmest
        TextSpan FeedLabel,     // "LIVE FEED" overline
        IReadOnlyList<Row> FeedRows,
        float CardHeight);

    /// <summary>
    /// Compute the segmented widget layout. kind = status color coding;
    /// progress 0..1 drives the countdown bar fill (0 = no bar).
    /// kpis = S22 v8 dashboard tiles — up to <see cref="MaxKpis"/>, each
    /// (label, value) pair rendered as an enclosed tile under the countdown.
    /// </summary>
    public static Layout Compute(
        string status, string countdown, string identity,
        IReadOnlyList<HudFeedItem> feed,
        JusticeStatusKind kind, float progress, int stars,
        Func<string, float, int, float> measure,
        bool hasCountdown = true, bool hasIdentity = true,
        bool countdownUrgent = false,   // S21 v3: yard-escape prompt → amber
        IReadOnlyList<(string Label, string Value)>? kpis = null,   // S22 v8
        int energy = 0, int energyMax = 0,             // v0.10: combat energy bar (FR-B3)
        IReadOnlyList<NeedBar>? needs = null)          // v0.10: survivor bars (FR-C14)
    {
        int feedRows = Math.Min(feed.Count, MaxFeedRows);
        // feed block: label pad + label + rows + bottom pad. The last row's text
        // CENTER sits at labelY+0.008+feedRows*0.024; its glyphs extend ~0.011
        // below the center (half line-height at 0.21 scale) — the v3 clip bug:
        // block was short by ~0.012, cutting the last feed line at the card edge.
        // S22 v8 r3: bottom pad raised 0.028 → 0.034 — the 4-row layout was
        // skimming the card edge (DOM check: last row flush against the bottom).
        float feedBlockH = 0.006f + 0.008f + feedRows * 0.024f + 0.034f;
        // S22 v8: KPI tile row height (label + BAN numeral inside one tile)
        float kpiH = kpis != null && kpis.Count > 0 ? 0.040f : 0f;
        // v0.10: life-systems band (energy bar + 4 need bars) under the KPI
        // tiles. UAT r48 r2 (user: labels unreadable): band 0.030 -> 0.090,
        // labels 0.13 -> 0.18, bars 0.006 -> 0.008 — readable at 1080p.
        float needsH = needs != null && needs.Count > 0 ? 0.090f : 0f;
        float cardH = HeaderH + DividerH + RowH * 2.5f + DividerH + kpiH + needsH + feedBlockH;

        float right = 1f - RightMargin;
        float x = right - CardW;
        float y = BottomY - cardH;
        var card = new Rect(x, y, CardW, cardH);
        var shadow = new Rect(x - 0.006f, y - 0.006f, CardW + 0.012f, cardH + 0.012f);

        float textX = x + 0.016f;
        float maxW = CardW - 0.032f;

        // ── Header strip (S22 v8 r2: CHRONO = product brand, FIRDAUS BUILDS = maker) ──
        var header = new Rect(x, y, CardW, HeaderH);
        var headerTitle = new TextSpan(Truncate("CHRONO · FIRDAUS BUILDS", maxW - 0.02f, measure, 0.22f), textX, y + HeaderH / 2f, 0.22f, true);
        string starStr = new string('★', Math.Max(0, Math.Min(5, stars)));
        var headerStars = new TextSpan(starStr, x + CardW - 0.016f - 0.055f, y + HeaderH / 2f, 0.22f, true);

        // ── Status block ──
        float divider1Y = y + HeaderH;
        var divider1 = new Rect(x + 0.008f, divider1Y, CardW - 0.016f, DividerH);

        float statusY = divider1Y + DividerH + RowH * 0.5f;
        var statusRow = new Row(
            new TextSpan(Truncate(status, maxW, measure, 0.30f), textX, statusY, 0.30f, true),
            KindColor(kind));

        float countdownY = statusY + RowH;
        var countdownRow = new Row(
            new TextSpan(Truncate(countdown, maxW, measure, 0.24f), textX, countdownY, 0.24f, countdownUrgent),
            countdownUrgent ? (255, 179, 64) : (170, 170, 170));   // amber = action prompt

        // ── S22 v8: KPI tiles (dashboard BANs — enclosed groups) ──
        var kpiTiles = new List<KpiTile>();
        float kpiY = countdownY + RowH * 0.5f + kpiH * 0.5f;   // tile CENTER: clears the countdown text
        if (kpis != null && kpis.Count > 0)
        {
            float gap = 0.006f;
            float tileW = (maxW - gap * (Math.Min(kpis.Count, MaxKpis) - 1)) / Math.Min(kpis.Count, MaxKpis);
            for (int i = 0; i < Math.Min(kpis.Count, MaxKpis); i++)
            {
                float tx = textX + i * (tileW + gap);
                var tile = new Rect(tx, kpiY - kpiH / 2f, tileW, kpiH);
                var label = new TextSpan(Truncate(kpis[i].Label, tileW - 0.008f, measure, 0.15f),
                    tx + 0.006f, kpiY - kpiH * 0.22f, 0.15f, false);
                var value = new TextSpan(Truncate(kpis[i].Value, tileW - 0.008f, measure, 0.26f),
                    tx + 0.006f, kpiY + kpiH * 0.16f, 0.26f, true);
                kpiTiles.Add(new KpiTile(tile, label, value));
            }
        }

        float identityY = countdownY + RowH + kpiH + needsH;

        // ── v0.10: life-systems band — energy bar (own label row) + 4 need bars.
        // Band top = identityY - needsH; rows: energy label, energy bar,
        // need labels, need bars. (UAT r48 r2 geometry: see block above.)
        float bandTop = identityY - needsH;
        Rect energyTrack = default, energyFill = default;
        if (energyMax > 0)
        {
            float eCenterY = bandTop + 0.030f;              // energy bar row
            energyTrack = new Rect(x + 0.016f, eCenterY - 0.004f, maxW, 0.008f);
            float ratio = Math.Max(0f, Math.Min(1f, (float)energy / energyMax));
            energyFill = new Rect(x + 0.016f, eCenterY - 0.004f, maxW * ratio, 0.008f);
        }

        var needBars = new List<(Rect Track, Rect Fill)>();
        if (needs != null && needs.Count > 0)
        {
            float gap = 0.008f;
            float barW = (maxW - gap * (needs.Count - 1)) / needs.Count;
            float needsBarCenterY = bandTop + 0.072f;       // need bar row (label above)
            for (int i = 0; i < needs.Count; i++)
            {
                float bx = textX + i * (barW + gap);
                var nTrack = new Rect(bx, needsBarCenterY - 0.004f, barW, 0.008f);
                float ratio = Math.Max(0f, Math.Min(1f, needs[i].Value / 100f));
                var nFill = new Rect(bx, needsBarCenterY - 0.004f, barW * ratio, 0.008f);
                needBars.Add((nTrack, nFill));
            }
        }
        var identityRow = new Row(
            new TextSpan(Truncate(identity, maxW, measure, 0.22f), textX, identityY, 0.22f, false),
            (130, 130, 130));

        // ── Divider 2 + feed block ──
        float feedTop = identityY + RowH * 0.5f;
        var divider2 = new Rect(x + 0.008f, feedTop, CardW - 0.016f, DividerH);
        float labelY = feedTop + 0.006f;
        var feedLabel = new TextSpan("LIVE FEED", textX, labelY, 0.17f, true);

        var feedRowsList = new List<Row>(feedRows);
        for (int i = 0; i < feedRows; i++)
        {
            var item = feed[i];
            // S22 v8 r3 (user: "feed seems too quiet"): the When timestamp was
            // stored but never shown — recency is what makes a feed feel LIVE.
            string stamp = item.When.Length >= 5 ? item.When.Substring(0, 5) : item.When;
            string label = $"{stamp}  " + (item.Kind == FeedKind.Webnet ? "W " + item.Text
                : item.Kind == FeedKind.Viral ? "V " + item.Text : item.Text);
            var color = item.Kind == FeedKind.Webnet ? (100, 181, 246)
                : item.Kind == FeedKind.Viral ? (239, 83, 80) : (150, 150, 150);
            feedRowsList.Add(new Row(
                new TextSpan(Truncate(label, maxW, measure, 0.21f), textX, labelY + 0.008f + (i + 1) * 0.024f, 0.21f, false),
                color));
        }

        // ── Countdown progress bar (under the countdown row) ──
        float barTop = countdownY + 0.010f;
        var track = new Rect(x + 0.016f, barTop, maxW, 0.004f);
        var fill = new Rect(x + 0.016f, barTop, maxW * Math.Max(0f, Math.Min(1f, progress)), 0.004f);

        return new Layout(card, shadow, header, divider1, divider2, track, fill,
            energyTrack, energyFill,
            headerTitle, headerStars, statusRow, countdownRow, kpiTiles, needBars, identityRow,
            feedLabel, feedRowsList, cardH);
    }

    /// <summary>Material semantic status colors (S21 v3).</summary>
    public static (int R, int G, int B) KindColor(JusticeStatusKind kind) => kind switch
    {
        JusticeStatusKind.Wanted   => (255, 179, 64),    // amber
        JusticeStatusKind.Captured => (239, 83, 80),     // red
        JusticeStatusKind.Prison   => (100, 181, 246),   // blue
        JusticeStatusKind.OnBail   => (171, 71, 188),    // violet
        JusticeStatusKind.Manhunt  => (198, 40, 40),     // crimson — prison-break manhunt
        _                          => (76, 175, 80),     // green (Free)
    };

    public static string Truncate(string text, float maxW, Func<string, float, int, float> measure,
        float scale)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (measure(text, scale, (int)Font) <= maxW) return text;
        for (int i = text.Length - 1; i > 0; i--)
        {
            string t = text.Substring(0, i) + "...";
            if (measure(t, scale, (int)Font) <= maxW) return t;
        }
        return "...";
    }
}
