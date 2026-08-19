using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the Typed-attributes surface (6/6),
/// mirrored from the TS reference. Confirms every route sits under
/// <c>/memory-typed/*</c>.</summary>
public class TypedServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private const string AttrJson =
        """{"id":"attr1","attributeKey":"credit_score","dataType":"numeric","required":false,"minValid":300,"maxValid":850,"projectId":"proj_1"}""";

    // ── registerAttribute ──────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterAttribute_posts_memory_typed_attributes()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, AttrJson); });
        using var mm = Client(handler);

        var def = await mm.Typed.RegisterAttributeAsync(new RegisterAttributeRequest(
            "credit_score", "numeric", MinValid: 300, MaxValid: 850));

        Assert.Equal("attr1", def.Id);
        Assert.Equal(300, def.MinValid);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/memory-typed/attributes", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("credit_score", doc.RootElement.GetProperty("attributeKey").GetString());
        Assert.Equal(850, doc.RootElement.GetProperty("maxValid").GetDouble());
        // Unset optional fields are omitted.
        Assert.False(doc.RootElement.TryGetProperty("unit", out _));
    }

    // ── listAttributes ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAttributes_gets_memory_typed_attributes_with_params()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, $"[{AttrJson}]"));
        using var mm = Client(handler);

        var defs = await mm.Typed.ListAttributesAsync(attributeKey: "credit_score", limit: 10, offset: 5);

        Assert.Single(defs);
        Assert.Equal("credit_score", defs[0].AttributeKey);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-typed/attributes", req.RequestUri!.AbsolutePath);
        var query = req.RequestUri!.Query;
        Assert.Contains("attributeKey=credit_score", query);
        Assert.Contains("limit=10", query);
        Assert.Contains("offset=5", query);
    }

    // ── ingest (sync) ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Ingest_posts_observations_and_returns_report()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK,
            """{"accepted":1,"quarantined":0,"duplicates":0,"quarantineReasons":{}}"""); });
        using var mm = Client(handler);

        var report = await mm.Typed.IngestAsync([new TypedObservationInput(
            "credit_score", "contact", "sarah", "2026-01-01T00:00:00Z", ValueNumeric: 650)]);

        Assert.Equal(1, report.Accepted);
        Assert.Empty(report.QuarantineReasons);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/memory-typed/observations", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        var obs = doc.RootElement.GetProperty("observations")[0];
        Assert.Equal("credit_score", obs.GetProperty("attributeKey").GetString());
        Assert.Equal(650, obs.GetProperty("valueNumeric").GetDouble());
        // Unset optional value fields are omitted from the observation.
        Assert.False(obs.TryGetProperty("valueText", out _));
    }

    // ── enqueue (async) ────────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_posts_to_observations_enqueue()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, """{"enqueued":3}"""));
        using var mm = Client(handler);

        var res = await mm.Typed.EnqueueAsync([
            new TypedObservationInput("credit_score", "contact", "sarah", "2026-01-01T00:00:00Z", ValueNumeric: 650)]);

        Assert.Equal(3, res.Enqueued);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/memory-typed/observations/enqueue", req.RequestUri!.AbsolutePath);
    }

    // ── queryObservations ──────────────────────────────────────────────────────

    [Fact]
    public async Task QueryObservations_gets_observations_with_filters()
    {
        const string obsJson =
            """[{"id":"o1","attributeKey":"credit_score","subjectKind":"contact","subjectExternalId":"sarah","observedAt":"2026-01-01T00:00:00Z","valueNumeric":650,"status":"accepted"}]""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, obsJson));
        using var mm = Client(handler);

        var obs = await mm.Typed.QueryObservationsAsync(
            subjectKind: "contact", subjectExternalId: "sarah", attributeKey: "credit_score",
            minValue: 600, maxValue: 700, status: "accepted", limit: 20);

        Assert.Single(obs);
        Assert.Equal("accepted", obs[0].Status);
        Assert.Equal(650, obs[0].ValueNumeric);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-typed/observations", req.RequestUri!.AbsolutePath);
        var query = req.RequestUri!.Query;
        Assert.Contains("subjectExternalId=sarah", query);
        // Doubles are formatted invariantly (no locale comma).
        Assert.Contains("minValue=600", query);
        Assert.Contains("maxValue=700", query);
        Assert.Contains("status=accepted", query);
    }

    // ── accumulator ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Accumulator_gets_running_stats()
    {
        const string accJson =
            """{"subjectKind":"contact","subjectExternalId":"sarah","attributeKey":"credit_score","count":1,"sum":650,"sumSq":422500,"cumulative":650,"mean":650}""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, accJson));
        using var mm = Client(handler);

        var acc = await mm.Typed.AccumulatorAsync("contact", "sarah", "credit_score");

        Assert.Equal(1, acc.Count);
        Assert.Equal(650, acc.Mean);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-typed/accumulator", req.RequestUri!.AbsolutePath);
        var query = req.RequestUri!.Query;
        Assert.Contains("subjectKind=contact", query);
        Assert.Contains("subjectExternalId=sarah", query);
        Assert.Contains("attributeKey=credit_score", query);
    }
}
