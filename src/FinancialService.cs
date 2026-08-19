using System.Globalization;

namespace MemMesh;

/// <summary>Financial — technical indicators, portfolio risk, and a
/// self-calibrating directional prediction loop for the engine's financial
/// vertical. Financial data IS memory data: ingest price bars, fundamentals,
/// holdings, and news as fact memories (stored via <c>/admin/memory</c>) and the
/// engine derives indicators, risk, and buy/sell/hold calls whose reported
/// confidence is structural agreement × realized hit-rate. Score due calls with
/// <see cref="ReconcileAsync"/> and inspect honesty with
/// <see cref="GetCalibrationAsync"/> at <c>/lattice/financial/*</c>.
///
/// Requires the <c>@thinkfleet/pack-financial</c> pack; the read methods return
/// FAILED_PRECONDITION otherwise (ingestion works regardless — it's plain
/// memory). Mirrors the TS reference surface (<c>tf.financial.*</c>).
/// Informational only — NOT investment advice.
///
/// Market data (prices / fundamentals / news) is pooled across the project;
/// holdings are private to their subject. A <c>subject</c> is caller-asserted —
/// the engine does not verify ownership, so only ever pass a subject the current
/// user is entitled to.</summary>
public sealed class FinancialService(MemMeshClient c)
{
    // ── Input — ingest market data + positions (stored as fact memories) ──

    /// <summary>Ingest a single price bar. Market data — not subject-attributed.</summary>
    public Task<MemoryItem> IngestPriceAsync(PriceInput price, RequestOptions? options = null,
        CancellationToken ct = default)
    {
        var content = $"{price.Ticker} close {price.Close.ToString(CultureInfo.InvariantCulture)}"
            + (price.AsOf is not null ? $" @ {price.AsOf}" : "");
        return RecordAsync(content, new Dictionary<string, object?> { ["price"] = price }, options, ct);
    }

    /// <summary>Ingest many price bars (e.g. a backfill). Issued concurrently;
    /// resolves once all are stored. For very large histories, batch yourself.</summary>
    public async Task<List<MemoryItem>> IngestPricesAsync(IEnumerable<PriceInput> prices,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var results = await Task.WhenAll(prices.Select(p => IngestPriceAsync(p, options, ct)))
            .ConfigureAwait(false);
        return results.ToList();
    }

    /// <summary>Ingest/refresh a ticker's fundamentals. Latest values win.</summary>
    public Task<MemoryItem> IngestFundamentalsAsync(FundamentalInput fundamental,
        RequestOptions? options = null, CancellationToken ct = default)
        => RecordAsync($"Fundamentals {fundamental.Ticker}",
            new Dictionary<string, object?> { ["fundamental"] = fundamental }, options, ct);

    /// <summary>Record a portfolio position. Subject-private — attributed to the
    /// owner (use a <c>{ kind: "portfolio", externalId }</c> subject). Restated,
    /// not summed: re-recording a ticker replaces the prior position.</summary>
    public Task<MemoryItem> IngestHoldingAsync(Subject subject, HoldingInput holding,
        RequestOptions? options = null, CancellationToken ct = default)
        => RecordAsync($"Holding {holding.Shares.ToString(CultureInfo.InvariantCulture)} {holding.Ticker}",
            new Dictionary<string, object?> { ["subject"] = subject, ["holding"] = holding }, options, ct);

    /// <summary>Ingest a news event. Market data — tag one or many tickers.</summary>
    public Task<MemoryItem> IngestNewsAsync(NewsInput news, RequestOptions? options = null,
        CancellationToken ct = default)
    {
        var label = news.Ticker
            ?? (news.Tickers is { Count: > 0 } t ? string.Join(",", t) : null)
            ?? "news";
        return RecordAsync($"News [{label}]: {news.Headline}",
            new Dictionary<string, object?> { ["newsEvent"] = news }, options, ct);
    }

    // ── Read — indicators, risk, calibrated predictions ──

    /// <summary>Technical indicators + (for a portfolio subject) a risk rollup,
    /// derived from ingested market data and holdings. Read-only and forecast-free.
    /// <c>subject.kind == "ticker"</c> → single-name analysis (externalId is the
    /// ticker); any other kind → portfolio mode over the subject's holdings.</summary>
    public Task<FinancialProfile> GetProfileAsync(Subject subject, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<FinancialProfile>(HttpMethod.Post, "lattice/financial/profile",
            new Dictionary<string, object?> { ["subject"] = subject }, options, ct);

    /// <summary>Generate directional buy/sell/hold calls. Reported confidence =
    /// structural agreement × the strategy's realized reliability. By default each
    /// call is persisted so it can be scored at horizon by
    /// <see cref="ReconcileAsync"/>. <paramref name="horizonDays"/> defaults to 30
    /// (clamped [1, 365]); <paramref name="persist"/> defaults to true.</summary>
    public Task<PredictFinancialResult> PredictAsync(Subject subject, int? horizonDays = null,
        bool? persist = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["subject"] = subject };
        if (horizonDays is not null) body["horizonDays"] = horizonDays;
        if (persist is not null) body["persist"] = persist;
        return c.Send<PredictFinancialResult>(HttpMethod.Post, "lattice/financial/predict", body, options, ct);
    }

    /// <summary>Run the feedback loop: score every persisted prediction whose horizon
    /// has elapsed against the realized close, and mark it resolved. Recomputes
    /// reliability and calibration from these outcomes. Idempotent; safe to run on a
    /// schedule.</summary>
    public Task<ReconcileFinancialResult> ReconcileAsync(RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<ReconcileFinancialResult>(HttpMethod.Post, "lattice/financial/reconcile",
            new Dictionary<string, object?>(), options, ct);

    /// <summary>The honesty proof: resolved predictions bucketed by the confidence we
    /// reported, with the realized hit-rate per band. <paramref name="bucketCount"/>
    /// defaults to 5 (clamped [1, 20]); pass <paramref name="strategy"/> to filter to
    /// one strategy, omit for all.</summary>
    public Task<FinancialCalibrationReport> GetCalibrationAsync(int? bucketCount = null,
        string? strategy = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>();
        if (bucketCount is not null) body["bucketCount"] = bucketCount;
        if (strategy is not null) body["strategy"] = strategy;
        return c.Send<FinancialCalibrationReport>(HttpMethod.Post, "lattice/financial/calibration",
            body, options, ct);
    }

    // Financial signals are plain fact memories (category "financial", source
    // "sdk:financial"), so ingestion works even without the financial pack enabled.
    private Task<MemoryItem> RecordAsync(string content, Dictionary<string, object?> metadata,
        RequestOptions? options, CancellationToken ct)
        => c.Send<MemoryItem>(HttpMethod.Post, "admin/memory", new Dictionary<string, object?>
        {
            ["content"] = content,
            ["type"] = "fact",
            ["scope"] = "project",
            ["category"] = "financial",
            ["source"] = "sdk:financial",
            ["metadata"] = metadata,
        }, options, ct);
}
