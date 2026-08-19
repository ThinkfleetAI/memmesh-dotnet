namespace MemMesh;

/// <summary>Events — the durable memory event log. The engine emits events on
/// interesting state changes (pattern emergence, risk firing, segment shift,
/// consent change); consumers walk the log with <see cref="PollAsync"/> (reads
/// <c>/memory-events</c>) or subscribe to a background poll loop with
/// <see cref="Subscribe"/>. <see cref="EmitAsync"/> is the write side, appending
/// to the log (<c>/lattice/events/emit</c>) and firing matching alert rules
/// synchronously.
///
/// Mirrors the TS reference surface (<c>tf.events.*</c>).
/// <code>
/// using var sub = mm.Events.Subscribe(async e =>
///     Console.WriteLine($"[{e.Severity}] {e.EventType}"),
///     eventTypes: ["risk.fired", "segment.changed"]);
/// // ... later: sub.Dispose() stops the loop.
/// </code></summary>
public sealed class EventsService(MemMeshClient c)
{
    /// <summary>Pull events newer than <paramref name="since"/> (an ISO timestamp;
    /// use the last event's <c>OccurredAt</c> as the next cursor). <paramref name="limit"/>
    /// defaults to 100, max 1000; <paramref name="eventTypes"/> restricts to specific
    /// types.</summary>
    public async Task<List<MemoryEvent>> PollAsync(string? since = null, int? limit = null,
        IEnumerable<string>? eventTypes = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (since is not null) q.Add($"since={Uri.EscapeDataString(since)}");
        if (limit is not null) q.Add($"limit={limit}");
        var types = eventTypes?.ToList();
        if (types is { Count: > 0 }) q.Add($"eventTypes={Uri.EscapeDataString(string.Join(",", types))}");
        var path = "memory-events" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return await c.Send<List<MemoryEvent>>(HttpMethod.Get, path, null, options, ct).ConfigureAwait(false);
    }

    /// <summary>Append an event to the durable log. Matching alert rules fire
    /// synchronously; the write-side counterpart to <see cref="PollAsync"/>.</summary>
    public Task<EmitEventResult> EmitAsync(EmitEventRequest body, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<EmitEventResult>(HttpMethod.Post, "lattice/events/emit", body, options, ct);

    /// <summary>Convenience: poll in the background and invoke <paramref name="handler"/>
    /// for each event, threading the cursor forward automatically. Handler exceptions
    /// and transient network errors are swallowed so the loop survives. The interval
    /// defaults to 5s and is floored at 500ms. Returns an
    /// <see cref="EventSubscription"/> — <c>Dispose()</c> it to stop the loop.</summary>
    public EventSubscription Subscribe(Func<MemoryEvent, Task> handler, string? since = null,
        int? limit = null, IEnumerable<string>? eventTypes = null, TimeSpan? interval = null,
        RequestOptions? options = null)
    {
        var ms = interval?.TotalMilliseconds ?? 5000;
        var effective = TimeSpan.FromMilliseconds(Math.Max(500, ms));
        return new EventSubscription(this, handler, since, limit, eventTypes?.ToList(), effective, options);
    }
}

/// <summary>A running background poll loop started by
/// <see cref="EventsService.Subscribe"/>. Owns a <see cref="CancellationTokenSource"/>
/// and a background <see cref="Task"/>; <see cref="Dispose"/> cancels the loop (idempotent).
/// Await <see cref="Completion"/> after disposing to join the loop — mainly for tests.</summary>
public sealed class EventSubscription : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    internal EventSubscription(EventsService events, Func<MemoryEvent, Task> handler, string? since,
        int? limit, IReadOnlyList<string>? eventTypes, TimeSpan interval, RequestOptions? options)
        => _loop = RunAsync(events, handler, since, limit, eventTypes, interval, options);

    private async Task RunAsync(EventsService events, Func<MemoryEvent, Task> handler, string? cursor,
        int? limit, IReadOnlyList<string>? eventTypes, TimeSpan interval, RequestOptions? options)
    {
        var ct = _cts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var batch = await events.PollAsync(cursor, limit, eventTypes, options, ct).ConfigureAwait(false);
                    foreach (var e in batch)
                    {
                        if (ct.IsCancellationRequested) break;
                        try { await handler(e).ConfigureAwait(false); }
                        catch { /* handler errors are the caller's concern; don't kill the loop */ }
                        cursor = e.OccurredAt;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch { /* network blip — back off briefly and retry */ }

                try { await Task.Delay(interval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally { _cts.Dispose(); }
    }

    /// <summary>Completes once the background poll loop has stopped.</summary>
    public Task Completion => _loop;

    /// <summary>Stop polling. Idempotent.</summary>
    public void Dispose()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { /* already stopped */ }
    }
}
