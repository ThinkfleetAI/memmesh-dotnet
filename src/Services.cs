using System.Text.Json;

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

public sealed class LatticeService(MemMeshClient c)
{
    public Task<JsonElement> PredictAsync(Subject subject, IDictionary<string, object?> target,
        CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "lattice/predict",
            new Dictionary<string, object?> { ["subject"] = subject, ["target"] = target }, ct);

    public Task<JsonElement> MineAsync(Subject? subject = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (subject is not null) body["subject"] = subject;
        return c.Send<JsonElement>(HttpMethod.Post, "lattice/patterns/extract", body, ct);
    }

    public Task<JsonElement> ProfileAsync(Subject subject, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "lattice/profile",
            new Dictionary<string, object?> { ["subject"] = subject }, ct);

    public Task<JsonElement> PredictByCohortAsync(Subject subject, IDictionary<string, object?> target,
        CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "lattice/cohort/predict",
            new Dictionary<string, object?> { ["subject"] = subject, ["target"] = target }, ct);

    public Task<JsonElement> CalibrationAsync(CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Get, "lattice/calibration", null, ct);
}

public sealed class EventsService(MemMeshClient c)
{
    public Task<JsonElement> EmitAsync(IDictionary<string, object?> ev, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "events", ev, ct);
}

public sealed class AlertsService(MemMeshClient c)
{
    public Task<JsonElement> CreateAsync(IDictionary<string, object?> rule, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "alerts", rule, ct);
    public Task<List<JsonElement>> ListAsync(CancellationToken ct = default)
        => c.Send<List<JsonElement>>(HttpMethod.Get, "alerts", null, ct);
    public Task DeleteAsync(string id, CancellationToken ct = default)
        => c.SendVoid(HttpMethod.Delete, $"alerts/{id}", null, ct);
}

public sealed class LearningService(MemMeshClient c)
{
    public Task<JsonElement> RecordDecisionAsync(IDictionary<string, object?> d, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "learning/decisions", d, ct);
    public Task<JsonElement> RecordOutcomeAsync(IDictionary<string, object?> o, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "learning/outcomes", o, ct);
    public Task<JsonElement> GetEffectivenessAsync(IDictionary<string, object?> q, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "learning/effectiveness", q, ct);
}

public sealed class TypedService(MemMeshClient c)
{
    public Task<JsonElement> RegisterAttributeAsync(IDictionary<string, object?> def, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "typed/attributes", def, ct);
    public Task<JsonElement> IngestAsync(IEnumerable<object> observations, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "typed/observations",
            new Dictionary<string, object?> { ["observations"] = observations }, ct);
}

public sealed class ComplianceService(MemMeshClient c)
{
    public Task<JsonElement> ExportSubjectAsync(Subject subject, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "admin/memory/compliance/export",
            new Dictionary<string, object?> { ["subject"] = subject }, ct);
    public Task<JsonElement> HardDeleteSubjectAsync(Subject subject, string reason, bool dryRun = false,
        CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "admin/memory/compliance/erase",
            new Dictionary<string, object?> { ["subject"] = subject, ["reason"] = reason, ["dryRun"] = dryRun }, ct);
    public Task<List<JsonElement>> ListPacksAsync(CancellationToken ct = default)
        => c.Send<List<JsonElement>>(HttpMethod.Get, "admin/memory/compliance/packs", null, ct);
}

public sealed class HealthService(MemMeshClient c)
{
    public Task<JsonElement> GetProfileAsync(Subject subject, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "health/profile",
            new Dictionary<string, object?> { ["subject"] = subject }, ct);
    public Task<JsonElement> GetCohortRiskAsync(Subject subject, CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Post, "health/cohort-risk",
            new Dictionary<string, object?> { ["subject"] = subject }, ct);
}
