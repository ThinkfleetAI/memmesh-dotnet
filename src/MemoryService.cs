using System.Text.Json;

namespace MemMesh;

/// <summary>Primary surface: ingest, recall, admin + SOTA ops.</summary>
public sealed class MemoryService(MemMeshClient c)
{
    /// <summary>Record that something happened (the primary agent ingestion call).</summary>
    /// <remarks>Pass structured fields the miner reads via <paramref name="metadata"/>.
    /// The RFM Monetary score sums a numeric <c>amount</c> (or <c>value</c>/<c>total</c>,
    /// or a <c>lineItems</c> array) — a price written only into <paramref name="content"/>
    /// is not parsed, so set it there:
    /// <c>metadata: new Dictionary&lt;string, object?&gt; { ["amount"] = 42.0 }</c>.</remarks>
    public Task<MemoryItem> ObserveAsync(string content, Subject? subject = null,
        string type = "event", string scope = "project", int importance = 5,
        string? category = null, string? activityType = null, string? occurredAt = null,
        IDictionary<string, object?>? metadata = null, CancellationToken ct = default)
    {
        var md = new Dictionary<string, object?>();
        if (subject is not null) md["subject"] = subject;
        if (activityType is not null) md["eventType"] = activityType;
        if (occurredAt is not null) md["occurredAt"] = occurredAt;
        if (metadata is not null) foreach (var kv in metadata) md[kv.Key] = kv.Value;
        var body = new Dictionary<string, object?>
        {
            ["content"] = content, ["type"] = type, ["scope"] = scope,
            ["importance"] = importance, ["source"] = "admin_created", ["metadata"] = md,
        };
        if (category is not null) body["category"] = category;
        return c.Send<MemoryItem>(HttpMethod.Post, "admin/memory", body, ct);
    }

    /// <summary>Ingest an image / audio / document. The engine extracts text
    /// (vision, transcription, or OCR via LiteLLM) and runs it through the
    /// observe pipeline, so the result is real memories — not just a stored
    /// file. Requires multimodal to be enabled on the engine.</summary>
    public Task<IngestMediaResult> IngestMediaAsync(byte[] media, string mimeType,
        string? userId = null, string? agentId = null, string? sessionId = null,
        string? source = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["dataBase64"] = Convert.ToBase64String(media),
            ["mimeType"] = mimeType,
        };
        if (userId is not null) body["userId"] = userId;
        if (agentId is not null) body["agentId"] = agentId;
        if (sessionId is not null) body["sessionId"] = sessionId;
        if (source is not null) body["source"] = source;
        return c.Send<IngestMediaResult>(HttpMethod.Post, "memory/media", body, ct);
    }

    /// <summary>Seed a memory directly.</summary>
    public Task<MemoryItem> CreateAsync(string content, string type = "fact",
        string scope = "project", int importance = 5, string? category = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["content"] = content, ["type"] = type, ["scope"] = scope, ["importance"] = importance,
        };
        if (category is not null) body["category"] = category;
        return c.Send<MemoryItem>(HttpMethod.Post, "admin/memory", body, ct);
    }

    /// <summary>Hybrid semantic + keyword search.</summary>
    public Task<List<SearchResult>> SearchAsync(string query, int limit = 10,
        string? scope = null, string? status = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["query"] = query, ["limit"] = limit };
        if (scope is not null) body["scope"] = scope;
        if (status is not null) body["status"] = status;
        return c.Send<List<SearchResult>>(HttpMethod.Post, "admin/memory/search", body, ct);
    }

    public Task<List<MemoryItem>> ListAsync(string? type = null, string? scope = null,
        string? status = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (type is not null) q.Add($"type={Uri.EscapeDataString(type)}");
        if (scope is not null) q.Add($"scope={Uri.EscapeDataString(scope)}");
        if (status is not null) q.Add($"status={Uri.EscapeDataString(status)}");
        if (limit is not null) q.Add($"limit={limit}");
        if (offset is not null) q.Add($"offset={offset}");
        var path = "admin/memory" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return c.Send<List<MemoryItem>>(HttpMethod.Get, path, null, ct);
    }

    public Task<MemoryItem> UpdateAsync(string id, IDictionary<string, object?> patch,
        CancellationToken ct = default)
        => c.Send<MemoryItem>(HttpMethod.Patch, $"admin/memory/{id}", patch, ct);

    public Task DeleteAsync(string id, CancellationToken ct = default)
        => c.SendVoid(HttpMethod.Delete, $"admin/memory/{id}", null, ct);

    public Task<JsonElement> StatsAsync(CancellationToken ct = default)
        => c.Send<JsonElement>(HttpMethod.Get, "admin/memory/stats", null, ct);

    public Task<MemoryItem> ConfirmAsync(string id, string status, string? comment = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["status"] = status };
        if (comment is not null) body["comment"] = comment;
        return c.Send<MemoryItem>(HttpMethod.Post, $"admin/memory/{id}/confirm", body, ct);
    }

    public Task<MemoryItem> PromoteAsync(string id, string targetScope, CancellationToken ct = default)
        => c.Send<MemoryItem>(HttpMethod.Post, $"admin/memory/{id}/promote",
            new Dictionary<string, object?> { ["targetScope"] = targetScope }, ct);

    public Task FeedbackAsync(string id, string rating, string? comment = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["memoryId"] = id, ["rating"] = rating };
        if (comment is not null) body["comment"] = comment;
        return c.SendVoid(HttpMethod.Post, "memory/feedback", body, ct);
    }

    // ── Admin / SOTA ──────────────────────────────────────────────────────

    public Task<DedupResult> DedupAsync(double? threshold = null, int? scanLimit = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (threshold is not null) body["threshold"] = threshold;
        if (scanLimit is not null) body["scanLimit"] = scanLimit;
        return c.Send<DedupResult>(HttpMethod.Post, "admin/memory/dedup", body, ct);
    }

    /// <summary>Synthesize higher-order insight memories from recent memories.</summary>
    public Task<ReflectResult> ReflectAsync(string? userId = null, int? maxSources = null,
        int? maxInsights = null, bool dryRun = false, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["dryRun"] = dryRun };
        if (userId is not null) body["userId"] = userId;
        if (maxSources is not null) body["maxSources"] = maxSources;
        if (maxInsights is not null) body["maxInsights"] = maxInsights;
        return c.Send<ReflectResult>(HttpMethod.Post, "admin/memory/reflect", body, ct);
    }

    /// <summary>Memories linked to the same graph entities as the seeds.</summary>
    public Task<List<MemoryItem>> PrefetchRelatedAsync(IEnumerable<string> seedMemoryIds,
        int? limit = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["seedMemoryIds"] = seedMemoryIds };
        if (limit is not null) body["limit"] = limit;
        return c.Send<List<MemoryItem>>(HttpMethod.Post, "admin/memory/prefetch-related", body, ct);
    }
}
