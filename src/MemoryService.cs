using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MemMesh;

/// <summary>Primary surface: ingest, recall, admin + SOTA ops.</summary>
public sealed class MemoryService(MemMeshClient c)
{
    /// <summary>Record that something happened (the primary agent ingestion call).</summary>
    /// <remarks>
    /// <para>PRIMARY path — pass <paramref name="text"/>: hand the engine the raw
    /// turn verbatim and let it run the Observe pipeline (extract → dedupe →
    /// budget), keeping only what's worth remembering. Filler comes back with an
    /// empty <see cref="ObserveResponse.Saved"/> — that is still a success.</para>
    /// <para>LEGACY path — pass <paramref name="content"/> (and leave
    /// <paramref name="text"/> null): a pre-decided fact is stored verbatim via
    /// admin-create, bypassing extraction, and wrapped as a single-item
    /// <see cref="ObserveResponse"/>. Structured fields the miner reads go via
    /// <paramref name="metadata"/>; the RFM Monetary score sums a numeric
    /// <c>amount</c> (or <c>value</c>/<c>total</c>, or a <c>lineItems</c> array) —
    /// a price written only into <paramref name="content"/> is not parsed, so set
    /// it there: <c>metadata: new Dictionary&lt;string, object?&gt; { ["amount"] = 42.0 }</c>.</para>
    /// </remarks>
    public async Task<ObserveResponse> ObserveAsync(string? text = null, string role = "user",
        string? content = null, Subject? subject = null,
        string type = "event", string scope = "project", int importance = 5,
        string? category = null, string? activityType = null, string? occurredAt = null,
        IDictionary<string, object?>? metadata = null, CancellationToken ct = default)
    {
        // PRIMARY path: raw text through the engine's noise filter. POST
        // /memory/observe runs the Observe pipeline and returns { saved,
        // candidateCount }; filler is dropped and returns saved: [].
        if (!string.IsNullOrWhiteSpace(text))
        {
            var observeBody = new Dictionary<string, object?> { ["text"] = text, ["role"] = role };
            if (occurredAt is not null) observeBody["occurredAt"] = occurredAt;
            return await c.Send<ObserveResponse>(HttpMethod.Post, "memory/observe", observeBody, ct)
                .ConfigureAwait(false);
        }

        // LEGACY path: caller handed a pre-decided fact. Store it verbatim (no
        // extraction) and wrap the single item so the return shape stays consistent.
        if (!string.IsNullOrWhiteSpace(content))
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
            // Event time, not ingest time. validFrom is the field behavior mining
            // buckets day-of-week / hour-of-day on, so this is what makes a backfill
            // work: without it every historical row lands at the moment of import and
            // the mined patterns describe the import job rather than the data. The
            // metadata copy above is kept only for readers that already look for it.
            if (occurredAt is not null) body["validFrom"] = occurredAt;
            if (category is not null) body["category"] = category;
            var item = await c.Send<MemoryItem>(HttpMethod.Post, "admin/memory", body, ct)
                .ConfigureAwait(false);
            return new ObserveResponse { Saved = new() { item }, CandidateCount = 1 };
        }

        throw new ArgumentException("ObserveAsync requires `text` (preferred) or `content`.", nameof(text));
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

    /// <summary>Record an image as a memory item. The bytes are uploaded as a
    /// MEMORY_ATTACHMENT file and a memory item is created with the subject +
    /// activity metadata you provide. Pass <paramref name="content"/> for the
    /// searchable caption — the engine does not auto-caption today.</summary>
    public Task<MemoryItem> ObserveImageAsync(Subject subject, byte[] image, string mimeType,
        string? fileName = null, string? content = null, string? activityType = null,
        string? occurredAt = null, int? importance = null,
        IDictionary<string, object?>? metadata = null,
        RequestOptions? options = null, CancellationToken ct = default)
        => UploadAttachmentAsync(subject, Convert.ToBase64String(image), mimeType, fileName,
            content, activityType, occurredAt, importance, metadata, options, ct);

    /// <summary>Record a voice clip / audio file as a memory item. Pass
    /// <paramref name="content"/> for the searchable transcript — the engine does
    /// not auto-transcribe today. Audio bytes are stored and reachable via
    /// <c>metadata.fileId</c>.</summary>
    public Task<MemoryItem> ObserveVoiceAsync(Subject subject, byte[] audio, string mimeType,
        string? fileName = null, string? content = null, string? activityType = null,
        string? occurredAt = null, int? importance = null,
        IDictionary<string, object?>? metadata = null,
        RequestOptions? options = null, CancellationToken ct = default)
        => UploadAttachmentAsync(subject, Convert.ToBase64String(audio), mimeType, fileName,
            content, activityType, occurredAt, importance, metadata, options, ct);

