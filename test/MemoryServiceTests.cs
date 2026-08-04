using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the memory-resource methods added in the
/// Phase 2 parity pass, driven through the scripted <see cref="StubHandler"/>.</summary>
public class MemoryServiceTests
{
    private const string ItemJson =
        """{"id":"m1","type":"fact","content":"hi","importance":5,"scope":"project","status":"active","confidence":0.9,"supersededById":null}""";

    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    // Read the request body inside the handler step — StubHandler buffers it
    // before calling the step, but the client disposes the request (and its
    // content) once the send returns, so it must be captured in-flight.
    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    // ── Observe: raw text (primary) vs verbatim content (legacy) ──────────

    [Fact]
    public async Task Observe_text_posts_raw_turn_to_engine_observe_pipeline()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK,
            $$"""{"saved":[{{ItemJson}}],"candidateCount":3}"""); });
        using var mm = Client(handler);

        var res = await mm.Memory.ObserveAsync(text: "Sarah prefers email over phone.",
            occurredAt: "2026-01-02T00:00:00Z");

        Assert.Single(res.Saved);
        Assert.Equal("m1", res.Saved[0].Id);
        Assert.Equal(3, res.CandidateCount);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        // Project-scoped observe pipeline, NOT the verbatim admin-create route.
        Assert.EndsWith("/memory/observe", req.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("/admin/", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Sarah prefers email over phone.", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
        Assert.Equal("2026-01-02T00:00:00Z", doc.RootElement.GetProperty("occurredAt").GetString());
    }

    [Fact]
    public async Task Observe_filler_text_returns_empty_saved_as_success()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            """{"saved":[],"candidateCount":0}"""));
        using var mm = Client(handler);

        var res = await mm.Memory.ObserveAsync(text: "ok thanks!");

        Assert.Empty(res.Saved);
        Assert.Equal(0, res.CandidateCount);
    }

    [Fact]
    public async Task Observe_content_falls_back_to_verbatim_admin_create_wrapped()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, ItemJson); });
        using var mm = Client(handler);

        var res = await mm.Memory.ObserveAsync(content: "Ordered large pepperoni pizza",
            subject: new Subject("contact", "sarah"), activityType: "pizza_order",
            occurredAt: "2026-01-03T00:00:00Z");

        Assert.Single(res.Saved);
        Assert.Equal("m1", res.Saved[0].Id);
        Assert.Equal(1, res.CandidateCount);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/admin/memory", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Ordered large pepperoni pizza", doc.RootElement.GetProperty("content").GetString());
        Assert.Equal("2026-01-03T00:00:00Z", doc.RootElement.GetProperty("validFrom").GetString());
        Assert.Equal("pizza_order", doc.RootElement.GetProperty("metadata").GetProperty("eventType").GetString());
        Assert.Equal("sarah",
            doc.RootElement.GetProperty("metadata").GetProperty("subject").GetProperty("externalId").GetString());
    }

    [Fact]
    public async Task Observe_with_neither_text_nor_content_throws()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, ItemJson));
        using var mm = Client(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => mm.Memory.ObserveAsync());
        Assert.Equal(0, handler.Calls); // never hit the network
    }

    // ── Multimodal attachments ────────────────────────────────────────────

    [Fact]
    public async Task ObserveImage_posts_base64_to_attachments()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, ItemJson); });
        using var mm = Client(handler);

        var item = await mm.Memory.ObserveImageAsync(
            new Subject("contact", "sarah"), new byte[] { 1, 2, 3 }, "image/png",
            fileName: "r.png", content: "receipt", activityType: "receipt_capture", importance: 7);

        Assert.Equal("m1", item.Id);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/memory/attachments", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), root.GetProperty("dataBase64").GetString());
        Assert.Equal("image/png", root.GetProperty("mimeType").GetString());
        Assert.Equal("r.png", root.GetProperty("fileName").GetString());
        Assert.Equal("receipt", root.GetProperty("content").GetString());
        // activityType stays top-level for attachments (unlike ObserveAsync).
        Assert.Equal("receipt_capture", root.GetProperty("activityType").GetString());
        Assert.Equal(7, root.GetProperty("importance").GetInt32());
        Assert.Equal("sarah", root.GetProperty("subject").GetProperty("externalId").GetString());
    }

    [Fact]
    public async Task ObserveVoice_and_Document_hit_attachments_with_their_bytes()
    {
        var bodies = new List<string>();
        var handler = new StubHandler(r => { bodies.Add(BodyOf(r)); return StubHandler.Json(HttpStatusCode.OK, ItemJson); });
        using var mm = Client(handler);

        await mm.Memory.ObserveVoiceAsync(new Subject("user", "ryan"), new byte[] { 9 }, "audio/mpeg");
        await mm.Memory.ObserveDocumentAsync(new Subject("project", "acme"), new byte[] { 8, 8 }, "application/pdf");

        Assert.EndsWith("/memory/attachments", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.EndsWith("/memory/attachments", handler.Requests[1].RequestUri!.AbsolutePath);
        using var voice = JsonDocument.Parse(bodies[0]);
        Assert.Equal(Convert.ToBase64String(new byte[] { 9 }), voice.RootElement.GetProperty("dataBase64").GetString());
        Assert.Equal("audio/mpeg", voice.RootElement.GetProperty("mimeType").GetString());
        using var docBody = JsonDocument.Parse(bodies[1]);
        Assert.Equal(Convert.ToBase64String(new byte[] { 8, 8 }), docBody.RootElement.GetProperty("dataBase64").GetString());
    }

    // ── Mine ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mine_gets_user_scoped_route_with_paging()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, $"[{ItemJson}]"));
        using var mm = Client(handler);

        var mine = await mm.Memory.MineAsync(limit: 20, offset: 40);

        Assert.Single(mine);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory/mine", req.RequestUri!.AbsolutePath);
        Assert.Contains("limit=20", req.RequestUri!.Query);
        Assert.Contains("offset=40", req.RequestUri!.Query);
    }

    // ── Explain ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Explain_resolves_source_memory_ids_from_metadata()
    {
        var pattern =
            """{"id":"p1","type":"behavior_pattern","content":"orders friday","importance":6,"scope":"project","status":"active","confidence":0.8,"supersededById":null,"metadata":{"sourceMemoryIds":["s1","s2"]}}""";
        var src = """{"id":"s1","type":"event","content":"order","importance":5,"scope":"project","status":"active","confidence":0.9,"supersededById":null}""";
        var handler = new StubHandler(
            _ => StubHandler.Json(HttpStatusCode.OK, pattern),                 // get pattern
            r => StubHandler.Json(HttpStatusCode.OK, src),                     // get s1
            r => StubHandler.Json(HttpStatusCode.NotFound, "{}"));             // s2 gone → skipped
        using var mm = Client(handler);

        var result = await mm.Memory.ExplainAsync("p1");

        Assert.Equal("p1", result.Memory.Id);
        Assert.Single(result.SourceMemories); // s2 dropped, not fatal
        Assert.Equal("s1", result.SourceMemories[0].Id);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Explain_returns_empty_sources_when_no_provenance()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, ItemJson));
        using var mm = Client(handler);

        var result = await mm.Memory.ExplainAsync("m1");

        Assert.Empty(result.SourceMemories);
        Assert.Equal(1, handler.Calls); // no source lookups fired
    }

    // ── ListAll (offset walk) ─────────────────────────────────────────────

    [Fact]
    public async Task ListAll_walks_offset_pages_until_short_page()
    {
        // First page full (limit 2), second page short → stop.
        var full = $"[{ItemJson},{ItemJson}]";
        var handler = new StubHandler(
            _ => StubHandler.Json(HttpStatusCode.OK, full),
            _ => StubHandler.Json(HttpStatusCode.OK, $"[{ItemJson}]"));
        using var mm = Client(handler);

        var all = new List<MemoryItem>();
        await foreach (var m in mm.Memory.ListAllAsync(type: "event", limit: 2))
            all.Add(m);

        Assert.Equal(3, all.Count);
        Assert.Equal(2, handler.Calls);
        Assert.Contains("offset=0", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("offset=2", handler.Requests[1].RequestUri!.Query);
        Assert.Contains("type=event", handler.Requests[0].RequestUri!.Query);
    }

    // ── ListPlatform ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListPlatform_hits_platform_route()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, $"[{ItemJson}]"));
        using var mm = Client(handler);

        await mm.Memory.ListPlatformAsync(status: "confirmed", limit: 5);

        var req = handler.Requests[0];
        Assert.EndsWith("/admin/memory/platform", req.RequestUri!.AbsolutePath);
        Assert.Contains("status=confirmed", req.RequestUri!.Query);
    }

    // ── Consolidate ───────────────────────────────────────────────────────

    [Fact]
    public async Task Consolidate_posts_subject_and_window_and_parses_result()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK,
            """{"subjectsConsidered":3,"observationsCreated":2,"observationsUpdated":1,"observationsSuperseded":0,"durationMs":42}"""); });
        using var mm = Client(handler);

        var res = await mm.Memory.ConsolidateAsync(new Subject("contact", "sarah"), windowDays: 30);

        Assert.Equal(3, res.SubjectsConsidered);
        Assert.Equal(2, res.ObservationsCreated);
        Assert.Equal(42, res.DurationMs);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/admin/memory/llm-consolidate", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(30, doc.RootElement.GetProperty("windowDays").GetInt32());
        Assert.Equal("sarah", doc.RootElement.GetProperty("subject").GetProperty("externalId").GetString());
    }

    // ── Backfill embeddings ───────────────────────────────────────────────

    [Fact]
    public async Task BackfillEmbeddings_posts_batch_and_parses_embedded()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, """{"embedded":128}"""); });
        using var mm = Client(handler);

        var res = await mm.Memory.BackfillEmbeddingsAsync(batch: 500);

        Assert.Equal(128, res.Embedded);
        var req = handler.Requests[0];
        Assert.EndsWith("/admin/memory/embeddings/backfill", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(500, doc.RootElement.GetProperty("batch").GetInt32());
    }

    // ── ListFeedback ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListFeedback_gets_per_memory_feedback()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            """[{"id":"f1","memoryId":"m1","responseId":null,"rating":"negative","comment":"wrong","createdByUserId":"u1","created":"2026-01-01T00:00:00Z"}]"""));
        using var mm = Client(handler);

        var fb = await mm.Memory.ListFeedbackAsync("m1");

        Assert.Single(fb);
        Assert.Equal("negative", fb[0].Rating);
        Assert.Equal("m1", fb[0].MemoryId);
        Assert.EndsWith("/admin/memory/m1/feedback", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    // ── User-scoped delete ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMine_hits_user_scoped_route_not_admin()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, ""));
        using var mm = Client(handler);

        await mm.Memory.DeleteMineAsync("m1");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Delete, req.Method);
        // user-level DELETE /memory/{id}, distinct from admin DELETE /admin/memory/{id}
        Assert.EndsWith("/memory/m1", req.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("/admin/", req.RequestUri!.AbsolutePath);
    }

    // ── Per-call options thread through ───────────────────────────────────

    [Fact]
    public async Task New_methods_honor_RequestOptions_project_override()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, $"[{ItemJson}]"));
        using var mm = Client(handler);

        await mm.Memory.MineAsync(options: new RequestOptions { ProjectId = "other_proj" });

        Assert.Contains("/projects/other_proj/", handler.Requests[0].RequestUri!.AbsolutePath);
    }
}
