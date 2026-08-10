using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chrono.Application;
using Chrono.Application.Ports;

namespace Chrono.MenuPreview;

/// <summary>
/// S21 — HTML preview generator (Playwright-style visual testing for the GTA UI,
/// user request 2026-08-09). Runs the SAME <see cref="MenuLayoutEngine"/> that
/// drives the in-game renderer, but maps normalized coords to a 1280x720 canvas
/// and uses an approximate font-width measurer (Arial metrics) so the layout can
/// be SEEN in a browser without launching GTA. Output: menu-preview.html.
///
/// Usage: dotnet run --project tools/MenuPreview  →  writes menu-preview.html
/// </summary>
public static class Program
{
    private const int W = 1280, H = 720;

    public static void Main()
    {
        var screens = BuildScreens();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{margin:0;background:#0b0b0b;font-family:Arial,sans-serif;}");
        sb.AppendLine(".stage{position:relative;width:1280px;height:720px;margin:20px auto;background:#101010;overflow:hidden;}");
        sb.AppendLine(".screen{position:absolute;top:0;left:0;width:1280px;height:720px;display:none;}");
        sb.AppendLine(".screen.active{display:block;}");
        sb.AppendLine(".rect{position:absolute;transform:translate(-50%,-50%);}");
        sb.AppendLine(".text{position:absolute;transform:translate(0,-50%);white-space:nowrap;text-shadow:1px 1px 2px #000;}");
        sb.AppendLine(".tab{position:fixed;top:8px;left:8px;z-index:99;background:#1e1e1e;color:#eee;font:13px Arial;padding:6px 10px;border:1px solid #4a4a4a;cursor:pointer;border-radius:4px;}");
        sb.AppendLine(".tab:hover{background:#2c2c2c;}");
        sb.AppendLine(".sel{background:#1867c0 !important;border-color:#1867c0 !important;}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div id='tabs'></div>");
        sb.AppendLine("<div class='stage'>");
        int screenIdx = 0;
        foreach (var (name, content) in screens)
        {
            sb.AppendLine($"<div class='screen' id='scr{screenIdx}'>");
            if (content is MenuScreen ms)
                AppendMenuScreen(sb, ms);
            else
                AppendWidgetScreen(sb, (WidgetPreview)content!);
            sb.AppendLine("</div>");
            screenIdx++;
        }
        sb.AppendLine("</div>");

        sb.AppendLine("<script>");
        sb.AppendLine("const tabs=[");
        for (int i = 0; i < screens.Count; i++)
            sb.AppendLine($"{{n:'{screens[i].name.Replace("'", "\\'")}',i:{i}}},");
        sb.AppendLine("];const tb=document.getElementById('tabs');");
        sb.AppendLine("tabs.forEach(t=>{const b=document.createElement('div');b.className='tab'+(t.i===0?' sel':'');b.textContent=t.n;b.onclick=()=>{document.querySelectorAll('.screen').forEach(s=>s.classList.remove('active'));document.getElementById('scr'+t.i).classList.add('active');document.querySelectorAll('.tab').forEach(x=>x.classList.remove('sel'));b.classList.add('sel');};tb.appendChild(b);});");
        sb.AppendLine("document.getElementById('scr0').classList.add('active');");
        sb.AppendLine("</script></body></html>");

        string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "menu-preview.html");
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"WROTE {outPath} ({screens.Count} screens)");
    }

    /// <summary>Widget preview inputs — the same snapshot shape the widget builds.</summary>
    private sealed record WidgetPreview(
        string Status, string Countdown, string Identity,
        HudFeedItem[] Feed, JusticeStatusKind Kind, float Progress, int Stars = 3);

    private static void AppendWidgetScreen(StringBuilder sb, WidgetPreview wp)
    {
        var layout = HudLayoutEngine.Compute(wp.Status, wp.Countdown, wp.Identity,
            wp.Feed, wp.Kind, wp.Progress, wp.Stars, MeasureApprox,
            hasCountdown: !string.IsNullOrEmpty(wp.Countdown),
            hasIdentity: !string.IsNullOrEmpty(wp.Identity));

        AppendRect(sb, layout.Shadow, 0, 0, 0, 0.62f);
        AppendRect(sb, layout.Card, 30, 30, 30, 0.95f);

        // header strip
        AppendRect(sb, layout.Header, 24, 103, 192, 1f);
        AppendText(sb, layout.HeaderTitle.Text, layout.HeaderTitle.X, layout.HeaderTitle.Y, layout.HeaderTitle.Scale, 235, 235, 235, true);
        AppendText(sb, layout.HeaderStars.Text, layout.HeaderStars.X, layout.HeaderStars.Y, layout.HeaderStars.Scale, 255, 193, 7, true);

        // dividers
        AppendRect(sb, layout.Divider1, 122, 122, 122, 0.55f);
        AppendRect(sb, layout.Divider2, 122, 122, 122, 0.55f);

        // status / countdown / identity
        AppendText(sb, layout.Status.Text.Text, layout.Status.Text.X, layout.Status.Text.Y, layout.Status.Text.Scale,
            layout.Status.Color.R, layout.Status.Color.G, layout.Status.Color.B, layout.Status.Text.Bold);
        var cdColor = wp.Status.Contains("MANHUNT") ? (255, 179, 64) : (layout.Countdown.Color.R, layout.Countdown.Color.G, layout.Countdown.Color.B);
        AppendText(sb, layout.Countdown.Text.Text, layout.Countdown.Text.X, layout.Countdown.Text.Y, layout.Countdown.Text.Scale,
            cdColor.Item1, cdColor.Item2, cdColor.Item3, layout.Countdown.Text.Bold);
        AppendText(sb, layout.Identity.Text.Text, layout.Identity.Text.X, layout.Identity.Text.Y, layout.Identity.Text.Scale,
            layout.Identity.Color.R, layout.Identity.Color.G, layout.Identity.Color.B, layout.Identity.Text.Bold);

        // progress bar
        AppendRect(sb, layout.ProgressTrack, 44, 44, 44, 1f);
        if (layout.ProgressFill.W > 0.001f)
        {
            var fill = wp.Kind == JusticeStatusKind.Manhunt ? (198, 40, 40) : (24, 103, 192);
            AppendRect(sb, layout.ProgressFill, fill.Item1, fill.Item2, fill.Item3, 1f);
        }

        // feed block
        AppendText(sb, layout.FeedLabel.Text, layout.FeedLabel.X, layout.FeedLabel.Y, layout.FeedLabel.Scale, 190, 190, 190, true);
        foreach (var row in layout.FeedRows)
        {
            AppendText(sb, row.Text.Text, row.Text.X, row.Text.Y, row.Text.Scale,
                row.Color.R, row.Color.G, row.Color.B, row.Text.Bold);
        }
    }

    private static void AppendMenuScreen(StringBuilder sb, MenuScreen screen)
    {
        var layout = MenuLayoutEngine.Compute(screen, MeasureApprox);

        // Panel
        AppendRect(sb, layout.Shadow, 0, 0, 0, 0.66f);
        AppendRect(sb, layout.Panel, 30, 30, 30, 0.99f);

        // Header bar
        float left = MenuLayoutEngine.PanelX - MenuLayoutEngine.PanelWidth / 2f;
        float right = MenuLayoutEngine.PanelX + MenuLayoutEngine.PanelWidth / 2f;
        float barCenter = layout.Panel.Y + 0.021f;
        AppendRect(sb, new MenuLayoutEngine.Rect(left + 0.006f, barCenter, MenuLayoutEngine.PanelWidth - 0.012f, 0.042f), 24, 103, 192, 1f);  // rect is centered by AppendRect
        AppendText(sb, layout.HeaderTitle, left + 0.012f, barCenter, 0.30f, 235, 235, 235, true);

        // Rows
        int idx = 0;
        foreach (var row in layout.Rows)
        {
            if (row.Selected)
            {
                AppendRect(sb, new MenuLayoutEngine.Rect(left + 0.006f, row.CenterY, MenuLayoutEngine.PanelWidth - 0.012f, MenuLayoutEngine.RowHeight - 0.005f), 24, 103, 192, 0.35f);
            }
            else if (idx % 2 == 1)
            {
                AppendRect(sb, new MenuLayoutEngine.Rect(left + 0.006f, row.CenterY, MenuLayoutEngine.PanelWidth - 0.012f, MenuLayoutEngine.RowHeight - 0.005f), 44, 44, 44, 0.43f);
            }
            idx++;

            var color = row.Selected ? (235, 235, 235) : (160, 160, 160);
            string label = row.Title + (row.HasSubmenu ? "  >" : "");
            AppendText(sb, label, left + 0.012f, row.CenterY, 0.26f, color.Item1, color.Item2, color.Item3);

            if (!string.IsNullOrEmpty(row.Value))
            {
                var vc = row.Selected ? (76, 175, 80) : (160, 160, 160);
                AppendText(sb, row.Value!, right - 0.016f - 0.10f, row.CenterY, 0.24f, vc.Item1, vc.Item2, vc.Item3);
            }
        }

        // Scroll strip
        if (layout.ScrollUp != null)
            AppendText(sb, layout.ScrollUp, left + 0.020f, layout.RowsEndY + 0.007f, 0.20f, 160, 160, 160);
        if (layout.ScrollDown != null)
            AppendText(sb, layout.ScrollDown, right - 0.016f - 0.085f, layout.RowsEndY + 0.007f, 0.20f, 160, 160, 160);

        // Footer
        AppendRect(sb, layout.FooterBar, 44, 44, 44, 0.92f);
        AppendText(sb, layout.FooterText, left + 0.020f, layout.FooterBar.Y, 0.20f, 160, 160, 160);
    }

    private static void AppendRect(StringBuilder sb, MenuLayoutEngine.Rect r, int rr, int g, int b, float a)
    {
        AppendRect(sb, r.X, r.Y, r.W, r.H, rr, g, b, a);
    }

    private static void AppendRect(StringBuilder sb, HudLayoutEngine.Rect r, int rr, int g, int b, float a)
    {
        AppendRect(sb, r.X, r.Y, r.W, r.H, rr, g, b, a);
    }

    private static void AppendRect(StringBuilder sb, float x, float y, float w, float h, int rr, int g, int b, float a)
    {
        // mirror DRAW_RECT: x/y are CENTER coords → center = X + W/2 (the 2nd-column bug fix)
        float cx = (x + w / 2f) * W, cy = (y + h / 2f) * H;
        sb.AppendLine($"<div class='rect' style='left:{cx.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}px;top:{cy.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}px;width:{w * W}px;height:{h * H}px;background:rgba({rr},{g},{b},{a.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)});'></div>");
    }

    private static void AppendText(StringBuilder sb, string text, float x, float y, float scale, int rr, int g, int b, bool bold = false)
    {
        // GTA SET_TEXT_SCALE: 1.0 ≈ 100px tall at 1080p → scale * 100 * (720/1080) ≈ scale * 66.7px at 720p
        float fontSize = scale * 66.7f;
        string esc = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&#39;");
        sb.AppendLine($"<div class='text' style='left:{x * W}px;top:{y * H}px;font-size:{fontSize.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}px;color:rgb({rr},{g},{b});{(bold ? "font-weight:bold;" : "")}'>" + esc + "</div>");
    }

    /// <summary>Approximate text width (normalized units) — Arial-like metrics at the given scale.
    /// Calibrated: GTA font 4 at scale 0.26 renders ~0.007 normalized per average char
    /// (≈ 9px at 1280px canvas); so avg char ≈ scale × 0.0265.</summary>
    private static float MeasureApprox(string text, float scale, int font)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float units = 0f;
        foreach (char c in text)
        {
            if (c == ' ') units += 0.45f;
            else if (c == 'i' || c == 'l' || c == 'I' || c == '|' || c == '.') units += 0.55f;
            else if (c == 'm' || c == 'w' || c == 'M' || c == 'W' || c == '@') units += 1.25f;
            else units += 0.85f;
        }
        return units * scale * 0.0265f;
    }

    private static List<(string name, object? content)> BuildScreens()
    {
        var screens = new List<(string, object?)>();

        // ── Root (mirrors PowerMenuService — S21 v3: powers grouped) ──
        var powers = new MenuScreen
        {
            Title = "Superpowers",
            SelectedIndex = 0,
            Items = new[]
            {
                new MenuItem { Title = "Dash Teleport [X]", Value = "12.0 m" },
                new MenuItem { Title = "Map Teleport" },
                new MenuItem { Title = "Fly", Value = "OFF" },
                new MenuItem { Title = "Invisible [B]", Value = "OFF" },
                new MenuItem { Title = "God Mode", Value = "OFF" },
                new MenuItem { Title = "Time Stop [Z]" },
            }
        };
        screens.Add(("SUPERPOWERS", powers));

        var root = new MenuScreen
        {
            Title = "CHRONO MENU",
            SelectedIndex = 0,
            Items = new[]
            {
                new MenuItem { Title = "Superpowers", Submenu = powers },
                new MenuItem { Title = "Justice", Submenu = BuildJustice() },
                new MenuItem { Title = "WEBNET News", Submenu = BuildWebnet() },
                new MenuItem { Title = "Settings", Submenu = BuildSettings() },
            }
        };
        screens.Add(("ROOT", root));

        // ── Justice ──
        screens.Add(("JUSTICE", BuildJustice()));

        // ── Settings (mirrors BuildSettingsScreen) ──
        screens.Add(("SETTINGS", BuildSettings()));

        // ── WEBNET (long items — overflow stress test) ──
        screens.Add(("WEBNET", BuildWebnet()));

        // ── Criminal Record (13+ rows — viewport window stress) ──
        screens.Add(("RECORD", BuildRecord()));

        // ── HUD widget states (S21 v2) — the bottom-right card ──
        screens.Add(("WIDGET: FREE", new WidgetPreview(
            Status: "FREE", Countdown: "", Identity: "CLEAN IDENTITY",
            Feed: new[] {
                new HudFeedItem("A civilian recognized you — police dispatched (1★)", FeedKind.Message, "12:01:22"),
            }, JusticeStatusKind.Free, 0f)));
        screens.Add(("WIDGET: WANTED", new WidgetPreview(
            Status: "WANTED 3*", Countdown: "", Identity: "WARRANT ACTIVE — FACE ON FILE",
            Feed: new[] {
                new HudFeedItem("A civilian recognized you — police dispatched (3★)", FeedKind.Message, "12:05:10"),
                new HudFeedItem("POLICE LOSE SUPER-POWERED SUSPECT in Vinewood — chase footage goes viral", FeedKind.Viral, "12:05:44"),
            }, JusticeStatusKind.Wanted, 0f)));
        screens.Add(("WIDGET: CUSTODY", new WidgetPreview(
            Status: "IN CUSTODY — COURT AWAITS", Countdown: "COURT IN 0:34", Identity: "FACE ON FILE (BURNED)",
            Feed: new[] {
                new HudFeedItem("BREAKING: super-powered suspect taken into custody", FeedKind.Webnet, "12:10:02"),
                new HudFeedItem("Bail: $12,000 — press G or face the court", FeedKind.Message, "12:10:05"),
            }, JusticeStatusKind.Captured, 0.55f)));
        screens.Add(("WIDGET: PRISON", new WidgetPreview(
            Status: "PRISON — DAY 3/14", Countdown: "NEXT DAY IN 0:12", Identity: "WARRANT CLEARED",
            Feed: new[] {
                new HudFeedItem("Day 2 of 14 — yard time at dusk", FeedKind.Message, "12:30:00"),
            }, JusticeStatusKind.Prison, 0.4f)));
        screens.Add(("WIDGET: MANHUNT", new WidgetPreview(
            Status: "MANHUNT — PRISON BREAK 4*", Countdown: "HEAT UNTIL DAY 12", Identity: "WARRANT ACTIVE — FACE ON FILE",
            Feed: new[] {
                new HudFeedItem("PRISON BREAK: super-powered inmate escapes Bolingbroke — MANHUNT underway", FeedKind.Viral, "21:30:02"),
                new HudFeedItem("A civilian recognized you — police dispatched (4★)", FeedKind.Message, "21:31:14"),
            }, JusticeStatusKind.Manhunt, 1f, Stars: 4)));

        return screens;
    }

    private static MenuScreen BuildJustice() => new()
    {
        Title = "JUSTICE SYSTEM",
        SelectedIndex = 3,
        Items = new[]
        {
            new MenuItem { Title = "Criminal Record", Submenu = BuildRecord() },
            new MenuItem { Title = "Identity", Value = "CLEAN" },
            new MenuItem { Title = "Warrant", Value = "ACTIVE" },
            new MenuItem { Title = "Clinic — Remove Face" },
            new MenuItem { Title = "Hack Police DB", Value = "1d CD" },
            new MenuItem { Title = "Show HUD", Value = "ON" },
        }
    };

    private static MenuScreen BuildSettings() => new()
    {
        Title = "SETTINGS",
        SelectedIndex = 2,
        Items = new[]
        {
            new MenuItem { Title = "Mod Enabled", Value = "ON" },
            new MenuItem { Title = "Superpowers", Value = "ON" },
            new MenuItem { Title = "Justice System", Value = "ON" },
            new MenuItem { Title = "Hotkeys", Value = "F9 menu | X dash | Z stop | B invis" },
            new MenuItem { Title = "Dash Range", Value = "12.0 m" },
            new MenuItem { Title = "Fly Speed", Value = "3.0" },
            new MenuItem { Title = "Freeze Props", Value = "ON" },
            new MenuItem { Title = "Pause Clock", Value = "OFF" },
            new MenuItem { Title = "Show HUD", Value = "ON" },
            new MenuItem { Title = "Back" },
        }
    };

    private static MenuScreen BuildWebnet() => new()
    {
        Title = "WEBNET NEWS",
        SelectedIndex = 0,
        Items = new[]
        {
            new MenuItem { Title = "BREAKING: super-powered suspect on the loose in Vinewood — witnesses describe a figure moving at impossible speed" },
            new MenuItem { Title = "POLICE LOSE SUPER-POWERED SUSPECT in downtown Los Santos — chase footage goes viral" },
            new MenuItem { Title = "COURT: suspect released on $12,000 bail — charges pending" },
            new MenuItem { Title = "WANTED: masked vigilante spotted near Del Perro — reward offered" },
            new MenuItem { Title = "MANHUNT: escaped prisoner believed to have superhuman abilities" },
            new MenuItem { Title = "WEBNET: city officials deny cover-up as witnesses report glowing figure" },
        }
    };

    private static MenuScreen BuildRecord() => new()
    {
        Title = "CRIMINAL RECORD",
        SelectedIndex = 7,
        Items = new[]
        {
            new MenuItem { Title = "1. Murder", Value = "5★" },
            new MenuItem { Title = "2. Public Offense", Value = "2★" },
            new MenuItem { Title = "3. Brandishing", Value = "1★" },
            new MenuItem { Title = "4. Property Damage", Value = "1★" },
            new MenuItem { Title = "5. Robbery", Value = "3★" },
            new MenuItem { Title = "6. Vehicular Manslaughter", Value = "3★" },
            new MenuItem { Title = "7. Assault", Value = "2★" },
            new MenuItem { Title = "8. Resisting Arrest", Value = "1★" },
            new MenuItem { Title = "9. Parole Violation", Value = "3★" },
            new MenuItem { Title = "10. Theft", Value = "2★" },
            new MenuItem { Title = "11. Vandalism", Value = "1★" },
            new MenuItem { Title = "12. Flight Risk", Value = "4★" },
            new MenuItem { Title = "13. Identity Fraud", Value = "3★" },
            new MenuItem { Title = "14. Obstruction", Value = "1★" },
            new MenuItem { Title = "15. Contempt", Value = "1★" },
        }
    };
}
