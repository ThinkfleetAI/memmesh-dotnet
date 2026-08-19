namespace MemMesh;

/// <summary>Alerts — user-defined "tell me when X happens, this way" rules that
/// hook into the engine event stream. Triggers match the same event types
/// <see cref="EventsService.PollAsync"/> returns; channels deliver via HTTP
/// webhook or write the alert back as a memory item for the LLM to pick up on the
/// next context build.
///
/// Mirrors the TS reference surface (<c>tf.alerts.*</c>). Rules live at
/// <c>/memory-alerts</c>; a rule's recent fires at <c>/memory-alerts/{id}/fires</c>.
/// <code>
/// var rule = await mm.Alerts.CreateAsync(new CreateAlertRuleRequest(
///     Name: "VIP at risk",
///     Trigger: new AlertTrigger("engine-event", EventTypes: ["risk.fired"]),
///     Notify: [new NotificationChannel("webhook", Url: "https://hooks.slack.com/...")],
///     Throttle: new ThrottleConfig(DedupOn: "subject", CooldownMinutes: 60)));
/// var fires = await mm.Alerts.ListFiresAsync(rule.Id);
/// </code></summary>
public sealed class AlertsService(MemMeshClient c)
{
    /// <summary>List every alert rule in the project.</summary>
    public Task<List<AlertRule>> ListAsync(RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<List<AlertRule>>(HttpMethod.Get, "memory-alerts", null, options, ct);

    /// <summary>Fetch one alert rule by id.</summary>
    public Task<AlertRule> GetAsync(string alertId, RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<AlertRule>(HttpMethod.Get, $"memory-alerts/{Uri.EscapeDataString(alertId)}", null, options, ct);

    /// <summary>Create an alert rule.</summary>
    public Task<AlertRule> CreateAsync(CreateAlertRuleRequest body, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<AlertRule>(HttpMethod.Post, "memory-alerts", body, options, ct);

    /// <summary>Patch an alert rule — only the fields you set are sent.</summary>
    public Task<AlertRule> UpdateAsync(string alertId, UpdateAlertRuleRequest body,
        RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<AlertRule>(HttpMethod.Patch, $"memory-alerts/{Uri.EscapeDataString(alertId)}", body, options, ct);

    /// <summary>Delete an alert rule.</summary>
    public Task DeleteAsync(string alertId, RequestOptions? options = null, CancellationToken ct = default)
        => c.SendVoid(HttpMethod.Delete, $"memory-alerts/{Uri.EscapeDataString(alertId)}", null, options, ct);

    /// <summary>Convenience — patch only the <c>enabled</c> flag on.</summary>
    public Task<AlertRule> EnableAsync(string alertId, RequestOptions? options = null, CancellationToken ct = default)
        => UpdateAsync(alertId, new UpdateAlertRuleRequest(Enabled: true), options, ct);

    /// <summary>Convenience — patch only the <c>enabled</c> flag off.</summary>
    public Task<AlertRule> DisableAsync(string alertId, RequestOptions? options = null, CancellationToken ct = default)
        => UpdateAsync(alertId, new UpdateAlertRuleRequest(Enabled: false), options, ct);

    /// <summary>The last ~100 fires for a rule, newest first.</summary>
    public Task<List<AlertFire>> ListFiresAsync(string alertId, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<List<AlertFire>>(HttpMethod.Get,
            $"memory-alerts/{Uri.EscapeDataString(alertId)}/fires", null, options, ct);
}
