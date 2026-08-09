using System;
using System.Collections.Generic;
using Chrono.Application;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S21 v2 — deterministic layout tests for the HUD widget (the "proper testing"
/// loop the user asked for). Same <see cref="HudLayoutEngine"/> drives the
/// in-game renderer AND the HTML preview tool.
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

    // ── 1. Card geometry ──

    [Fact]
    public void Card_IsBottomRight_InsideScreen()
    {
        var layout = HudLayoutEngine.Compute("FREE", "", "CLEAN IDENTITY", Feed(), Measure,
            hasCountdown: false);

        // Card RIGHT edge must sit at the right margin (v2 bug: it was at 1.13 — off-screen)
        float cardRight = layout.Card.X + layout.Card.W;
        Assert.InRange(cardRight, 1f - HudLayoutEngine.RightMargin - 0.001f, 1f - HudLayoutEngine.RightMargin + 0.001f);
        // Card bottom must sit at the bottom anchor (not above, not off-screen)
        Assert.InRange(layout.Card.Y + layout.Card.H, HudLayoutEngine.BottomY - 0.001f, HudLayoutEngine.BottomY + 0.001f);
        Assert.True(layout.Card.X > 0.5f, "card must be on the right half");
        Assert.True(layout.Card.X + layout.Card.W <= 1f, "card must not exceed the screen width");
    }

    [Fact]
    public void AccentRail_SpansCard_InsideLeftEdge()
    {
        var layout = HudLayoutEngine.Compute("FREE", "", "", Feed(), Measure, hasCountdown: false, hasIdentity: false);

        Assert.True(layout.Accent.W >= 0.008f, "accent rail must be visible (v1 was too thin)");
        Assert.True(layout.Accent.X + layout.Accent.W <= layout.Card.X + 0.02f,
            "accent must sit near the card's left edge");
        Assert.True(layout.Accent.H <= layout.Card.H - 0.004f, "accent must not exceed card height");
    }

    // ── 2. Row balance (the v1 "off" look: top-heavy, bottom hugged the edge) ──

    [Fact]
    public void Rows_AreVerticallyBalanced_InsideCard()
    {
        var layout = HudLayoutEngine.Compute("WANTED 3*", "COURT IN 0:34", "WARRANT ACTIVE", Feed(), Measure);

        Assert.True(layout.Rows.Count >= 3, "status + countdown + identity rows");
        foreach (var row in layout.Rows)
        {
            Assert.InRange(row.Text.Y, layout.Card.Y + 0.004f, layout.Card.Y + layout.Card.H - 0.004f);
        }
        // status must not hug the top edge (v1 defect)
        Assert.True(layout.Rows[0].Text.Y > layout.Card.Y + 0.008f,
            "status row must have top breathing room");
        // identity must not hug the bottom edge
        var last = layout.Rows[Math.Min(2, layout.Rows.Count - 1)];
        Assert.True(last.Text.Y < layout.Card.Y + layout.Card.H - 0.008f,
            "last text row must have bottom breathing room");
    }

    // ── 3. Feed ──

    [Fact]
    public void Feed_RendersUpToMaxRows_OldestFirst()
    {
        var feed = Feed(
            ("first", FeedKind.Message),
            ("second", FeedKind.Message),
            ("third", FeedKind.Webnet),
            ("fourth", FeedKind.Viral),
            ("fifth", FeedKind.Message),
            ("sixth", FeedKind.Message));   // 6 items > MaxFeedRows 4

        var layout = HudLayoutEngine.Compute("FREE", "", "CLEAN", feed, Measure, hasCountdown: false);

        int feedRows = layout.Rows.Count - 2;   // status + identity are the first 2
        Assert.Equal(HudLayoutEngine.MaxFeedRows, feedRows);
        // oldest items shown (first 4 of the feed)
        Assert.Contains(layout.Rows, r => r.Text.Text == "first");
        Assert.DoesNotContain(layout.Rows, r => r.Text.Text == "sixth");
    }

    [Fact]
    public void WebnetItems_GetBlueTint_AndPrefix()
    {
        var feed = Feed(("BREAKING: suspect caught", FeedKind.Webnet));
        var layout = HudLayoutEngine.Compute("FREE", "", "CLEAN", feed, Measure, hasCountdown: false);

        var webnetRow = layout.Rows[2];
        Assert.StartsWith("W ", webnetRow.Text.Text);
        Assert.Equal((100, 181, 246), webnetRow.Color);
    }

    [Fact]
    public void LongFeedText_IsEllipsized()
    {
        var feed = Feed(("A very long WEBNET headline that will never fit the widget width at all so it must be truncated with dots", FeedKind.Webnet));
        var layout = HudLayoutEngine.Compute("FREE", "", "CLEAN", feed, Measure, hasCountdown: false);

        Assert.EndsWith("...", layout.Rows[2].Text.Text);
    }

    // ── 4. Feed buffer behavior ──

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
