using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemMesh;

public sealed class ContextService(MemMeshClient c)
{
    private static Dictionary<string, object?> Opts(IEnumerable<string>? include, int? maxTokens,
        int? memoryLimit, int? predictionLimit, IEnumerable<string>? excludeCategories)
    {
        var m = new Dictionary<string, object?>();
        if (include is not null) m["include"] = include;
        if (maxTokens is not null) m["maxTokens"] = maxTokens;
        if (memoryLimit is not null) m["memoryLimit"] = memoryLimit;
        if (predictionLimit is not null) m["predictionLimit"] = predictionLimit;
        if (excludeCategories is not null) m["excludeCategories"] = excludeCategories;
        return m;
    }

    /// <summary>Unified, token-budgeted context bundle for one subject.</summary>
    public Task<JsonElement> BuildAsync(Subject subject, IEnumerable<string>? include = null,
        int? maxTokens = null, int? memoryLimit = null, int? predictionLimit = null,
        IEnumerable<string>? excludeCategories = null, CancellationToken ct = default)
    {
        var body = Opts(include, maxTokens, memoryLimit, predictionLimit, excludeCategories);
        body["subject"] = subject;
        return c.Send<JsonElement>(HttpMethod.Post, "lattice/context", body, ct);
    }

    /// <summary>Bundles for many subjects (&lt;=500) in one call.</summary>
    public async Task<List<JsonElement>> BatchBuildAsync(IEnumerable<Subject> subjects,
        IEnumerable<string>? include = null, int? maxTokens = null, int? memoryLimit = null,
        int? predictionLimit = null, IEnumerable<string>? excludeCategories = null,
        CancellationToken ct = default)
    {
        var body = Opts(include, maxTokens, memoryLimit, predictionLimit, excludeCategories);
        body["subjects"] = subjects;
        var res = await c.Send<BatchBundles>(HttpMethod.Post, "lattice/context/batch", body, ct);
        return res.Bundles;
    }

    /// <summary>Point-in-time knowledge-graph query.</summary>
    public async Task<List<GraphEdge>> QueryGraphAsync(string? subjectId = null,
        string? predicate = null, string? asOf = null, int? limit = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (subjectId is not null) body["subjectId"] = subjectId;
        if (predicate is not null) body["predicate"] = predicate;
        if (asOf is not null) body["asOf"] = asOf;
        if (limit is not null) body["limit"] = limit;
        var res = await c.Send<GraphResult>(HttpMethod.Post, "lattice/graph/query", body, ct);
        return res.Edges;
    }

    private sealed record BatchBundles(List<JsonElement> Bundles);
    private sealed record GraphResult(List<GraphEdge> Edges);
}

