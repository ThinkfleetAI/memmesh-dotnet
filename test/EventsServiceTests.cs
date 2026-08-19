using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the Events surface (3/3), mirrored from
/// the TS reference. Confirms emit posts to <c>/lattice/events/emit</c> and poll
/// reads from <c>/memory-events</c>, plus a subscribe start/stop lifecycle.</summary>
public class EventsServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private const string EventJson =
        """{"id":"ev1","eventType":"risk.fired","subject":{"kind":"contact","externalId":"sarah"},"severity":"warn","payload":{"riskKind":"churn"},"sourceMemoryIds":["m1"],"sourcePatternId":"p1","emittedByPack":"risk-pack","occurredAt":"2026-01-01T00:00:00Z"}""";

    // ── emit ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Emit_posts_to_lattice_events_emit()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK,
            """{"emitted":true,"event":{"id":"ev1","eventType":"cart.abandoned","severity":"warn","occurredAt":"2026-01-01T00:00:00Z"},"alertDispatches":2}"""); });
        using var mm = Client(handler);

        var res = await mm.Events.EmitAsync(new EmitEventRequest(
            EventType: "cart.abandoned",
            Subject: new Subject("contact", "sarah"),
            Severity: "warn",
            PayloadJson: """{"cartValue":84}"""));

        Assert.True(res.Emitted);
        Assert.Equal("ev1", res.Event!.Id);
        Assert.Equal(2, res.AlertDispatches);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/events/emit", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("cart.abandoned", doc.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("sarah", doc.RootElement.GetProperty("subject").GetProperty("externalId").GetString());
        // Unset optional fields are omitted.
        Assert.False(doc.RootElement.TryGetProperty("sourcePatternId", out _));
    }

    // ── poll ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Poll_gets_memory_events_with_query_params()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, $"[{EventJson}]"));
        using var mm = Client(handler);

        var events = await mm.Events.PollAsync(
            since: "2026-01-01T00:00:00Z", limit: 50, eventTypes: ["risk.fired", "segment.changed"]);

        Assert.Single(events);
        Assert.Equal("ev1", events[0].Id);
        Assert.Equal("sarah", events[0].Subject!.ExternalId);
        Assert.Equal("churn", events[0].Payload["riskKind"].GetString());
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-events", req.RequestUri!.AbsolutePath);
        var query = req.RequestUri!.Query;
        Assert.Contains("since=", query);
        Assert.Contains("limit=50", query);
        // eventTypes are comma-joined (comma URL-encoded as %2C).
        Assert.Contains("eventTypes=risk.fired%2Csegment.changed", query);
    }

    [Fact]
    public async Task Poll_without_params_hits_bare_route()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, "[]"));
        using var mm = Client(handler);

        var events = await mm.Events.PollAsync();

        Assert.Empty(events);
        Assert.EndsWith("/memory-events", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("", handler.Requests[0].RequestUri!.Query);
    }

    // ── subscribe (start / stop lifecycle) ─────────────────────────────────────

    [Fact]
    public async Task Subscribe_polls_delivers_events_then_stops_on_dispose()
    {
        // First poll returns one event, subsequent polls return empty.
        var handler = new StubHandler(
            _ => StubHandler.Json(HttpStatusCode.OK, $"[{EventJson}]"),
            _ => StubHandler.Json(HttpStatusCode.OK, "[]"));
        using var mm = Client(handler);

        var received = new List<MemoryEvent>();
        var gotOne = new TaskCompletionSource();
        var sub = mm.Events.Subscribe(async e =>
        {
            received.Add(e);
            gotOne.TrySetResult();
            await Task.CompletedTask;
        }, interval: TimeSpan.FromMilliseconds(500));

        // Wait until the handler saw the first event, then stop.
        await gotOne.Task.WaitAsync(TimeSpan.FromSeconds(5));
        sub.Dispose();
        await sub.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(received);
        Assert.Equal("ev1", received[0].Id);
        // The loop polled the canonical route and stopped cleanly on dispose.
        Assert.All(handler.Requests, r => Assert.EndsWith("/memory-events", r.RequestUri!.AbsolutePath));
        Assert.True(sub.Completion.IsCompleted);
    }

    [Fact]
    public void Subscribe_dispose_is_idempotent()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, "[]"));
        using var mm = Client(handler);

        var sub = mm.Events.Subscribe(_ => Task.CompletedTask, interval: TimeSpan.FromMilliseconds(500));
        sub.Dispose();
        sub.Dispose(); // must not throw
    }
}
