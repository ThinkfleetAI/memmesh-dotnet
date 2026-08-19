namespace MemMesh;

/// <summary>Lattice — behavioral pattern intelligence. Mines a subject's event /
/// memory history for repeatable behaviors, projects them forward, and exposes a
/// context bundle a downstream AI step can hand to a message renderer.
///
/// Mirrors the TS reference surface (<c>tf.lattice.*</c>): extract/mine patterns,
/// inspect/list them, fetch the context bundle, run the pattern-break monitor,
/// predict (pattern-projection or declared-target), profile a subject, compute
/// cohorts, run deterministic estimators, and read the calibration report.</summary>
public sealed class LatticeService(MemMeshClient c)
{
    // ── Pattern extraction / mining ─────────────────────────────────────────

    /// <summary>Force pattern (re-)extraction. Omit <paramref name="contactId"/>
    /// and <paramref name="subject"/> for a project-wide bulk run. Defaults to
    /// mining the memory corpus (<c>source='memories'</c>); pass
    /// <c>source='contact_events'</c> for the legacy contact-event path.</summary>
    public Task<ExtractPatternsResult> ExtractPatternsAsync(string? contactId = null,
        int? windowDays = null, bool? force = null, string? source = null,
        Subject? subject = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (contactId is not null) body["contactId"] = contactId;
        if (windowDays is not null) body["windowDays"] = windowDays;
        if (force is not null) body["force"] = force;
        if (source is not null) body["source"] = source;
        if (subject is not null) body["subject"] = subject;
        return c.Send<ExtractPatternsResult>(HttpMethod.Post, "lattice/patterns/extract", body, options, ct);
    }