/// <summary>Learning — the closed-loop <b>decision → action → outcome</b>
/// primitive. Where <see cref="LatticeService.PredictAsync"/> answers "what will
/// happen?", the learning loop answers "did acting on it work?": record a
/// decision (with links to the patterns/predictions that informed it), record its
/// realized outcome, and every informing pattern's calibrated confidence moves
/// toward what actually happened. <see cref="GetEffectivenessAsync"/> rolls "what
/// worked" up per action_type / decision_type / policy / pattern_kind.
///
/// Mirrors the TS reference surface (<c>tf.learning.*</c>). Domain-agnostic by
/// design — subject/decision/action/outcome/reward only.</summary>
public sealed class LearningService(MemMeshClient c)
{
    /// <summary>Record a decision and its causal provenance. <paramref name="status"/>
    /// defaults to "executed" server-side; re-sending an <paramref name="idempotencyKey"/>
    /// returns the existing decision rather than duplicating.</summary>
    public Task<RecordDecisionResult> RecordDecisionAsync(Subject subject, string? actor = null,
        string? decisionType = null, string? policy = null, IEnumerable<ProvenanceRef>? informedBy = null,
        string? actionType = null, IDictionary<string, string>? parameters = null, string? status = null,
        string? occurredAt = null, IDictionary<string, string>? metadata = null,
        string? idempotencyKey = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["subject"] = subject };
        if (actor is not null) body["actor"] = actor;
        if (decisionType is not null) body["decisionType"] = decisionType;
        if (policy is not null) body["policy"] = policy;
        if (informedBy is not null) body["informedBy"] = informedBy;
        if (actionType is not null) body["actionType"] = actionType;
        if (parameters is not null) body["params"] = parameters;
        if (status is not null) body["status"] = status;
        if (occurredAt is not null) body["occurredAt"] = occurredAt;
        if (metadata is not null) body["metadata"] = metadata;
        if (idempotencyKey is not null) body["idempotencyKey"] = idempotencyKey;
        return c.Send<RecordDecisionResult>(HttpMethod.Post, "lattice/decisions", body, options, ct);
    }

    /// <summary>Record the realized outcome of a decision. Folds the result into
    /// the online calibrated confidence of every pattern the decision was
    /// <c>informedBy</c>, and returns the before/after for each. A replayed
    /// <paramref name="idempotencyKey"/> won't double-count calibration.</summary>
    public Task<RecordOutcomeResult> RecordOutcomeAsync(string decisionId, string result,
        Subject? subject = null, string? outcomeType = null, double? reward = null,
        string? realizedAt = null, int? attributionWindowSecs = null,
        IDictionary<string, string>? metadata = null, string? idempotencyKey = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["decisionId"] = decisionId, ["result"] = result };
        if (subject is not null) body["subject"] = subject;
        if (outcomeType is not null) body["outcomeType"] = outcomeType;
        if (reward is not null) body["reward"] = reward;
        if (realizedAt is not null) body["realizedAt"] = realizedAt;
        if (attributionWindowSecs is not null) body["attributionWindowSecs"] = attributionWindowSecs;
        if (metadata is not null) body["metadata"] = metadata;
        if (idempotencyKey is not null) body["idempotencyKey"] = idempotencyKey;
        return c.Send<RecordOutcomeResult>(HttpMethod.Post, "lattice/outcomes", body, options, ct);
    }

    /// <summary>List recorded outcomes for a subject (or the whole scope), newest
    /// first. <paramref name="limit"/> defaults to 100, clamped [1, 1000].</summary>
    public async Task<List<OutcomeRecord>> GetOutcomesAsync(Subject? subject = null,
        string? decisionType = null, string? actionType = null, int? limit = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (subject is not null)
        {
            q.Add($"subjectKind={Uri.EscapeDataString(subject.Kind)}");
            q.Add($"subjectExternalId={Uri.EscapeDataString(subject.ExternalId)}");
        }
        if (decisionType is not null) q.Add($"decisionType={Uri.EscapeDataString(decisionType)}");
        if (actionType is not null) q.Add($"actionType={Uri.EscapeDataString(actionType)}");
        if (limit is not null) q.Add($"limit={limit}");
        var path = "lattice/outcomes" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        var res = await c.Send<OutcomesResponse>(HttpMethod.Get, path, null, options, ct).ConfigureAwait(false);
        return res.Outcomes;
    }

    /// <summary>"What worked" roll-up — success rate, average reward, and posterior
    /// confidence per group. <paramref name="groupBy"/> is one of action_type /
    /// decision_type / policy / pattern_kind (defaults to action_type);
    /// <paramref name="minSupport"/> returns only groups with at least that many
    /// outcomes. Per-scope only.</summary>
    public async Task<List<EffectivenessRow>> GetEffectivenessAsync(string? groupBy = null,
        int? minSupport = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (groupBy is not null) q.Add($"groupBy={Uri.EscapeDataString(groupBy)}");
        if (minSupport is not null) q.Add($"minSupport={minSupport}");
        var path = "lattice/effectiveness" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        var res = await c.Send<EffectivenessResponse>(HttpMethod.Get, path, null, options, ct).ConfigureAwait(false);
        return res.Rows;
    }

    private sealed record OutcomesResponse(
        [property: JsonPropertyName("outcomes")] List<OutcomeRecord> Outcomes);
    private sealed record EffectivenessResponse(
        [property: JsonPropertyName("rows")] List<EffectivenessRow> Rows);
}

