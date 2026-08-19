using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the Compliance surface (7/7), mirrored
/// from thinkfleet-memory-sdk/src/resources/compliance.ts. Confirms the canonical
/// routes under <c>/memory-compliance/*</c> and <c>/memory-compliance-packs</c>
/// (export, hard-delete [NOT erase], audit, packs, project packs).</summary>
public class ComplianceServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    // ── exportSubject ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportSubject_posts_memory_compliance_export()
    {
        const string json =
            """{"subject":{"kind":"contact","externalId":"sarah"},"export":{"subject":{"kind":"contact","externalId":"sarah"},"memories":[{"id":"m1"}],"patterns":[],"observations":[],"events":[],"alert_fires":[],"generated_at":"2026-05-25T00:00:00Z"},"counts":{"memories":1,"patterns":0,"observations":0,"events":0,"alertFires":0},"generatedAt":"2026-05-25T00:00:00Z","durationMs":12.5}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, json); });
        using var mm = Client(handler);

        var res = await mm.Compliance.ExportSubjectAsync(new Subject("contact", "sarah"));

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/memory-compliance/export", req.RequestUri!.AbsolutePath);
        Assert.Equal("sarah", JsonDocument.Parse(body).RootElement.GetProperty("subject").GetProperty("externalId").GetString());
        Assert.Equal(1, res.Counts.Memories);
        Assert.Equal(12.5, res.DurationMs);
        Assert.NotNull(res.Export);
        Assert.Single(res.Export!.Memories);
    }

    // ── hardDeleteSubject ───────────────────────────────────────────────────────

    [Fact]
    public async Task HardDeleteSubject_posts_memory_compliance_hard_delete()
    {
        const string json =
            """{"subject":{"kind":"contact","externalId":"sarah"},"memoriesDeleted":3,"patternsDeleted":0,"observationsDeleted":1,"eventsDeleted":2,"alertFiresDeleted":0,"dryRun":true,"auditEventId":"a1","generatedAt":"2026-05-25T00:00:00Z","durationMs":7}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, json); });
        using var mm = Client(handler);

        var res = await mm.Compliance.HardDeleteSubjectAsync(
            new Subject("contact", "sarah"), reason: "GDPR Art. 17 case A", dryRun: true);

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        // Route is hard-delete, NOT erase.
        Assert.EndsWith("/memory-compliance/hard-delete", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("GDPR Art. 17 case A", doc.RootElement.GetProperty("reason").GetString());
        Assert.True(doc.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal(3, res.MemoriesDeleted);
        Assert.True(res.DryRun);
        Assert.Equal("a1", res.AuditEventId);
    }

    // ── listAuditEvents ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAuditEvents_gets_memory_compliance_audit_with_filters()
    {
        const string json =
            """[{"id":"e1","created":"2026-05-01T00:00:00Z","actor":"svc","eventType":"read.export","query":null,"memoryIds":null,"resultCount":3,"metadata":{"k":"v"}}]""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, json));
        using var mm = Client(handler);

        var events = await mm.Compliance.ListAuditEventsAsync(
            subject: new Subject("contact", "sarah"), actor: "svc",
            eventTypes: ["read.context", "read.export"], since: "2026-05-01T00:00:00Z", limit: 50);

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-compliance/audit", req.RequestUri!.AbsolutePath);
        var query = req.RequestUri!.Query;
        Assert.Contains("subjectKind=contact", query);
        Assert.Contains("subjectExternalId=sarah", query);
        Assert.Contains("actor=svc", query);
        Assert.Contains("since=", query);
        Assert.Contains("limit=50", query);
        // eventTypes are joined with a comma (URL-encoded).
        Assert.Contains("eventTypes=read.context%2Cread.export", query);
        Assert.Single(events);
        Assert.Equal("read.export", events[0].EventType);
        Assert.Equal(3, events[0].ResultCount);
    }

    // ── listPacks ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListPacks_gets_memory_compliance_packs()
    {
        const string json =
            """[{"id":"hipaa","version":"1.0","description":"HIPAA","ownsClasses":["phi"],"regulatoryTags":["HIPAA"]}]""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, json));
        using var mm = Client(handler);

        var packs = await mm.Compliance.ListPacksAsync();

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-compliance/packs", req.RequestUri!.AbsolutePath);
        Assert.Single(packs);
        Assert.Equal("hipaa", packs[0].Id);
        Assert.Equal("phi", packs[0].OwnsClasses[0]);
    }

    // ── listProjectPacks ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListProjectPacks_gets_memory_compliance_packs_enablement()
    {
        const string json =
            """[{"id":"pp1","packId":"@thinkfleet/pack-healthcare","enabled":true,"config":{"deidentificationMode":"safe-harbor"},"enabledByUserId":"u1","created":"2026-01-01T00:00:00Z","updated":"2026-01-02T00:00:00Z"}]""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, json));
        using var mm = Client(handler);

        var enabled = await mm.Compliance.ListProjectPacksAsync();

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-compliance-packs", req.RequestUri!.AbsolutePath);
        Assert.Single(enabled);
        Assert.True(enabled[0].Enabled);
        Assert.Equal("@thinkfleet/pack-healthcare", enabled[0].PackId);
        Assert.Equal("safe-harbor", enabled[0].Config["deidentificationMode"].GetString());
    }

    // ── upsertProjectPack ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertProjectPack_posts_memory_compliance_packs()
    {
        const string json =
            """{"id":"pp1","packId":"@thinkfleet/pack-healthcare","enabled":true,"config":{"deidentificationMode":"safe-harbor"},"enabledByUserId":"u1","created":"2026-01-01T00:00:00Z","updated":"2026-01-02T00:00:00Z"}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, json); });
        using var mm = Client(handler);

        var row = await mm.Compliance.UpsertProjectPackAsync(new UpsertProjectPackRequest(
            "@thinkfleet/pack-healthcare", Enabled: true,
            Config: new Dictionary<string, object?> { ["deidentificationMode"] = "safe-harbor" }));

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/memory-compliance-packs", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("@thinkfleet/pack-healthcare", doc.RootElement.GetProperty("packId").GetString());
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.Equal("safe-harbor", doc.RootElement.GetProperty("config").GetProperty("deidentificationMode").GetString());
        Assert.True(row.Enabled);
    }

    [Fact]
    public async Task UpsertProjectPack_omits_config_when_absent()
    {
        const string json =
            """{"id":"pp1","packId":"gdpr","enabled":false,"config":{},"enabledByUserId":null,"created":"2026-01-01T00:00:00Z","updated":"2026-01-02T00:00:00Z"}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, json); });
        using var mm = Client(handler);

        await mm.Compliance.UpsertProjectPackAsync(new UpsertProjectPackRequest("gdpr", Enabled: false));

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("config", out _));
    }

    // ── removeProjectPack ───────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveProjectPack_deletes_encoded_pack_id()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.NoContent, ""));
        using var mm = Client(handler);

        await mm.Compliance.RemoveProjectPackAsync("@thinkfleet/pack-healthcare");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Delete, req.Method);
        // The packId is URL-encoded into the path segment.
        Assert.EndsWith("/memory-compliance-packs/%40thinkfleet%2Fpack-healthcare", req.RequestUri!.AbsolutePath);
    }
}
