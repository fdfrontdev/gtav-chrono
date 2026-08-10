using System;
using System.Collections.Generic;
using System.Linq;

namespace Chrono.Application;

/// <summary>
/// S21 v2 — in-widget message feed (user UAT: "all the messages on the left
/// should move inside the widget on the right; WEBNET should live-stream into
/// it"). Ring buffer shared by the notifier (vanilla bottom-left ticker replaced)
/// and the media service (WEBNET headlines) — the HUD widget renders the tail.
/// </summary>
public sealed class HudFeedBuffer
{
    public const int MaxItems = 5;

    private readonly Queue<HudFeedItem> _items = new();

    /// <summary>All live items, oldest → newest.</summary>
    public IReadOnlyList<HudFeedItem> Items => _items.ToList();

    /// <summary>S22: immutable snapshot for the renderer (same items as <see cref="Items"/>).</summary>
    public IReadOnlyList<HudFeedItem> Snapshot() => _items.ToList();

    public void Push(string text, FeedKind kind = FeedKind.Message)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _items.Enqueue(new HudFeedItem(text, kind, DateTime.Now.ToString("HH:mm:ss")));
        while (_items.Count > MaxItems) _items.Dequeue();
    }

    public void Clear() => _items.Clear();
}

public enum FeedKind { Message, Webnet, Viral }

public sealed record HudFeedItem(string Text, FeedKind Kind, string When);
