using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the knowledge-graph surface, and for
/// the identity provenance raw-text observe now carries.</summary>
public class GraphServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private const string StatsJson =
        """{"entityCount":12142,"edgeCount":287698,"memoriesWithEdges":184737,"retiredEntities":0,"retiredEdges":2,"entitiesByType":{"concept":5463,"org":3863},"extraction":{"platformEnabled":true,"projectEnabled":false}}""";

    /// The shape the read routes actually return: hydrated subject/object, plus
    /// a hop counter. There is no subjectId on the wire.
    private const string EdgeJson =
        """[{"id":"g1","subject":{"id":"e1","canonicalName":"NVIDIA CORP","type":"org"},"predicate":"reported_metric","object":{"id":"e2","canonicalName":"Cost of Revenue","type":"concept"},"objectLiteral":null,"weight":0.85,"validFrom":"2026-07-24T19:45:04.420Z","validTo":null,"sourceMemoryId":"m1","hop":0}]""";

    // ── stats ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stats_hits_graph_stats_and_parses_counts()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(r => { seen = r; return StubHandler.Json(HttpStatusCode.OK, StatsJson); });
        using var mm = Client(handler);

        var st = await mm.Graph.StatsAsync();

        Assert.Equal(12142, st.EntityCount);
        Assert.Equal(287698, st.EdgeCount);
        Assert.Equal(184737, st.MemoriesWithEdges);
        Assert.Equal(5463, st.EntitiesByType["concept"]);
        Assert.False(st.Extraction!.ProjectEnabled);
        Assert.Contains("/admin/memory/graph/stats", seen!.RequestUri!.ToString());
    }

    // ── entities ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntities_sends_only_set_filters()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(r => { seen = r; return StubHandler.Json(HttpStatusCode.OK, "[]"); });
        using var mm = Client(handler);

        await mm.Graph.ListEntitiesAsync(search: "Sarah", limit: 5);

        var url = seen!.RequestUri!.ToString();
        Assert.Contains("search=Sarah", url);
        Assert.Contains("limit=5", url);
        Assert.DoesNotContain("scope=", url);
        Assert.DoesNotContain("offset=", url);
    }

    [Fact]
    public async Task ListEntities_without_filters_sends_no_query()
    {
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(r => { seen = r; return StubHandler.Json(HttpStatusCode.OK, "[]"); });
        using var mm = Client(handler);

        await mm.Graph.ListEntitiesAsync();

        Assert.EndsWith("/admin/memory/entities", seen!.RequestUri!.ToString());
    }

    [Fact]
    public async Task ListEntities_percent_encodes_filter_values()
    {
        // An unescaped `&` would truncate the filter server-side and quietly
        // return the wrong page — a correctness test, not a style one.
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(r => { seen = r; return StubHandler.Json(HttpStatusCode.OK, "[]"); });
        using var mm = Client(handler);

        await mm.Graph.ListEntitiesAsync(search: "a&b c");

        // AbsoluteUri, not ToString(): Uri.ToString() un-escapes for display,
        // so it would hide whether the escaping happened at all.
        Assert.Contains("search=a%26b%20c", seen!.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetEntity_returns_entity_with_hydrated_edges()
    {
        const string json =
            """{"entity":{"id":"e1","canonicalName":"Sarah"},"edges":[{"id":"g1","subject":{"id":"e1","canonicalName":"Sarah"},"predicate":"works_at","object":{"id":"e2","canonicalName":"Acme"},"weight":0.9,"hop":1}]}""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, json));
        using var mm = Client(handler);

        var hood = await mm.Graph.GetEntityAsync("e1");

        Assert.Equal("Sarah", hood.Entity!.CanonicalName);
        Assert.Equal("Acme", hood.Edges[0].Object!.CanonicalName);
        Assert.Equal(1, hood.Edges[0].Hop);
    }

    // ── edges ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEdges_decodes_hydrated_traversal_shape()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, EdgeJson));
        using var mm = Client(handler);

        var edges = await mm.Graph.ListEdgesAsync(limit: 1);

        Assert.Equal("NVIDIA CORP", edges[0].Subject.CanonicalName);
        Assert.Equal("Cost of Revenue", edges[0].Object!.CanonicalName);
        Assert.Equal("reported_metric", edges[0].Predicate);
        Assert.Equal(0, edges[0].Hop);
        Assert.Equal(0.85, edges[0].Weight, 3);
    }

    [Fact]
    public async Task ListEdges_decodes_literal_object()
    {
        // `object` is null when the value is a literal rather than an entity.
        const string json =
            """[{"id":"g2","subject":{"id":"e1","canonicalName":"NVIDIA CORP"},"predicate":"ticker_symbol","object":null,"objectLiteral":"NVDA","weight":0.85,"hop":0}]""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, json));
        using var mm = Client(handler);

        var edges = await mm.Graph.ListEdgesAsync();

        Assert.Null(edges[0].Object);
        Assert.Equal("NVDA", edges[0].ObjectLiteral);
    }

    // ── traverse ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Traverse_posts_entity_id_and_omits_unset()
    {
        string body = "";
        HttpRequestMessage? seen = null;
        var handler = new StubHandler(r => { seen = r; body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, "[]"); });
        using var mm = Client(handler);

        await mm.Graph.TraverseAsync("e1", hops: 2, predicates: ["member_of", "led_by"]);

        Assert.Equal(HttpMethod.Post, seen!.Method);
        Assert.Contains("/admin/memory/graph/traverse", seen.RequestUri!.ToString());
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("e1", doc.RootElement.GetProperty("entityId").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("hops").GetInt32());
        Assert.Equal("member_of", doc.RootElement.GetProperty("predicates")[0].GetString());
        Assert.False(doc.RootElement.TryGetProperty("asOf", out _));
    }

    // ── observe provenance ─────────────────────────────────────────────────

    [Fact]
    public async Task Observe_text_forwards_identity_fields()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, """{"saved":[],"candidateCount":0}"""); });
        using var mm = Client(handler);

        await mm.Memory.ObserveAsync(text: "I just moved to Denver.",
            userId: "user-123", agentId: "agent-9", sessionId: "thread-456");

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("user-123", doc.RootElement.GetProperty("userId").GetString());
        Assert.Equal("agent-9", doc.RootElement.GetProperty("agentId").GetString());
        Assert.Equal("thread-456", doc.RootElement.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task Observe_text_omits_identity_when_unset()
    {
        // An older call site must produce the request it always did — the
        // fields are absent, not explicit nulls.
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, """{"saved":[],"candidateCount":0}"""); });
        using var mm = Client(handler);

        await mm.Memory.ObserveAsync(text: "hello");

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("userId", out _));
        Assert.False(doc.RootElement.TryGetProperty("agentId", out _));
        Assert.False(doc.RootElement.TryGetProperty("sessionId", out _));
    }
}
