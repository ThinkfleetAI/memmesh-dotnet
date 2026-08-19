using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the Brains marketplace-registry surface,
/// mirrored from the TS reference, driven through the scripted
/// <see cref="StubHandler"/>.</summary>
public class BrainsServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private const string BrainJson =
        """{"id":"b1","projectId":"proj_1","externalId":"sec-edgar","name":"SEC EDGAR","domain":"finance","brainInterface":"v1","version":"2026.07.0","visibility":"PRIVATE","status":"DRAFT","rightsAttested":false,"card":{"provenance":[{"source":"SEC EDGAR","license":"public-domain"}],"coverage":{"subjects":10,"facts":200}},"created":"2026-01-01T00:00:00Z","updated":"2026-01-02T00:00:00Z"}""";

    // ── create ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_posts_brains_with_full_body()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, BrainJson); });
        using var mm = Client(handler);

        var brain = await mm.Brains.CreateAsync(new CreateBrainRequest(
            ExternalId: "sec-edgar", Name: "SEC EDGAR", Domain: "finance", Version: "2026.07.0",
            Card: new BrainCard(Provenance: [new BrainProvenance("SEC EDGAR", "public-domain")])));

        Assert.Equal("b1", brain.Id);
        Assert.Equal("finance", brain.Domain);
        Assert.Equal("v1", brain.BrainInterface);
        Assert.NotNull(brain.Card);
        Assert.Equal("SEC EDGAR", brain.Card!.Provenance![0].Source);

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/brains", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("sec-edgar", doc.RootElement.GetProperty("externalId").GetString());
        Assert.Equal("public-domain",
            doc.RootElement.GetProperty("card").GetProperty("provenance")[0].GetProperty("license").GetString());
    }

    // ── createFromProject ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateFromProject_defaults_to_draft_private_with_empty_card()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, BrainJson); });
        using var mm = Client(handler);

        await mm.Brains.CreateFromProjectAsync(externalId: "support-playbook", name: "Support Playbook",
            domain: "support");

        Assert.EndsWith("/brains", handler.Requests[0].RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("support-playbook", root.GetProperty("externalId").GetString());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());
        Assert.Equal("PRIVATE", root.GetProperty("visibility").GetString());
        // Empty-but-valid card: empty provenance list + empty coverage object.
        var card = root.GetProperty("card");
        Assert.Equal(0, card.GetProperty("provenance").GetArrayLength());
        Assert.Equal(JsonValueKind.Object, card.GetProperty("coverage").ValueKind);
        // status is never sent on create — the server assigns DRAFT.
        Assert.False(root.TryGetProperty("status", out _));
    }

    // ── list (cursor pagination) ─────────────────────────────────────────────

    [Fact]
    public async Task List_paginates_by_following_the_next_cursor()
    {
        var page1 = $$"""{"data":[{{BrainJson}}],"next":"cur2","previous":null}""";
        var page2 = """{"data":[],"next":null,"previous":"cur2"}""";
        var handler = new StubHandler(
            _ => StubHandler.Json(HttpStatusCode.OK, page1),
            _ => StubHandler.Json(HttpStatusCode.OK, page2));
        using var mm = Client(handler);

        var first = await mm.Brains.ListAsync(limit: 20);
        Assert.Single(first.Data);
        Assert.Equal("b1", first.Data[0].Id);
        Assert.Equal("cur2", first.Next);
        Assert.Null(first.Previous);
        Assert.Contains("limit=20", handler.Requests[0].RequestUri!.Query);

        var second = await mm.Brains.ListAsync(limit: 20, cursor: first.Next);
        Assert.Empty(second.Data);
        Assert.Null(second.Next);
        // The cursor from page 1 is threaded into page 2's request.
        Assert.Contains("cursor=cur2", handler.Requests[1].RequestUri!.Query);
        Assert.Contains("limit=20", handler.Requests[1].RequestUri!.Query);
    }

    // ── get ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_fetches_brain_by_id()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, BrainJson));
        using var mm = Client(handler);

        var brain = await mm.Brains.GetAsync("b1");

        Assert.Equal("b1", brain.Id);
        Assert.Equal(10, brain.Card!.Coverage!.Subjects);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/brains/b1", req.RequestUri!.AbsolutePath);
    }

    // ── update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_patches_only_the_set_fields()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, BrainJson); });
        using var mm = Client(handler);

        await mm.Brains.UpdateAsync("b1", new UpdateBrainRequest(Visibility: "PUBLIC", Status: "PUBLISHED"));

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Patch, req.Method);
        Assert.EndsWith("/brains/b1", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("PUBLIC", doc.RootElement.GetProperty("visibility").GetString());
        Assert.Equal("PUBLISHED", doc.RootElement.GetProperty("status").GetString());
        // Unset optional fields are omitted from the PATCH body.
        Assert.False(doc.RootElement.TryGetProperty("name", out _));
        Assert.False(doc.RootElement.TryGetProperty("card", out _));
    }

    // ── delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_sends_delete_to_brain_route()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, """{"success":true}"""));
        using var mm = Client(handler);

        await mm.Brains.DeleteAsync("b1");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Delete, req.Method);
        Assert.EndsWith("/brains/b1", req.RequestUri!.AbsolutePath);
    }
}
