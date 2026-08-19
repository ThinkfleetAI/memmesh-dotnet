using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Coverage for the Consent surface, which has no dedicated endpoints —
/// it is implemented client-side over the admin memory CRUD (list / create /
/// delete). These tests drive the scripted <see cref="StubHandler"/> to assert
/// the multi-request choreography matches the TS reference.</summary>
public class ConsentServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private static string ConsentItem(string id, string kind, string externalId, bool optedOut,
        string? reason, string created) =>
        $$"""
        {"id":"{{id}}","type":"consent","content":"[consent] {{kind}}:{{externalId}}","importance":10,"scope":"project","status":"confirmed","confidence":1,"supersededById":null,"metadata":{"subject":{"kind":"{{kind}}","externalId":"{{externalId}}"},"optedOut":{{(optedOut ? "true" : "false")}},"optedOutAt":{{(optedOut ? $"\"{created}\"" : "null")}},"reason":{{(reason is null ? "null" : $"\"{reason}\"")}},"recordKind":"consent"},"created":"{{created}}"}
        """;

    // ── optOut, no prior record ──────────────────────────────────────────────

    [Fact]
    public async Task OptOut_with_no_prior_lists_then_creates_a_consent_memory()
    {
        string createBody = "";
        var handler = new StubHandler(
            // 1) findActiveConsent → GET admin/memory?limit=500 (nothing yet)
            _ => StubHandler.Json(HttpStatusCode.OK, "[]"),
            // 2) createMemory → POST admin/memory
            r => { createBody = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK,
                ConsentItem("new1", "contact", "sarah", optedOut: true, reason: "gdpr", "2026-05-25T00:00:00.000Z")); });
        using var mm = Client(handler);

        var status = await mm.Consent.OptOutAsync(new Subject("contact", "sarah"), reason: "gdpr");

        // No prior row → no DELETE; exactly a GET (list) then a POST (create).
        Assert.Equal(2, handler.Calls);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Contains("limit=500", handler.Requests[0].RequestUri!.Query);
        Assert.EndsWith("/admin/memory", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("/admin/memory", handler.Requests[1].RequestUri!.AbsolutePath);

        // The created consent memory carries the right type / metadata shape.
        using var doc = JsonDocument.Parse(createBody);
        var root = doc.RootElement;
        Assert.Equal("consent", root.GetProperty("type").GetString());
        Assert.Equal("consent", root.GetProperty("category").GetString());
        Assert.Equal(10, root.GetProperty("importance").GetInt32());
        Assert.Equal("[consent] contact:sarah opted out", root.GetProperty("content").GetString());
        var md = root.GetProperty("metadata");
        Assert.True(md.GetProperty("optedOut").GetBoolean());
        Assert.Equal("gdpr", md.GetProperty("reason").GetString());
        Assert.Equal("sarah", md.GetProperty("subject").GetProperty("externalId").GetString());

        // Returned status echoes the write and links the new memory id.
        Assert.True(status.OptedOut);
        Assert.Equal("gdpr", status.Reason);
        Assert.Equal("new1", status.MemoryId);
        Assert.NotNull(status.OptedOutAt);
    }

    // ── optIn supersedes a prior record ──────────────────────────────────────

    [Fact]
    public async Task OptIn_supersedes_prior_record_by_deleting_it_first()
    {
        var prior = ConsentItem("old1", "contact", "sarah", optedOut: true, reason: "gdpr", "2026-01-01T00:00:00Z");
        string createBody = "";
        var handler = new StubHandler(
            // 1) findActiveConsent → prior opt-out exists
            _ => StubHandler.Json(HttpStatusCode.OK, $"[{prior}]"),
            // 2) supersede → DELETE admin/memory/old1
            _ => StubHandler.Json(HttpStatusCode.OK, """{"success":true}"""),
            // 3) createMemory → POST admin/memory
            r => { createBody = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK,
                ConsentItem("new1", "contact", "sarah", optedOut: false, reason: null, "2026-05-25T00:00:00.000Z")); });
        using var mm = Client(handler);

        var status = await mm.Consent.OptInAsync(new Subject("contact", "sarah"));

        Assert.Equal(3, handler.Calls);
        // The prior row is hard-deleted before the new one is written.
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.EndsWith("/admin/memory/old1", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, handler.Requests[2].Method);

        // opt-in clears optedOut / optedOutAt / reason on the new record.
        using var doc = JsonDocument.Parse(createBody);
        var md = doc.RootElement.GetProperty("metadata");
        Assert.False(md.GetProperty("optedOut").GetBoolean());
        Assert.Equal(JsonValueKind.Null, md.GetProperty("optedOutAt").ValueKind);
        Assert.Equal("[consent] contact:sarah opted in", doc.RootElement.GetProperty("content").GetString());

        Assert.False(status.OptedOut);
        Assert.Null(status.OptedOutAt);
        Assert.Null(status.Reason);
        Assert.Equal("new1", status.MemoryId);
    }

    // ── getStatus: newest matching record wins; default when none ────────────

    [Fact]
    public async Task GetStatus_picks_newest_matching_record_and_defaults_when_none()
    {
        var older = ConsentItem("m-old", "contact", "sarah", optedOut: false, reason: null, "2026-01-01T00:00:00Z");
        var newer = ConsentItem("m-new", "contact", "sarah", optedOut: true, reason: "gdpr", "2026-06-01T00:00:00Z");
        var otherSubject = ConsentItem("m-x", "contact", "mike", optedOut: true, reason: "n/a", "2026-07-01T00:00:00Z");
        // Intentionally out of order; the service must sort by `created` desc.
        var listBody = $"[{older},{otherSubject},{newer}]";

        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, listBody));
        using var mm = Client(handler);

        var status = await mm.Consent.GetStatusAsync(new Subject("contact", "sarah"));

        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.True(status.OptedOut);
        Assert.Equal("gdpr", status.Reason);
        Assert.Equal("m-new", status.MemoryId);      // newest of the two sarah rows
        Assert.NotNull(status.OptedOutAt);

        // No matching record → default (opted-in) status, memoryId null.
        var handler2 = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, "[]"));
        using var mm2 = Client(handler2);
        var missing = await mm2.Consent.GetStatusAsync(new Subject("contact", "nobody"));
        Assert.False(missing.OptedOut);
        Assert.Null(missing.MemoryId);
        Assert.Null(missing.OptedOutAt);
        Assert.Null(missing.Reason);
    }
}
