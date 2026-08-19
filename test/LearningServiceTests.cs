using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the Learning + Behaviors surfaces brought
/// to parity with the TS reference, driven through the scripted
/// <see cref="StubHandler"/>.</summary>
public class LearningServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    // ── recordDecision ────────────────────────────────────────────────────

    [Fact]
    public async Task RecordDecision_posts_lattice_decisions_with_provenance()
    {
        string body = "";
        var handler = new StubHandler(r =>
        {
            body = BodyOf(r);
            return StubHandler.Json(HttpStatusCode.OK,
                """{"decision":{"decisionId":"d1","subject":{"kind":"contact","externalId":"sarah"},"actor":"policy:winback-v1","decisionType":"offer","policy":"winback-v1","informedBy":[{"memoryId":"p1","refType":"pattern","weight":1.0}],"actionType":"apply_discount","status":"executed","occurredAt":"2026-01-01T00:00:00Z","created":"2026-01-01T00:00:01Z"}}""");
        });
        using var mm = Client(handler);

        var res = await mm.Learning.RecordDecisionAsync(
            new Subject("contact", "sarah"), actor: "policy:winback-v1", decisionType: "offer",
            informedBy: new[] { new ProvenanceRef("p1", RefType: "pattern") },
            actionType: "apply_discount", parameters: new Dictionary<string, string> { ["pct"] = "15" });

        Assert.NotNull(res.Decision);
        Assert.Equal("d1", res.Decision!.DecisionId);
        Assert.Equal("sarah", res.Decision.Subject!.ExternalId);
        Assert.Single(res.Decision.InformedBy);
        Assert.Equal("p1", res.Decision.InformedBy[0].MemoryId);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/decisions", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("sarah", doc.RootElement.GetProperty("subject").GetProperty("externalId").GetString());
        Assert.Equal("p1", doc.RootElement.GetProperty("informedBy")[0].GetProperty("memoryId").GetString());
        // `params` (reserved keyword in C#) must serialize under its TS name.
        Assert.Equal("15", doc.RootElement.GetProperty("params").GetProperty("pct").GetString());
    }

    // ── recordOutcome ─────────────────────────────────────────────────────

    [Fact]
    public async Task RecordOutcome_posts_lattice_outcomes_and_parses_updates()
    {
        string body = "";
        var handler = new StubHandler(r =>
        {
            body = BodyOf(r);
            return StubHandler.Json(HttpStatusCode.OK,
                """{"outcomeId":"o1","updates":[{"refId":"p1","refType":"pattern","priorConfidence":0.5,"posteriorConfidence":0.62,"hits":3,"misses":1}]}""");
        });
        using var mm = Client(handler);

        var res = await mm.Learning.RecordOutcomeAsync("d1", "success", outcomeType: "conversion", reward: 84.0);

        Assert.Equal("o1", res.OutcomeId);
        Assert.Single(res.Updates);
        Assert.Equal(0.62, res.Updates[0].PosteriorConfidence);
        Assert.Equal(3, res.Updates[0].Hits);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/outcomes", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("d1", doc.RootElement.GetProperty("decisionId").GetString());
        Assert.Equal("success", doc.RootElement.GetProperty("result").GetString());
        Assert.Equal(84.0, doc.RootElement.GetProperty("reward").GetDouble());
    }

    // ── getOutcomes ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetOutcomes_gets_lattice_outcomes_with_subject_query_and_unwraps()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            """{"outcomes":[{"outcomeId":"o1","decisionId":"d1","subject":{"kind":"contact","externalId":"sarah"},"decisionType":"offer","actionType":"apply_discount","outcomeType":"conversion","result":"success","reward":84.0,"occurredAt":"2026-01-01T00:00:00Z","realizedAt":"2026-01-02T00:00:00Z"}]}"""));
        using var mm = Client(handler);

        var res = await mm.Learning.GetOutcomesAsync(
            new Subject("contact", "sarah"), decisionType: "offer", limit: 50);

        Assert.Single(res);
        Assert.Equal("o1", res[0].OutcomeId);
        Assert.Equal("success", res[0].Result);
        Assert.Equal(84.0, res[0].Reward);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/lattice/outcomes", req.RequestUri!.AbsolutePath);
        Assert.Contains("subjectKind=contact", req.RequestUri!.Query);
        Assert.Contains("subjectExternalId=sarah", req.RequestUri!.Query);
        Assert.Contains("decisionType=offer", req.RequestUri!.Query);
        Assert.Contains("limit=50", req.RequestUri!.Query);
    }

    // ── getEffectiveness ──────────────────────────────────────────────────

    [Fact]
    public async Task GetEffectiveness_gets_lattice_effectiveness_with_query_and_unwraps()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK,
            """{"rows":[{"groupKey":"apply_discount","n":40,"successRate":0.3,"avgReward":12.5,"confidence":0.31}]}"""));
        using var mm = Client(handler);

        var res = await mm.Learning.GetEffectivenessAsync(groupBy: "action_type", minSupport: 5);

        Assert.Single(res);
        Assert.Equal("apply_discount", res[0].GroupKey);
        Assert.Equal(40, res[0].N);
        Assert.Equal(0.3, res[0].SuccessRate);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/lattice/effectiveness", req.RequestUri!.AbsolutePath);
        Assert.Contains("groupBy=action_type", req.RequestUri!.Query);
        Assert.Contains("minSupport=5", req.RequestUri!.Query);
    }

    // ── behaviors.discover ────────────────────────────────────────────────

    [Fact]
    public async Task Discover_posts_lattice_discover_with_params_and_parses_behaviors()
    {
        string body = "";
        var handler = new StubHandler(r =>
        {
            body = BodyOf(r);
            return StubHandler.Json(HttpStatusCode.OK,
                """{"behaviors":[{"label":"weekly friday orderers","prevalence":0.42,"stability":0.81,"size":37,"memberSubjects":[{"kind":"contact","externalId":"sarah"}],"exemplarEvidence":["pattern: recurring_event"]}],"subjectsAnalyzed":88,"generatedAt":"2026-01-01T00:00:00Z","durationMs":120}""");
        });
        using var mm = Client(handler);

        var res = await mm.Behaviors.DiscoverAsync(simThreshold: 0.8, minClusterSize: 5, maxMembers: 50);

        Assert.Single(res.Behaviors);
        Assert.Equal("weekly friday orderers", res.Behaviors[0].Label);
        Assert.Equal(0.42, res.Behaviors[0].Prevalence);
        Assert.Equal(37, res.Behaviors[0].Size);
        Assert.Equal("sarah", res.Behaviors[0].MemberSubjects[0].ExternalId);
        Assert.Equal(88, res.SubjectsAnalyzed);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/discover", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(0.8, doc.RootElement.GetProperty("simThreshold").GetDouble());
        Assert.Equal(5, doc.RootElement.GetProperty("minClusterSize").GetInt32());
        Assert.Equal(50, doc.RootElement.GetProperty("maxMembers").GetInt32());
    }

    [Fact]
    public async Task Discover_posts_empty_body_when_no_params_given()
    {
        string body = "";
        var handler = new StubHandler(r =>
        {
            body = BodyOf(r);
            return StubHandler.Json(HttpStatusCode.OK,
                """{"behaviors":[],"subjectsAnalyzed":0,"generatedAt":"2026-01-01T00:00:00Z","durationMs":3}""");
        });
        using var mm = Client(handler);

        var res = await mm.Behaviors.DiscoverAsync();

        Assert.Empty(res.Behaviors);
        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("simThreshold", out _));
    }
}