/// <summary>Behaviors — emergent behavior discovery. Where
/// <see cref="LatticeService.PredictAsync"/> answers "what will this subject do?"
/// and <see cref="LatticeService.GetProfileAsync"/> answers "who is this
/// subject?", <see cref="DiscoverAsync"/> answers a project-wide question: "what
/// behaviors exist in my data that nobody defined?" It clusters subjects by their
/// feature vectors and surfaces the dense, cohesive groups as behaviors.
///
/// Mirrors the TS reference surface (<c>tf.behaviors.*</c>).</summary>
public sealed class BehaviorsService(MemMeshClient c)
{
    /// <summary>Discover emergent behaviors across the project. Returns clusters of
    /// like-behaving subjects, sorted most-common-and-cohesive first. An empty
    /// result means the engine abstained — not enough signal to assert any
    /// behavior — never "there are no behaviors". All params are optional; the
    /// engine clamps to safe ranges.</summary>
    public Task<DiscoverResult> DiscoverAsync(double? simThreshold = null, int? minClusterSize = null,
        double? minStability = null, int? maxMembers = null, RequestOptions? options = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (simThreshold is not null) body["simThreshold"] = simThreshold;
        if (minClusterSize is not null) body["minClusterSize"] = minClusterSize;
        if (minStability is not null) body["minStability"] = minStability;
        if (maxMembers is not null) body["maxMembers"] = maxMembers;
        return c.Send<DiscoverResult>(HttpMethod.Post, "lattice/discover", body, options, ct);
    }
}

