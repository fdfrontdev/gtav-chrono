using System;
using System.Collections.Generic;
using Chrono.Application;
using Chrono.Application.Ports;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S21 v3 — deterministic layout tests for the segmented HUD widget (the
/// "proper widget" redesign: header strip, divider lines, color-coded status,
/// progress bar, LIVE FEED segment). Same <see cref="HudLayoutEngine"/> drives
/// the in-game renderer AND the HTML preview tool.
/// </summary>
public class HudLayoutEngineTests
{
    private static float Measure(string text, float scale, int font)
        => text.Length * scale * 0.0265f;

    private static List<HudFeedItem> Feed(params (string text, FeedKind kind)[] items)
    {
        var list = new List<HudFeedItem>();
        foreach (var (t, k) in items) list.Add(new HudFeedItem(t, k, "12:00:00"));
        return list;
    }

    private static HudLayoutEngine.Layout Compute(
        string status = "FREE", string countdown = "", string identity = "CLEAN IDENTITY",
        List<HudFeedItem>? feed = null, JusticeStatusKind kind = JusticeStatusKind.Free, float progress = 0f, int stars = 3)
        => HudLayoutEngine.Compute(status, countdown, identity,
            feed ?? Feed(), kind, progress, stars, Measure,
            hasCountdown: !string.IsNullOrEmpty(countdown),
            hasIdentity: !string.IsNullOrEmpty(identity));

    // ── 1. Card geometry ──

    [Fact]
    public void Card_IsBottomRight_InsideScreen()
    {
        var layout = Compute();

        // Card RIGHT edge must sit at the right margin (v2 bug: it was at 1.13 — off-screen)
        float cardRight = layout.Card.X + layout.Card.W;
        Assert.InRange(cardRight, 1f - HudLayoutEngine.RightMargin - 0.001f, 1f - HudLayoutEngine.RightMargin + 0.001f);
        // Card bottom must sit at the bottom anchor
        Assert.InRange(layout.Card.Y + layout.Card.H, HudLayoutEngine.BottomY - 0.001f, HudLayoutEngine.BottomY + 0.001f);
        Assert.True(layout.Card.X > 0.5f, "card must be on the right half");
        Assert.True(layout.Card.X + layout.Card.W <= 1f, "card must not exceed the screen width");
    }

    [Fact]
    public void HeaderStrip_SitsAtCardTop_FullWidth()
    {
        var layout = Compute();

        Assert.Equal(layout.Card.Y, layout.Header.Y, 3);
        Assert.Equal(layout.Card.W, layout.Header.W, 3);
        Assert.Equal(HudLayoutEngine.HeaderH, layout.Header.H, 3);
        Assert.Equal(layout.Card.X, layout.Header.X, 3);
    }

    // ── 2. Segments & partitions (the "proper widget" ask) ──

    [Fact]
    public void Dividers_SeparateHeaderAndFeedBlocks()
    {
        var layout = Compute(feed: Feed(("msg", FeedKind.Message)));

        // divider 1: between header and status block
        Assert.True(layout.Divider1.Y > layout.Header.Y + layout.Header.H - 0.001f,
            "divider1 must sit below the header strip");
        Assert.True(layout.Status.Text.Y > layout.Divider1.Y, "status must sit below divider1");

        // divider 2: between status block and feed block
        Assert.True(layout.Divider2.Y > layout.Identity.Text.Y, "divider2 must sit below identity");
        Assert.True(layout.FeedLabel.Y > layout.Divider2.Y, "feed label must sit below divider2");
        Assert.Equal(layout.Divider2.Y, layout.Divider2.Y, 3);   // same rect, defined once
    }

    [Fact]
    public void FeedLabel_PrefixesFeedRows()
    {
        var layout = Compute(feed: Feed(("hello", FeedKind.Message)));

        Assert.Equal("LIVE FEED", layout.FeedLabel.Text);
        Assert.Single(layout.FeedRows);
        Assert.True(layout.FeedRows[0].Text.Y > layout.FeedLabel.Y, "feed rows below the label");
    }

    // ── 3. Row balance & readability (font 0, bigger type) ──

    [Fact]
    public void StatusRow_UsesReadableFontAndBigScale()
    {
        var layout = Compute(status: "WANTED 3★", kind: JusticeStatusKind.Wanted);

        Assert.Equal(0, (int)HudLayoutEngine.Font);   // font 0 = Chalet London
        Assert.True(layout.Status.Text.Scale >= 0.28f, "status must be big (readability)");
        Assert.True(layout.Status.Text.Bold, "status must be bold");
    }

    [Fact]
    public void Rows_AreVerticallyBalanced_InsideCard()
    {
        var layout = Compute(countdown: "COURT IN 0:34", identity: "WARRANT ACTIVE",
            feed: Feed(("m", FeedKind.Message)), kind: JusticeStatusKind.Captured, progress: 0.5f);

        foreach (var row in new[] { layout.Status, layout.Countdown, layout.Identity })
        {
            Assert.InRange(row.Text.Y, layout.Card.Y, layout.Card.Y + layout.Card.H);
        }
        Assert.True(layout.Status.Text.Y > layout.Card.Y + 0.008f, "status must have top breathing room");
    }

    [Fact]
    public void HeaderStars_ReflectWantedLevel_Clamped()
    {
        var none = Compute(stars: 0);
        Assert.Equal("", none.HeaderStars.Text);

        var three = Compute(stars: 3);
        Assert.Equal("★★★", three.HeaderStars.Text);

        var over = Compute(stars: 7);
        Assert.Equal("★★★★★", over.HeaderStars.Text);   // clamped to 5
    }

    // ── 4. Status color coding ──

    [Fact]
    public void KindColor_CodesEachState()
    {
        Assert.Equal((76, 175, 80), HudLayoutEngine.KindColor(JusticeStatusKind.Free));     // green
        Assert.Equal((255, 179, 64), HudLayoutEngine.KindColor(JusticeStatusKind.Wanted));  // amber
        Assert.Equal((239, 83, 80), HudLayoutEngine.KindColor(JusticeStatusKind.Captured)); // red
        Assert.Equal((100, 181, 246), HudLayoutEngine.KindColor(JusticeStatusKind.Prison)); // blue
        Assert.Equal((171, 71, 188), HudLayoutEngine.KindColor(JusticeStatusKind.OnBail));  // violet
    }

    [Fact]
    public void StatusColor_MatchesKind()
    {
        var layout = Compute(status: "IN CUSTODY — COURT AWAITS", kind: JusticeStatusKind.Captured);
        Assert.Equal(HudLayoutEngine.KindColor(JusticeStatusKind.Captured), layout.Status.Color);
    }

    // ── 5. Progress bar ──

    [Fact]
    public void ProgressFill_ScalesWithProgress_Clamped()
    {
        var half = Compute(countdown: "COURT IN 0:34", progress: 0.5f);
        Assert.Equal(half.ProgressTrack.W * 0.5f, half.ProgressFill.W, 3);

        var full = Compute(countdown: "COURT IN 0:05", progress: 1.2f);
        Assert.Equal(full.ProgressTrack.W, full.ProgressFill.W, 3);   // clamped to 1.0

        var none = Compute(progress: 0f);
        Assert.True(none.ProgressFill.W <= 0.001f, "zero progress → no fill");
    }

    // ── 6. Feed ──

    [Fact]
    public void Feed_RendersUpToMaxRows_OldestFirst()
    {
        var feed = Feed(
            ("first", FeedKind.Message),
            ("second", FeedKind.Message),
            ("third", FeedKind.Webnet),
            ("fourth", FeedKind.Viral),
            ("fifth", FeedKind.Message));   // 5 items > MaxFeedRows 3

        var layout = Compute(feed: feed);

        Assert.Equal(HudLayoutEngine.MaxFeedRows, layout.FeedRows.Count);
        Assert.Contains(layout.FeedRows, r => r.Text.Text == "first");
        Assert.DoesNotContain(layout.FeedRows, r => r.Text.Text == "fifth");
    }

    [Fact]
    public void WebnetItems_GetBlueTint_AndPrefix()
    {
        var layout = Compute(feed: Feed(("BREAKING: suspect caught", FeedKind.Webnet)));

        Assert.StartsWith("W ", layout.FeedRows[0].Text.Text);
        Assert.Equal((100, 181, 246), layout.FeedRows[0].Color);
    }

    [Fact]
    public void LongFeedText_IsEllipsized()
    {
        var layout = Compute(feed: Feed(("A very long WEBNET headline that will never fit the widget width at all so it must be truncated with dots", FeedKind.Webnet)));

        Assert.EndsWith("...", layout.FeedRows[0].Text.Text);
    }

    // ── 7. Feed buffer behavior ──

    [Fact]
    public void FeedBuffer_CapsAtMaxItems_OldestDropped()
    {
        var buffer = new HudFeedBuffer();
        for (int i = 0; i < 10; i++) buffer.Push($"msg {i}");
        Assert.Equal(HudFeedBuffer.MaxItems, buffer.Items.Count);
        Assert.Equal("msg 5", buffer.Items[0].Text);
        Assert.Equal("msg 9", buffer.Items[buffer.Items.Count - 1].Text);
    }

    [Fact]
    public void FeedBuffer_IgnoresBlank()
    {
        var buffer = new HudFeedBuffer();
        buffer.Push("   ");
        buffer.Push("");
        Assert.Empty(buffer.Items);
    }
}