    /// <summary>Mine behavioral patterns from the memory corpus. Subject-agnostic:
    /// works on any subject kind. Thin wrapper over
    /// <see cref="ExtractPatternsAsync"/> with <c>source='memories'</c>.</summary>
    public Task<ExtractPatternsResult> MineMemoriesAsync(Subject? subject = null,
        int? windowDays = null, bool? force = null, RequestOptions? options = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["source"] = "memories" };
        if (subject is not null) body["subject"] = subject;
        if (windowDays is not null) body["windowDays"] = windowDays;
        if (force is not null) body["force"] = force;
        return c.Send<ExtractPatternsResult>(HttpMethod.Post, "lattice/patterns/extract", body, options, ct);
    }

    /// <summary>Inspect a single pattern by id. 404s if it doesn't exist or belongs
    /// to a different project.</summary>
    public Task<BehaviorPatternRecord> GetPatternAsync(string patternId,
        RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<BehaviorPatternRecord>(HttpMethod.Get,
            $"lattice/patterns/{Uri.EscapeDataString(patternId)}", null, options, ct);

    /// <summary>List behavior patterns Lattice has learned for a contact.
    /// Cursor-paginated — pass the response's <c>NextCursor</c> back as
    /// <paramref name="cursor"/> for the next page.</summary>
    public Task<ListContactPatternsResponse> ListPatternsAsync(string contactId,
        bool? activeOnly = null, int? limit = null, string? cursor = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (activeOnly is not null) q.Add($"activeOnly={(activeOnly.Value ? "true" : "false")}");
        if (limit is not null) q.Add($"limit={limit}");
        if (cursor is not null) q.Add($"cursor={Uri.EscapeDataString(cursor)}");
        var path = $"lattice/contacts/{Uri.EscapeDataString(contactId)}/patterns"
            + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return c.Send<ListContactPatternsResponse>(HttpMethod.Get, path, null, options, ct);
    }

    /// <summary>Full retrieval bundle for a contact — profile, active patterns,
    /// recent events, recent memories, and (optionally) the entity/edge graph.
    /// Supports bi-temporal replay via <paramref name="asOf"/> (ISO-8601).</summary>
    public Task<LatticeContextBundle> GetContextAsync(string contactId, string? asOf = null,
        int? eventsLimit = null, int? memoriesLimit = null, int? graphHops = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (asOf is not null) q.Add($"asOf={Uri.EscapeDataString(asOf)}");
        if (eventsLimit is not null) q.Add($"eventsLimit={eventsLimit}");
        if (memoriesLimit is not null) q.Add($"memoriesLimit={memoriesLimit}");
        if (graphHops is not null) q.Add($"graphHops={graphHops}");
        var path = $"lattice/contacts/{Uri.EscapeDataString(contactId)}/context"
            + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return c.Send<LatticeContextBundle>(HttpMethod.Get, path, null, options, ct);
    }

    // ── Monitor ─────────────────────────────────────────────────────────────

    /// <summary>Manually run the pattern-break monitor tick. The platform runs
    /// this on a cron; manual triggers exist for debugging and tests that need an
    /// overdue pattern to fire immediately.</summary>
    public Task<MonitorTickResult> RunMonitorTickAsync(RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<MonitorTickResult>(HttpMethod.Post, "lattice/monitor/tick",
            new Dictionary<string, object?>(), options, ct);

    /// <summary>Monitor health: timestamp of the last tick and a count of patterns
    /// due for the next check — a liveness probe for the pattern-break dispatcher.</summary>
    public Task<MonitorStatus> GetMonitorStatusAsync(RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<MonitorStatus>(HttpMethod.Get, "lattice/monitor/status", null, options, ct);

    // ── Predict ─────────────────────────────────────────────────────────────

    /// <summary>Predict for a subject. Two modes:
    /// <list type="number">
    /// <item><b>Pattern projection (default).</b> Omit <paramref name="target"/> to
    /// project the subject's active behavior patterns forward.</item>
    /// <item><b>Declared target (v2).</b> Pass a <paramref name="target"/> to
    /// predict anything from the subject's observation history; the estimate
    /// arrives in <c>PredictResult.TargetPrediction</c> with first-class
    /// abstention. Prefer the typed <see cref="PredictTargetAsync"/> helper.</item>
    /// </list></summary>
    public Task<PredictResult> PredictAsync(Subject subject, int? horizonDays = null,
        int? limit = null, double? minConfidence = null, int? occurrencesPerPattern = null,
        bool? emitEvents = null, int? imminentWithinHours = null, PredictionTarget? target = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["subject"] = subject };
        if (horizonDays is not null) body["horizonDays"] = horizonDays;
        if (limit is not null) body["limit"] = limit;
        if (minConfidence is not null) body["minConfidence"] = minConfidence;
        if (occurrencesPerPattern is not null) body["occurrencesPerPattern"] = occurrencesPerPattern;
        if (emitEvents is not null) body["emitEvents"] = emitEvents;
        if (imminentWithinHours is not null) body["imminentWithinHours"] = imminentWithinHours;
        if (target is not null) body["target"] = target;
        return c.Send<PredictResult>(HttpMethod.Post, "lattice/predict", body, options, ct);
    }

    /// <summary>v2 general prediction, typed: declare *what* to predict and get back
    /// the single calibrated <see cref="TargetPrediction"/> (or an abstention).
    /// Thin wrapper over <see cref="PredictAsync"/>. Always check
    /// <c>Abstained</c> before reading a value — an abstention means "unknown",
    /// never "low risk". If the engine returns none, a synthetic abstention is
    /// returned so callers never have to null-check.</summary>
    public async Task<TargetPrediction> PredictTargetAsync(Subject subject, PredictionTarget target,
        int? horizonDays = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var result = await PredictAsync(subject, horizonDays: horizonDays, target: target,
            options: options, ct: ct).ConfigureAwait(false);
        return result.TargetPrediction ?? new TargetPrediction(
            TargetKind: target.Kind,
            EventType: target.EventType ?? target.AttributeKey ?? "",
            Probability: 0, ProbabilityLower: 0, ProbabilityUpper: 0,
            Value: 0, ValueLower: 0, ValueUpper: 0,
            ExpectedAt: "", ExpectedAtLower: "", ExpectedAtUpper: "",
            DaysUntil: 0, AnomalyScore: 0, IsAnomaly: false,
            Abstained: true,
            AbstentionReason: string.IsNullOrEmpty(result.AbstentionReason)
                ? "insufficient_signal: engine returned no target estimate"
                : result.AbstentionReason!,
            Explanation: "", EvidenceMemoryIds: []);
    }

    // ── Profile / cohort ────────────────────────────────────────────────────

    /// <summary>Behavioral profile snapshot for a subject — RFM segment + top
    /// entity + cadence summary + risk indicators. Non-temporal counterpart to
    /// <see cref="PredictAsync"/>: predict answers "what will they do?",
    /// getProfile answers "who are they?".</summary>
    public Task<SubjectProfile> GetProfileAsync(Subject subject, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<SubjectProfile>(HttpMethod.Post, "lattice/profile",
            new Dictionary<string, object?> { ["subject"] = subject }, options, ct);

    /// <summary>Find subjects whose behavior looks similar to the target — top-K
    /// nearest neighbors ranked by a blended similarity score (RFM + entity
    /// Jaccard + pattern-kind Jaccard).</summary>
    public Task<GetCohortResponse> GetCohortAsync(Subject subject, int? k = null,
        double? minSimilarity = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["subject"] = subject };
        if (k is not null) body["k"] = k;
        if (minSimilarity is not null) body["minSimilarity"] = minSimilarity;
        return c.Send<GetCohortResponse>(HttpMethod.Post, "lattice/cohort", body, options, ct);
    }

    /// <summary>Cohort-aware predictions — "people like the target also did X."
    /// Computes the target's cohort, then aggregates each member's predictions
    /// weighted by similarity. Every prediction carries <c>SupportingSubjects</c>
    /// + <c>SourceMemoryIds</c> so the answer is fully traceable.</summary>
    public Task<PredictByCohortResponse> PredictByCohortAsync(Subject subject, int? cohortK = null,
        int? predictionLimit = null, double? minSimilarity = null, RequestOptions? options = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["subject"] = subject };
        if (cohortK is not null) body["cohortK"] = cohortK;
        if (predictionLimit is not null) body["predictionLimit"] = predictionLimit;
        if (minSimilarity is not null) body["minSimilarity"] = minSimilarity;
        return c.Send<PredictByCohortResponse>(HttpMethod.Post, "lattice/cohort/predict", body, options, ct);
    }

    // ── Estimate / calibration ──────────────────────────────────────────────

    /// <summary>Run a deterministic estimator (e.g. PhenoAge biological age) over a
    /// subject's biomarker signals. Returns a wellness estimate with per-signal
    /// contributors and a not-a-diagnosis disclaimer — never a medical verdict.
    /// Pass <paramref name="persist"/> to store the score as a memory so it builds
    /// a trajectory and feeds the calibration loop.</summary>
    public Task<EstimateResult> EstimateAsync(Subject subject, string estimatorId,
        bool? persist = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["subject"] = subject, ["estimatorId"] = estimatorId,
        };
        if (persist is not null) body["persist"] = persist;
        return c.Send<EstimateResult>(HttpMethod.Post, "lattice/estimate", body, options, ct);
    }

    /// <summary>Prediction calibration report: confidence buckets mapped to
    /// realized hit-rates. Tells you whether "80% confident" predictions actually
    /// fire ~80% of the time.</summary>
    public Task<CalibrationReport> GetCalibrationAsync(int? bucketCount = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var path = "lattice/calibration" + (bucketCount is not null ? $"?bucketCount={bucketCount}" : "");
        return c.Send<CalibrationReport>(HttpMethod.Get, path, null, options, ct);
    }
}
