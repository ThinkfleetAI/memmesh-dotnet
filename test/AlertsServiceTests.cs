using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the Alerts surface (8/8), mirrored from
/// the TS reference and driven through the scripted <see cref="StubHandler"/>.
/// Confirms every route sits under <c>/memory-alerts</c>.</summary>
public class AlertsServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private const string RuleJson =
        """{"id":"a1","projectId":"proj_1","name":"VIP at risk","description":null,"enabled":true,"trigger":{"kind":"engine-event","eventTypes":["risk.fired"]},"filter":{"metadataMatch":{"riskKind":"rfm_at_risk_high_value"}},"notify":[{"kind":"webhook","url":"https://hooks.slack.com/x"}],"throttle":{"dedupOn":"subject","cooldownMinutes":60},"created":"2026-01-01T00:00:00Z","updated":"2026-01-02T00:00:00Z"}""";

    // ── list ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_gets_memory_alerts()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, $"[{RuleJson}]"));
        using var mm = Client(handler);

        var rules = await mm.Alerts.ListAsync();

        Assert.Single(rules);
        Assert.Equal("a1", rules[0].Id);
        Assert.Equal("engine-event", rules[0].Trigger.Kind);
        Assert.Equal("risk.fired", rules[0].Trigger.EventTypes![0]);
        Assert.Equal("webhook", rules[0].Notify[0].Kind);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-alerts", req.RequestUri!.AbsolutePath);
    }

    // ── get ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_fetches_alert_by_id()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, RuleJson));
        using var mm = Client(handler);

        var rule = await mm.Alerts.GetAsync("a1");

        Assert.Equal("a1", rule.Id);
        Assert.Equal("subject", rule.Throttle!.DedupOn);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-alerts/a1", req.RequestUri!.AbsolutePath);
    }

    // ── create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_posts_memory_alerts_with_trigger_and_channels()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, RuleJson); });
        using var mm = Client(handler);

        var rule = await mm.Alerts.CreateAsync(new CreateAlertRuleRequest(
            Name: "VIP at risk",
            Trigger: new AlertTrigger("engine-event", EventTypes: ["risk.fired"]),
            Notify: [new NotificationChannel("webhook", Url: "https://hooks.slack.com/x")],
            Throttle: new ThrottleConfig(DedupOn: "subject", CooldownMinutes: 60)));

        Assert.Equal("a1", rule.Id);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/memory-alerts", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("engine-event", root.GetProperty("trigger").GetProperty("kind").GetString());
        Assert.Equal("risk.fired", root.GetProperty("trigger").GetProperty("eventTypes")[0].GetString());
        Assert.Equal("webhook", root.GetProperty("notify")[0].GetProperty("kind").GetString());
        // Unset optional segment-change/pattern fields are omitted from the trigger.
        Assert.False(root.GetProperty("trigger").TryGetProperty("patternKind", out _));
    }

    [Fact]
    public async Task Create_memory_channel_serializes_write_as_template()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, RuleJson); });
        using var mm = Client(handler);

        await mm.Alerts.CreateAsync(new CreateAlertRuleRequest(
            Name: "Churn risk to memory",
            Trigger: new AlertTrigger("engine-event", EventTypes: ["risk.fired"]),
            Notify: [new NotificationChannel("memory",
                WriteAs: new NotificationChannelWriteAs("Risk fired for {{subject.externalId}}.", Scope: "project"))]));

        using var doc = JsonDocument.Parse(body);
        var channel = doc.RootElement.GetProperty("notify")[0];
        Assert.Equal("memory", channel.GetProperty("kind").GetString());
        Assert.Equal("project", channel.GetProperty("writeAs").GetProperty("scope").GetString());
        Assert.False(channel.TryGetProperty("url", out _));
    }

    // ── update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_patches_memory_alert_with_set_fields_only()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, RuleJson); });
        using var mm = Client(handler);

        await mm.Alerts.UpdateAsync("a1", new UpdateAlertRuleRequest(Name: "renamed"));

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Patch, req.Method);
        Assert.EndsWith("/memory-alerts/a1", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("renamed", doc.RootElement.GetProperty("name").GetString());
        Assert.False(doc.RootElement.TryGetProperty("trigger", out _));
    }

    // ── enable / disable ──────────────────────────────────────────────────────

    [Fact]
    public async Task Enable_patches_enabled_true()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, RuleJson); });
        using var mm = Client(handler);

        await mm.Alerts.EnableAsync("a1");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Patch, req.Method);
        Assert.EndsWith("/memory-alerts/a1", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Disable_patches_enabled_false()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, RuleJson); });
        using var mm = Client(handler);

        await mm.Alerts.DisableAsync("a1");

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.False(doc.RootElement.TryGetProperty("name", out _));
    }

    // ── delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_sends_delete_to_memory_alert_route()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, """{"success":true}"""));
        using var mm = Client(handler);

        await mm.Alerts.DeleteAsync("a1");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Delete, req.Method);
        Assert.EndsWith("/memory-alerts/a1", req.RequestUri!.AbsolutePath);
    }

    // ── listFires (nested) ────────────────────────────────────────────────────

    [Fact]
    public async Task ListFires_gets_nested_fires_route()
    {
        const string fireJson =
            """[{"id":"f1","alertRuleId":"a1","eventId":"e1","dedupeKey":"subject:sarah","deliveryResults":[{"channel":"webhook","ok":true}],"firedAt":"2026-01-03T00:00:00Z"}]""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, fireJson));
        using var mm = Client(handler);

        var fires = await mm.Alerts.ListFiresAsync("a1");

        Assert.Single(fires);
        Assert.Equal("f1", fires[0].Id);
        Assert.True(fires[0].DeliveryResults[0].Ok);
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.EndsWith("/memory-alerts/a1/fires", req.RequestUri!.AbsolutePath);
    }
}