    /// <summary>Record a document (PDF, Word, Markdown, plain text, …) as a memory
    /// item. Pass <paramref name="content"/> for the searchable text — the engine
    /// does not auto-extract from PDFs / Office files yet, so parse first.</summary>
    public Task<MemoryItem> ObserveDocumentAsync(Subject subject, byte[] document, string mimeType,
        string? fileName = null, string? content = null, string? activityType = null,
        string? occurredAt = null, int? importance = null,
        IDictionary<string, object?>? metadata = null,
        RequestOptions? options = null, CancellationToken ct = default)
        => UploadAttachmentAsync(subject, Convert.ToBase64String(document), mimeType, fileName,
            content, activityType, occurredAt, importance, metadata, options, ct);

    // Shared upload path for the three modality helpers above. Mirrors the TS
    // `uploadAttachment`: everything except the bytes is spread top-level onto the
    // POST /memory/attachments body alongside the base64 payload — note fields
    // like activityType stay top-level here rather than being nested under
    // metadata the way ObserveAsync does.
    private Task<MemoryItem> UploadAttachmentAsync(Subject subject, string dataBase64, string mimeType,
        string? fileName, string? content, string? activityType, string? occurredAt,
        int? importance, IDictionary<string, object?>? metadata,
        RequestOptions? options, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["subject"] = subject, ["mimeType"] = mimeType, ["dataBase64"] = dataBase64,
        };
        if (fileName is not null) body["fileName"] = fileName;
        if (content is not null) body["content"] = content;
        if (activityType is not null) body["activityType"] = activityType;
        if (occurredAt is not null) body["occurredAt"] = occurredAt;
        if (importance is not null) body["importance"] = importance;
        if (metadata is not null) body["metadata"] = metadata;
        return c.Send<MemoryItem>(HttpMethod.Post, "memory/attachments", body, options, ct);
    }

    /// <summary>List the current user's memories across all scopes
    /// (GET /memory/mine). Distinct from the admin <see cref="ListAsync"/>, which
    /// requires READ_MEMORY.</summary>
    public Task<List<MemoryItem>> MineAsync(int? limit = null, int? offset = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (limit is not null) q.Add($"limit={limit}");
        if (offset is not null) q.Add($"offset={offset}");
        var path = "memory/mine" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return c.Send<List<MemoryItem>>(HttpMethod.Get, path, null, options, ct);
    }

    /// <summary>Right-to-explanation: for a derived memory (e.g. a behavior
    /// pattern), resolve the raw source memories that produced it. Reads
    /// <c>metadata.sourceMemoryIds</c> (camelCase or snake_case) off the item and
    /// point-looks-up each source, skipping any since deleted. For a non-derived
    /// item, returns the item with an empty source list.</summary>
    public async Task<MemoryExplanation> ExplainAsync(string memoryId,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var memory = await c.Send<MemoryItem>(HttpMethod.Get, $"admin/memory/{memoryId}",
            null, options, ct).ConfigureAwait(false);

        var ids = ExtractSourceMemoryIds(memory.Metadata);
        if (ids.Count == 0) return new MemoryExplanation(memory, []);

        var lookups = ids.Select(async id =>
        {
            try
            {
                return await c.Send<MemoryItem>(HttpMethod.Get, $"admin/memory/{id}",
                    null, options, ct).ConfigureAwait(false);
            }
            catch (MemMeshException)
            {
                // A source since deleted or superseded is skipped rather than
                // failing the whole explain.
                return null;
            }
        });
        var resolved = await Task.WhenAll(lookups).ConfigureAwait(false);
        var sources = resolved.Where(m => m is not null).Select(m => m!).ToList();
        return new MemoryExplanation(memory, sources);
    }

    // Pull string source ids off a memory's metadata. The Rust engine emits
    // camelCase; legacy rows may carry snake_case.
    private static List<string> ExtractSourceMemoryIds(IReadOnlyDictionary<string, JsonElement>? md)
    {
        if (md is null) return [];
        if (!md.TryGetValue("sourceMemoryIds", out var arr) &&
            !md.TryGetValue("source_memory_ids", out arr))
            return [];
        if (arr.ValueKind != JsonValueKind.Array) return [];
        var ids = new List<string>();
        foreach (var el in arr.EnumerateArray())
            if (el.ValueKind == JsonValueKind.String) ids.Add(el.GetString()!);
        return ids;
    }

    /// <summary>Seed a memory directly. Pass <paramref name="occurredAt"/> (ISO-8601)
    /// when seeding anything back-dated: it is the event time behavior mining
    /// buckets patterns by, and it defaults to now.</summary>
    public Task<MemoryItem> CreateAsync(string content, string type = "fact",
        string scope = "project", int importance = 5, string? category = null,
        string? occurredAt = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["content"] = content, ["type"] = type, ["scope"] = scope, ["importance"] = importance,
        };
        if (occurredAt is not null) body["validFrom"] = occurredAt;
        if (category is not null) body["category"] = category;
        return c.Send<MemoryItem>(HttpMethod.Post, "admin/memory", body, ct);
    }

    /// <summary>Fetch a single memory by id.
    ///
    /// The point-lookup counterpart to ListAsync/SearchAsync: without it, a caller
    /// holding a memory id (from a pattern's sourceMemoryIds, an audit log, a
    /// webhook) had no way to resolve it and had to page ListAsync hoping the row
    /// was still on one.</summary>
    public Task<MemoryItem> GetAsync(string memoryId, CancellationToken ct = default)
        => c.Send<MemoryItem>(HttpMethod.Get, $"admin/memory/{memoryId}", null, ct);

    /// <summary>Render a procedure into the injectable content string —
    /// identical to the engine-side renderer, so client-authored content
    /// matches what the server would produce.</summary>
    public static string RenderProcedureContent(string goal, IEnumerable<ProcedureStep> steps,
        string? whenToUse = null, IEnumerable<string>? failureModes = null)
    {
        var lines = new List<string> { $"Goal: {goal.Trim()}" };
        if (!string.IsNullOrWhiteSpace(whenToUse)) lines.Add($"When: {whenToUse!.Trim()}");
        lines.Add("Steps:");
        var i = 1;
        foreach (var s in steps)
        {
            var suffix = string.IsNullOrWhiteSpace(s.Pitfall) ? "" : $" (watch out: {s.Pitfall!.Trim()})";
            lines.Add($"{i}. {s.Text.Trim()}{suffix}");
            i++;
        }
        var failures = (failureModes ?? []).Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
        if (failures.Count > 0)
        {
            lines.Add("Avoid:");
            lines.AddRange(failures.Select(f => $"- {f}"));
        }
        return string.Join("\n", lines);
    }

    /// <summary>Author a procedure ("how this job is done here"). Stored as a
    /// <c>procedure</c> memory: the structured shape on <c>metadata</c> and the
    /// rendered how-to on <c>content</c>, so retrieval injects it as an explicit
    /// exemplar.</summary>
    public Task<MemoryItem> CreateProcedureAsync(string goal, IReadOnlyList<ProcedureStep> steps,
        string? whenToUse = null, IReadOnlyList<string>? failureModes = null,
        string? category = null, string scope = "project", int importance = 7,
        CancellationToken ct = default)
    {
        var metadata = new Dictionary<string, object?> { ["goal"] = goal, ["steps"] = steps };
        if (whenToUse is not null) metadata["whenToUse"] = whenToUse;
        if (failureModes is not null && failureModes.Count > 0) metadata["failureModes"] = failureModes;
        var body = new Dictionary<string, object?>
        {
            ["content"] = RenderProcedureContent(goal, steps, whenToUse, failureModes),
            ["type"] = "procedure", ["scope"] = scope, ["importance"] = importance,
            ["metadata"] = metadata,
        };
        if (category is not null) body["category"] = category;
        return c.Send<MemoryItem>(HttpMethod.Post, "admin/memory", body, ct);
    }

    /// <summary>The adjudication queue — everything the system is unsure about.
    /// Each row carries a <c>ReviewReason</c> (pending / flagged / low_confidence
    /// / stale).</summary>
    public Task<List<ReviewQueueItem>> ListPendingReviewAsync(int? limit = null, int? offset = null,
        CancellationToken ct = default)
    {
        var q = new List<string>();
        if (limit is not null) q.Add($"limit={limit}");
        if (offset is not null) q.Add($"offset={offset}");
        var path = "admin/memory/review" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return c.Send<List<ReviewQueueItem>>(HttpMethod.Get, path, null, ct);
    }

    /// <summary>Get the project's memory precedence policy — which memory wins
    /// when two disagree. Falls back to the default ladder when unset.</summary>
    public Task<PrecedencePolicy> GetPrecedenceAsync(CancellationToken ct = default)
        => c.Send<PrecedencePolicy>(HttpMethod.Get, "admin/memory/precedence", null, ct);

    /// <summary>Save the precedence policy. Requires the Memory Steward role.</summary>
    public Task<PrecedencePolicy> SetPrecedenceAsync(PrecedencePolicy policy, CancellationToken ct = default)
        => c.Send<PrecedencePolicy>(HttpMethod.Put, "admin/memory/precedence", policy, ct);

    /// <summary>Hybrid semantic + keyword search. Page by bumping <paramref name="offset"/>.</summary>
    // `offset` is appended after the existing optional params on purpose: putting
    // it earlier would rebind existing positional callers (SearchAsync(q, 10,
    // "project")) onto the wrong argument.
    public Task<List<SearchResult>> SearchAsync(string query, int limit = 10,
        string? scope = null, string? status = null, int? offset = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["query"] = query, ["limit"] = limit };
        if (offset is > 0) body["offset"] = offset;
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

    /// <summary>Walk every memory matching the filter, transparently paging under
    /// the hood, yielding items one at a time so a large corpus never has to fit
    /// in memory.</summary>
    /// <remarks>The admin list route is offset-paginated over a bare, newest-first
    /// array (not the cursor/<see cref="SeekPage{T}"/> shape
    /// <see cref="MemMeshClient.ListAllAsync{T}"/> follows), so this walks by
    /// offset with a page cap of 500 — mirroring the TS <c>admin.listAll</c>.
    /// Writes landing mid-walk can shift rows across page boundaries; fine for
    /// browsing and export.</remarks>
    public async IAsyncEnumerable<MemoryItem> ListAllAsync(string? type = null, string? scope = null,
        string? status = null, int? limit = null, RequestOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        const int maxPage = 500;
        var pageLimit = Math.Min(limit ?? maxPage, maxPage);
        var offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var q = new List<string>();
            if (type is not null) q.Add($"type={Uri.EscapeDataString(type)}");
            if (scope is not null) q.Add($"scope={Uri.EscapeDataString(scope)}");
            if (status is not null) q.Add($"status={Uri.EscapeDataString(status)}");
            q.Add($"limit={pageLimit}");
            q.Add($"offset={offset}");
            var path = "admin/memory?" + string.Join("&", q);
            var page = await c.Send<List<MemoryItem>>(HttpMethod.Get, path, null, options, ct)
                .ConfigureAwait(false);
            foreach (var item in page) yield return item;
            if (page.Count < pageLimit) yield break;
            offset += page.Count;
        }
    }

    /// <summary>List platform-level memories (shared across every project on this
    /// platform).</summary>
    public Task<List<MemoryItem>> ListPlatformAsync(string? status = null, int? limit = null,
        int? offset = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (status is not null) q.Add($"status={Uri.EscapeDataString(status)}");
        if (limit is not null) q.Add($"limit={limit}");
        if (offset is not null) q.Add($"offset={offset}");
        var path = "admin/memory/platform" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return c.Send<List<MemoryItem>>(HttpMethod.Get, path, null, options, ct);
    }

    public Task<MemoryItem> UpdateAsync(string id, IDictionary<string, object?> patch,
        CancellationToken ct = default)
        => c.Send<MemoryItem>(HttpMethod.Patch, $"admin/memory/{id}", patch, ct);

    public Task DeleteAsync(string id, CancellationToken ct = default)
        => c.SendVoid(HttpMethod.Delete, $"admin/memory/{id}", null, ct);

    /// <summary>Delete one of your own memory items (user-scoped,
    /// DELETE /memory/{id}). Distinct from the admin <see cref="DeleteAsync"/>
    /// (DELETE /admin/memory/{id}), which requires WRITE_MEMORY.</summary>
    public Task DeleteMineAsync(string id, RequestOptions? options = null, CancellationToken ct = default)
        => c.SendVoid(HttpMethod.Delete, $"memory/{id}", null, options, ct);

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

    /// <summary>LLM-driven deductive consolidation — read recent activity for a
    /// subject (or every subject when omitted), batch it with existing
    /// observations, and let an LLM emit create / update / supersede actions. The
    /// deductive counterpart to Lattice's inductive pattern miner: inductive
    /// answers "what's repeating?", deductive answers "what's true?".</summary>
    public Task<ConsolidateResult> ConsolidateAsync(Subject? subject = null, int? windowDays = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (subject is not null) body["subject"] = subject;
        if (windowDays is not null) body["windowDays"] = windowDays;
        return c.Send<ConsolidateResult>(HttpMethod.Post, "admin/memory/llm-consolidate", body, options, ct);
    }

    /// <summary>Vectorize memory items that have no embedding yet (catch-up
    /// work-list). Idempotent — already-embedded items are skipped. Call
    /// repeatedly until <c>Embedded</c> is 0 to drain a large corpus.</summary>
    public Task<BackfillEmbeddingsResult> BackfillEmbeddingsAsync(int? batch = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (batch is not null) body["batch"] = batch;
        return c.Send<BackfillEmbeddingsResult>(HttpMethod.Post, "admin/memory/embeddings/backfill", body, options, ct);
    }

    /// <summary>List the feedback records attached to a memory item — useful when
    /// inspecting auto-flagged items to decide whether to confirm or reject.</summary>
    public Task<List<MemoryFeedback>> ListFeedbackAsync(string memoryId,
        RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<List<MemoryFeedback>>(HttpMethod.Get, $"admin/memory/{memoryId}/feedback", null, options, ct);

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
