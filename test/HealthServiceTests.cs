using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the Health surface (5/5), mirrored from
/// thinkfleet-memory-sdk/src/resources/health.ts. Signals are recorded as fact
/// memories via <c>/admin/memory</c>; reads sit under <c>/lattice/health/*</c>.</summary>
public class HealthServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private const string MemJson =
        """{"id":"m1","type":"fact","content":"x","importance":5,"scope":"project","status":"active","confidence":1,"supersededById":null}""";

    // ── recordBiomarker ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordBiomarker_posts_fact_memory_to_admin_memory()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, MemJson); });
        using var mm = Client(handler);

        var item = await mm.Health.RecordBiomarkerAsync(
            new Subject("patient", "p-123"), "hba1c", 6.2, unit: "%", observedAt: "2026-01-01T00:00:00Z");

        Assert.Equal("m1", item.Id);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/admin/memory", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("hba1c = 6.2 %", root.GetProperty("content").GetString());
        Assert.Equal("fact", root.GetProperty("type").GetString());
        Assert.Equal("project", root.GetProperty("scope").GetString());
        Assert.Equal("health", root.GetProperty("category").GetString());
        Assert.Equal("sdk:health", root.GetProperty("source").GetString());
        var health = root.GetProperty("metadata").GetProperty("health");
        Assert.Equal("hba1c", health.GetProperty("biomarker").GetString());
        Assert.Equal(6.2, health.GetProperty("value").GetDouble());
        Assert.Equal("%", health.GetProperty("unit").GetString());
        Assert.Equal("patient", root.GetProperty("metadata").GetProperty("subject").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task RecordBiomarker_omits_unit_from_content_when_absent()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, MemJson); });
        using var mm = Client(handler);

        await mm.Health.RecordBiomarkerAsync(new Subject("patient", "p-1"), "ldl", 130);

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("ldl = 130", doc.RootElement.GetProperty("content").GetString());
        var health = doc.RootElement.GetProperty("metadata").GetProperty("health");
        Assert.False(health.TryGetProperty("unit", out _));
    }

    // ── recordDemographics ──────────────────────────────────────────────────────

    [Fact]
    public async Task RecordDemographics_posts_demographic_metadata()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, MemJson); });
        using var mm = Client(handler);

        await mm.Health.RecordDemographicsAsync(new Subject("patient", "p-1"),
            new DemographicsInput(AgeYears: 54, Sex: "female", WeightKg: 82, HeightCm: 170, Activity: "low"));

        var req = handler.Requests[0];
        Assert.EndsWith("/admin/memory", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Demographics update", doc.RootElement.GetProperty("content").GetString());
        var demo = doc.RootElement.GetProperty("metadata").GetProperty("demographic");
        Assert.Equal(54, demo.GetProperty("ageYears").GetDouble());
        Assert.Equal("female", demo.GetProperty("sex").GetString());
    }

    // ── recordCondition ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordCondition_posts_icd10_diagnosis()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, MemJson); });
        using var mm = Client(handler);

        await mm.Health.RecordConditionAsync(new Subject("patient", "p-1"),
            new ConditionInput("I10", Status: "active"));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Diagnosis I10", doc.RootElement.GetProperty("content").GetString());
        var cond = doc.RootElement.GetProperty("metadata").GetProperty("condition");
        Assert.Equal("I10", cond.GetProperty("icd10").GetString());
        Assert.Equal("active", cond.GetProperty("status").GetString());
    }

    // ── getProfile ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfile_posts_lattice_health_profile_and_parses()
    {
        const string profileJson =
            """{"subject":{"kind":"patient","externalId":"p-1"},"predictedConditions":[{"condition":"type2_diabetes","label":"Type 2 Diabetes","basis":"above_threshold_now","biomarker":"hba1c","currentValue":6.2,"threshold":6.5,"confidence":0.8,"rationale":"trending","sourceMemoryIds":["m1"]}],"diagnosedConditions":["I10"],"latestBiomarkers":[{"biomarker":"hba1c","value":6.2,"unit":"%","observedAt":"2026-01-01T00:00:00Z"}],"disclaimer":"screening only","generatedAt":"2026-01-02T00:00:00Z","biologicalAge":{"biologicalAgeYears":58,"chronologicalAgeYears":54,"deltaYears":4,"method":"phenoage_hybrid","confidence":0.7,"components":[{"label":"crp","yearsDelta":1.2}],"mortalityScore":0.1}}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, profileJson); });
        using var mm = Client(handler);

        var profile = await mm.Health.GetProfileAsync(new Subject("patient", "p-1"));

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/health/profile", req.RequestUri!.AbsolutePath);
        Assert.Equal("p-1", JsonDocument.Parse(body).RootElement.GetProperty("subject").GetProperty("externalId").GetString());
        Assert.Equal(58, profile.BiologicalAge!.BiologicalAgeYears);
        Assert.Single(profile.PredictedConditions);
        Assert.Equal("type2_diabetes", profile.PredictedConditions[0].Condition);
        Assert.Equal("I10", profile.DiagnosedConditions[0]);
        Assert.Equal("hba1c", profile.LatestBiomarkers[0].Biomarker);
    }

    // ── getCohortRisk ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCohortRisk_posts_lattice_health_cohort_risk_with_k()
    {
        const string cohortJson =
            """{"subject":{"kind":"patient","externalId":"p-1"},"cohortSize":25,"populationSize":1000,"risks":[{"condition":"type2_diabetes","cohortPrevalence":0.32,"cohortSize":25,"countWith":8,"meanSimilarity":0.9,"confidence":0.6,"rationale":"cohort"}],"disclaimer":"screening only","generatedAt":"2026-01-02T00:00:00Z"}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, cohortJson); });
        using var mm = Client(handler);

        var cohort = await mm.Health.GetCohortRiskAsync(new Subject("patient", "p-1"), k: 25);

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/health/cohort-risk", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(25, doc.RootElement.GetProperty("k").GetInt32());
        Assert.Equal(25, cohort.CohortSize);
        Assert.Equal("type2_diabetes", cohort.Risks[0].Condition);
        Assert.Equal(0.32, cohort.Risks[0].CohortPrevalence);
    }

    [Fact]
    public async Task GetCohortRisk_omits_k_when_not_supplied()
    {
        const string cohortJson =
            """{"subject":{"kind":"patient","externalId":"p-1"},"cohortSize":25,"populationSize":1000,"risks":[],"disclaimer":"d","generatedAt":"2026-01-02T00:00:00Z"}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, cohortJson); });
        using var mm = Client(handler);

        await mm.Health.GetCohortRiskAsync(new Subject("patient", "p-1"));

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("k", out _));
    }
}
