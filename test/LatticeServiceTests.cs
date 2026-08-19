using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the Lattice surface brought to full
/// parity with the TS reference, driven through the scripted
/// <see cref="StubHandler"/>.</summary>
public class LatticeServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private const string PatternJson =
        """{"id":"p1","projectId":"proj_1","contactId":"sarah","summary":"orders fridays","metadata":{"patternKind":"day_of_week","contactId":"sarah","confidence":0.8,"observationCount":12,"observationWindowDays":90,"lastObservedAt":"2026-01-01T00:00:00Z","active":true},"active":true,"confidence":0.8,"created":"2026-01-01T00:00:00Z","updated":"2026-01-02T00:00:00Z"}""";

    private const string ExtractResultJson =
        """{"contactsProcessed":3,"patternsCreated":5,"patternsRefreshed":2,"patternsDeactivated":1,"durationMs":42}""";

    // ── mineMemories ──────────────────────────────────────────────────────

    [Fact]
    public async Task MineMemories_posts_extract_route_with_source_memories()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, ExtractResultJson); });
        using var mm = Client(handler);

        var res = await mm.Lattice.MineMemoriesAsync(new Subject("contact", "sarah"), windowDays: 30);

        Assert.Equal(5, res.PatternsCreated);
        Assert.Equal(42, res.DurationMs);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/patterns/extract", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("memories", doc.RootElement.GetProperty("source").GetString());
        Assert.Equal(30, doc.RootElement.GetProperty("windowDays").GetInt32());
        Assert.Equal("sarah", doc.RootElement.GetProperty("subject").GetProperty("externalId").GetString());
    }

    // ── getPattern ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPattern_gets_pattern_by_id_and_parses_metadata()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, PatternJson));
        using var mm = Client(handler);

        var p = await mm.Lattice.GetPatternAsync("p1");

        Assert.Equal("p1", p.Id);
        Assert.Equal("day_of_week", p.Metadata.PatternKind);
        Assert.Equal(12, p.Metadata.ObservationCount);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/lattice/patterns/p1", req.RequestUri!.AbsolutePath);
    }

    // ── listPatterns ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListPatterns_gets_contact_route_with_paging_query()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            $$"""{"data":[{{PatternJson}}],"nextCursor":"c2"}"""));
        using var mm = Client(handler);

        var res = await mm.Lattice.ListPatternsAsync("sarah", activeOnly: false, limit: 25, cursor: "c1");

        Assert.Single(res.Data);
        Assert.Equal("c2", res.NextCursor);
        var req = handler.Requests[0];
        Assert.EndsWith("/lattice/contacts/sarah/patterns", req.RequestUri!.AbsolutePath);
        Assert.Contains("activeOnly=false", req.RequestUri!.Query);
        Assert.Contains("limit=25", req.RequestUri!.Query);
        Assert.Contains("cursor=c1", req.RequestUri!.Query);
    }

    // ── getContext ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetContext_gets_bundle_with_asOf_and_limits()
    {
        var bundle =
            $$"""{"contactId":"sarah","contact":{"id":"sarah","displayName":"Sarah"},"activePatterns":[{{PatternJson}}],"recentEvents":[],"recentMemories":[]}""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, bundle));
        using var mm = Client(handler);

        var res = await mm.Lattice.GetContextAsync("sarah", asOf: "2026-03-01T00:00:00Z",
            eventsLimit: 10, memoriesLimit: 5, graphHops: 2);

        Assert.Equal("sarah", res.ContactId);
        Assert.Equal("Sarah", res.Contact.DisplayName);
        Assert.Single(res.ActivePatterns);
        var req = handler.Requests[0];
        Assert.EndsWith("/lattice/contacts/sarah/context", req.RequestUri!.AbsolutePath);
        Assert.Contains("asOf=2026-03-01", req.RequestUri!.Query);
        Assert.Contains("eventsLimit=10", req.RequestUri!.Query);
        Assert.Contains("graphHops=2", req.RequestUri!.Query);
    }

    // ── runMonitorTick ────────────────────────────────────────────────────

    [Fact]
    public async Task RunMonitorTick_posts_tick_and_parses_result()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            """{"patternsChecked":9,"patternsBroken":1,"breaksEmitted":1,"durationMs":15,"capped":false,"failures":[{"patternId":"p9","error":"boom"}]}"""));
        using var mm = Client(handler);

        var res = await mm.Lattice.RunMonitorTickAsync();

        Assert.Equal(9, res.PatternsChecked);
        Assert.Single(res.Failures);
        Assert.Equal("p9", res.Failures[0].PatternId);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/monitor/tick", req.RequestUri!.AbsolutePath);
    }

    // ── getMonitorStatus ──────────────────────────────────────────────────

    [Fact]
    public async Task GetMonitorStatus_gets_status_with_nullable_last_tick()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            """{"lastTickAt":null,"lastTickDurationMs":null,"patternsDue":4,"activePatternCount":20}"""));
        using var mm = Client(handler);

        var res = await mm.Lattice.GetMonitorStatusAsync();

        Assert.Null(res.LastTickAt);
        Assert.Equal(4, res.PatternsDue);
        Assert.Equal(20, res.ActivePatternCount);
        Assert.EndsWith("/lattice/monitor/status", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    // ── predictTarget ─────────────────────────────────────────────────────

    [Fact]
    public async Task PredictTarget_posts_target_and_returns_target_prediction()
    {
        string body = "";
        var handler = new StubHandler(r =>
        {
            body = BodyOf(r);
            return StubHandler.Json(HttpStatusCode.OK,
                """{"subject":{"kind":"customer","externalId":"acct-42"},"predictions":[],"activePatternCount":0,"generatedAt":"2026-01-01T00:00:00Z","durationMs":7,"targetPrediction":{"targetKind":"event_occurrence","eventType":"subscription_cancelled","probability":0.3,"probabilityLower":0.1,"probabilityUpper":0.5,"value":0,"valueLower":0,"valueUpper":0,"expectedAt":"","expectedAtLower":"","expectedAtUpper":"","daysUntil":0,"anomalyScore":0,"isAnomaly":false,"abstained":false,"abstentionReason":"","explanation":"12 of 40","evidenceMemoryIds":["m1"]}}""");
        });
        using var mm = Client(handler);

        var p = await mm.Lattice.PredictTargetAsync(
            new Subject("customer", "acct-42"),
            new PredictionTarget("event_occurrence", EventType: "subscription_cancelled"),
            horizonDays: 90);

        Assert.False(p.Abstained);
        Assert.Equal(0.3, p.Probability);
        Assert.Equal("subscription_cancelled", p.EventType);
        Assert.Single(p.EvidenceMemoryIds);
        var req = handler.Requests[0];
        Assert.EndsWith("/lattice/predict", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("event_occurrence", doc.RootElement.GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal(90, doc.RootElement.GetProperty("horizonDays").GetInt32());
    }

    [Fact]
    public async Task PredictTarget_synthesizes_abstention_when_engine_returns_none()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            """{"subject":{"kind":"customer","externalId":"acct-42"},"predictions":[],"activePatternCount":0,"generatedAt":"2026-01-01T00:00:00Z","durationMs":7,"abstained":true,"abstentionReason":"not_enough_history"}"""));
        using var mm = Client(handler);

        var p = await mm.Lattice.PredictTargetAsync(
            new Subject("customer", "acct-42"),
            new PredictionTarget("numeric", AttributeKey: "order_total"));

        Assert.True(p.Abstained);
        Assert.Equal("not_enough_history", p.AbstentionReason);
        // eventType falls back to attributeKey when the target had no eventType.
        Assert.Equal("order_total", p.EventType);
    }

    // ── getCohort ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCohort_posts_subject_k_and_parses_members()
    {
        string body = "";
        var handler = new StubHandler(r =>
        {
            body = BodyOf(r);
            return StubHandler.Json(HttpStatusCode.OK,
                """{"target":{"kind":"contact","externalId":"sarah"},"members":[{"subject":{"kind":"contact","externalId":"mike"},"similarity":0.72,"rfmSegment":"loyal","patternKinds":["day_of_week"]}],"candidateCount":50,"generatedAt":"2026-01-01T00:00:00Z","durationMs":11}""");
        });
        using var mm = Client(handler);

        var res = await mm.Lattice.GetCohortAsync(new Subject("contact", "sarah"), k: 10, minSimilarity: 0.5);

        Assert.Single(res.Members);
        Assert.Equal("mike", res.Members[0].Subject.ExternalId);
        Assert.Equal(0.72, res.Members[0].Similarity);
        var req = handler.Requests[0];
        Assert.EndsWith("/lattice/cohort", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(10, doc.RootElement.GetProperty("k").GetInt32());
        Assert.Equal(0.5, doc.RootElement.GetProperty("minSimilarity").GetDouble());
    }

    // ── estimate ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Estimate_posts_estimator_and_parses_result()
    {
        string body = "";
        var handler = new StubHandler(r =>
        {
            body = BodyOf(r);
            return StubHandler.Json(HttpStatusCode.OK,
                """{"subject":{"kind":"patient","externalId":"p-123"},"estimatorId":"phenoage","ok":true,"value":41.2,"unit":"years","contributors":[{"signal":"crp","contribution":1.3}],"confidence":0.9,"provenance":["m1"],"framing":"estimate","disclaimer":"not a diagnosis","missingSignals":[]}""");
        });
        using var mm = Client(handler);

        var res = await mm.Lattice.EstimateAsync(new Subject("patient", "p-123"), "phenoage", persist: true);

        Assert.True(res.Ok);
        Assert.Equal(41.2, res.Value);
        Assert.Equal("years", res.Unit);
        Assert.Single(res.Contributors);
        var req = handler.Requests[0];
        Assert.EndsWith("/lattice/estimate", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("phenoage", doc.RootElement.GetProperty("estimatorId").GetString());
        Assert.True(doc.RootElement.GetProperty("persist").GetBoolean());
    }

    // ── extractPatterns / predict / calibration alignment (renamed surface) ──

    [Fact]
    public async Task ExtractPatterns_posts_extract_route_with_filters()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, ExtractResultJson); });
        using var mm = Client(handler);

        var res = await mm.Lattice.ExtractPatternsAsync(contactId: "sarah", windowDays: 60, force: true);

        Assert.Equal(3, res.ContactsProcessed);
        Assert.EndsWith("/lattice/patterns/extract", handler.Requests[0].RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("sarah", doc.RootElement.GetProperty("contactId").GetString());
        Assert.True(doc.RootElement.GetProperty("force").GetBoolean());
        // extractPatterns must NOT force source=memories (that's mineMemories).
        Assert.False(doc.RootElement.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task Predict_projects_patterns_when_no_target_given()
    {
        string body = "";
        var handler = new StubHandler(r =>
        {
            body = BodyOf(r);
            return StubHandler.Json(HttpStatusCode.OK,
                """{"subject":{"kind":"contact","externalId":"sarah"},"predictions":[{"patternId":"p1","patternKind":"day_of_week","description":"orders friday","expectedAt":"2026-02-06T19:00:00Z","confidence":0.8,"windowMinutes":120,"sourceMemoryIds":["m1"]}],"activePatternCount":1,"generatedAt":"2026-01-01T00:00:00Z","durationMs":9}""");
        });
        using var mm = Client(handler);

        var res = await mm.Lattice.PredictAsync(new Subject("contact", "sarah"), horizonDays: 30);

        Assert.Single(res.Predictions);
        Assert.Equal("day_of_week", res.Predictions[0].PatternKind);
        Assert.Equal(1, res.ActivePatternCount);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(30, doc.RootElement.GetProperty("horizonDays").GetInt32());
        // no target in pattern-projection mode
        Assert.False(doc.RootElement.TryGetProperty("target", out _));
        Assert.EndsWith("/lattice/predict", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetCalibration_gets_report_with_bucket_count_query()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            """{"buckets":[{"lower":0,"upper":0.2,"patterns":3,"predictions":10,"hits":7,"misses":3,"realizedHitRate":0.7,"hasData":true}],"totalPatterns":3,"totalPredictions":10}"""));
        using var mm = Client(handler);

        var res = await mm.Lattice.GetCalibrationAsync(bucketCount: 5);

        Assert.Single(res.Buckets);
        Assert.Equal(0.7, res.Buckets[0].RealizedHitRate);
        Assert.Equal(10, res.TotalPredictions);
        var req = handler.Requests[0];
        Assert.EndsWith("/lattice/calibration", req.RequestUri!.AbsolutePath);
        Assert.Contains("bucketCount=5", req.RequestUri!.Query);
    }

    // ── options thread through ────────────────────────────────────────────

    [Fact]
    public async Task Lattice_methods_honor_RequestOptions_project_override()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            """{"lastTickAt":null,"lastTickDurationMs":null,"patternsDue":0,"activePatternCount":0}"""));
        using var mm = Client(handler);

        await mm.Lattice.GetMonitorStatusAsync(options: new RequestOptions { ProjectId = "other_proj" });

        Assert.Contains("/projects/other_proj/", handler.Requests[0].RequestUri!.AbsolutePath);
    }
}
