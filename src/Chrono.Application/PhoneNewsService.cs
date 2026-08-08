using System;
using System.Collections.Generic;
using Chrono.Application.Ports;

namespace Chrono.Application;

/// <summary>One social-media post in the WEBNET feed (phone, S7).</summary>
public sealed record NewsFeedItem(string Text, string When, bool Viral);

/// <summary>Phone-style social feed overlay (boundary draws the panel).</summary>
public interface IPhoneOverlay
{
    void ShowFeed(IReadOnlyList<NewsFeedItem> items);
    void Hide();
}

/// <summary>
/// WEBNET phone feed (S7): open the phone (Up) → latest viral/news posts from the
/// media service; toggle or Esc closes. The real phone keeps working — the feed is
/// drawn over its screen area while it's open.
/// </summary>
public sealed class PhoneNewsService
{
    private readonly IGameInput _input;
    private readonly Func<IReadOnlyList<NewsFeedItem>> _feedProvider;
    private readonly IPhoneOverlay _overlay;
    private bool _open;

    public PhoneNewsService(IGameInput input, Func<IReadOnlyList<NewsFeedItem>> feedProvider, IPhoneOverlay overlay)
    {
        _input = input;
        _feedProvider = feedProvider;
        _overlay = overlay;
    }

    public bool IsOpen => _open;

    public void Tick()
    {
        if (_input.IsPhoneKeyJustPressed)
        {
            _open = !_open;
            if (_open) _overlay.ShowFeed(_feedProvider());
            else _overlay.Hide();
            return;
        }

        if (!_open) return;

        if (_input.IsMenuCancelJustPressed)   // Esc closes like the real phone
        {
            _open = false;
            _overlay.Hide();
            return;
        }

        _overlay.ShowFeed(_feedProvider());   // live refresh while open
    }
}