/// <summary>Compliance — GDPR-grade export, erasure, audit, and pack enablement.
/// Two subject-scoped operations (<see cref="ExportSubjectAsync"/> for Art. 15,
/// <see cref="HardDeleteSubjectAsync"/> for Art. 17) plus the audit log and the
/// compliance-pack surface (installed packs + per-project enablement).
///
/// Mirrors the TS reference surface (<c>tf.compliance.*</c>); operations live
/// under <c>/memory-compliance/*</c> and <c>/memory-compliance-packs</c>.</summary>
public sealed class ComplianceService(MemMeshClient c)
{
    /// <summary>Art. 15 subject-access: return every memory, pattern, observation,
    /// event, and alert fire for the subject in a single bundle.</summary>
    public Task<ExportSubjectResponse> ExportSubjectAsync(Subject subject,
        RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<ExportSubjectResponse>(HttpMethod.Post, "memory-compliance/export",
            new Dictionary<string, object?> { ["subject"] = subject }, options, ct);

    /// <summary>Art. 17 right-to-erasure: cascade-delete the same set and write a
    /// tombstone audit row. <paramref name="reason"/> is required (a case id);
    /// <paramref name="dryRun"/> previews without touching any rows.</summary>
    public Task<HardDeleteSubjectResponse> HardDeleteSubjectAsync(Subject subject, string reason,
        bool dryRun = false, RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<HardDeleteSubjectResponse>(HttpMethod.Post, "memory-compliance/hard-delete",
            new Dictionary<string, object?> { ["subject"] = subject, ["reason"] = reason, ["dryRun"] = dryRun },
            options, ct);

    /// <summary>Read the memory_audit_event log ("who accessed my data and when").
    /// Pass a <paramref name="subject"/> to narrow to one person; omit for the
    /// project-wide log. <paramref name="limit"/> defaults to 100, max 1000.</summary>
    public async Task<List<AuditEvent>> ListAuditEventsAsync(Subject? subject = null,
        string? actor = null, IEnumerable<string>? eventTypes = null, string? since = null,
        int? limit = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (subject is not null)
        {
            q.Add($"subjectKind={Uri.EscapeDataString(subject.Kind)}");
            q.Add($"subjectExternalId={Uri.EscapeDataString(subject.ExternalId)}");
        }
        if (actor is not null) q.Add($"actor={Uri.EscapeDataString(actor)}");
        if (since is not null) q.Add($"since={Uri.EscapeDataString(since)}");
        if (limit is not null) q.Add($"limit={limit}");
        var types = eventTypes?.ToList();
        if (types is { Count: > 0 }) q.Add($"eventTypes={Uri.EscapeDataString(string.Join(",", types))}");
        var path = "memory-compliance/audit" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return await c.Send<List<AuditEvent>>(HttpMethod.Get, path, null, options, ct).ConfigureAwait(false);
    }

    /// <summary>List installed compliance packs and the memory classes / regulations
    /// each enforces on every read.</summary>
    public Task<List<CompliancePack>> ListPacksAsync(RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<List<CompliancePack>>(HttpMethod.Get, "memory-compliance/packs", null, options, ct);

    /// <summary>List which compliance packs are enabled (or explicitly disabled) for
    /// the current project. Packs absent from the list fall back to the platform
    /// default set.</summary>
    public Task<List<ProjectPackEnablement>> ListProjectPacksAsync(RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<List<ProjectPackEnablement>>(HttpMethod.Get, "memory-compliance-packs", null, options, ct);

    /// <summary>Enable, disable, or reconfigure a compliance pack for the current
    /// project. Idempotent on <c>PackId</c> — per-pack config survives
    /// enable/disable cycles.</summary>
    public Task<ProjectPackEnablement> UpsertProjectPackAsync(UpsertProjectPackRequest body,
        RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<ProjectPackEnablement>(HttpMethod.Post, "memory-compliance-packs", body, options, ct);

    /// <summary>Remove the per-project enablement row for a pack; the project then
    /// falls back to the platform default set for it. To explicitly disable instead,
    /// use <see cref="UpsertProjectPackAsync"/> with <c>enabled: false</c>.</summary>
    public Task RemoveProjectPackAsync(string packId, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.SendVoid(HttpMethod.Delete, $"memory-compliance-packs/{Uri.EscapeDataString(packId)}",
            null, options, ct);
}

/// <summary>Health — biological ("health") age + condition prediction for the
/// engine's health vertical. Health data IS memory data: record biomarkers,
/// demographics, and ICD-10 diagnoses as fact memories (stored via
/// <c>/admin/memory</c>) and the engine derives a biological age + condition
/// predictions (<see cref="GetProfileAsync"/>) and cohort base rates
/// (<see cref="GetCohortRiskAsync"/>) at <c>/lattice/health/*</c>.
///
/// Requires the <c>@thinkfleet/pack-healthcare</c> pack; the read methods return
/// FAILED_PRECONDITION otherwise. Mirrors the TS reference surface
/// (<c>tf.health.*</c>). Screening indicators — not a diagnosis.</summary>
public sealed class HealthService(MemMeshClient c)
{
    /// <summary>Record a biomarker reading. Send whatever unit the lab reported via
    /// <paramref name="unit"/>; the engine normalizes it.</summary>
    public Task<MemoryItem> RecordBiomarkerAsync(Subject subject, string biomarker, double value,
        string? unit = null, string? observedAt = null, RequestOptions? options = null,
        CancellationToken ct = default)
    {
        var health = new Dictionary<string, object?>
        {
            ["biomarker"] = biomarker,
            ["value"] = value,
        };
        if (unit is not null) health["unit"] = unit;
        if (observedAt is not null) health["observedAt"] = observedAt;
        var content = $"{biomarker} = {value.ToString(CultureInfo.InvariantCulture)}"
            + (unit is not null ? $" {unit}" : "");
        return RecordAsync(content, new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["health"] = health,
        }, options, ct);
    }

    /// <summary>Record/refresh a subject's demographics. Latest values win.</summary>
    public Task<MemoryItem> RecordDemographicsAsync(Subject subject, DemographicsInput demographics,
        RequestOptions? options = null, CancellationToken ct = default)
        => RecordAsync("Demographics update", new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["demographic"] = demographics,
        }, options, ct);

    /// <summary>Record an ICD-10 diagnosis.</summary>
    public Task<MemoryItem> RecordConditionAsync(Subject subject, ConditionInput condition,
        RequestOptions? options = null, CancellationToken ct = default)
        => RecordAsync($"Diagnosis {condition.Icd10}", new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["condition"] = condition,
        }, options, ct);

    /// <summary>Biological-age estimate + condition predictions + latest biomarkers
    /// for a subject, derived from their recorded health data.</summary>
    public Task<HealthProfile> GetProfileAsync(Subject subject, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<HealthProfile>(HttpMethod.Post, "lattice/health/profile",
            new Dictionary<string, object?> { ["subject"] = subject }, options, ct);

    /// <summary>Cohort outcomes — condition prevalence among the patients most
    /// similar to this subject. <paramref name="k"/> is the cohort size (default 25).</summary>
    public Task<CohortHealthRisk> GetCohortRiskAsync(Subject subject, int? k = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["subject"] = subject };
        if (k is not null) body["k"] = k;
        return c.Send<CohortHealthRisk>(HttpMethod.Post, "lattice/health/cohort-risk", body, options, ct);
    }

    // Health signals are plain fact memories (category "health", source
    // "sdk:health"), so they feed the health engine without being mined as
    // behavioral patterns.
    private Task<MemoryItem> RecordAsync(string content, Dictionary<string, object?> metadata,
        RequestOptions? options, CancellationToken ct)
        => c.Send<MemoryItem>(HttpMethod.Post, "admin/memory", new Dictionary<string, object?>
        {
            ["content"] = content,
            ["type"] = "fact",
            ["scope"] = "project",
            ["category"] = "health",
            ["source"] = "sdk:health",
            ["metadata"] = metadata,
        }, options, ct);
}
