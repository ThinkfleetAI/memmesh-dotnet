using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace MemMesh.Tests;

/// <summary>Route + payload coverage for the Financial surface (9/9), mirrored
/// from thinkfleet-memory-sdk/src/resources/financial.ts. Ingestion records fact
/// memories via <c>/admin/memory</c>; reads sit under <c>/lattice/financial/*</c>.</summary>
public class FinancialServiceTests
{
    private static MemMeshClient Client(HttpMessageHandler handler) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler), maxRetries: 0);

    private static string BodyOf(HttpRequestMessage req) =>
        req.Content is null ? "" : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private const string MemJson =
        """{"id":"m1","type":"fact","content":"x","importance":5,"scope":"project","status":"active","confidence":1,"supersededById":null}""";

    // ── ingestPrice ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestPrice_posts_fact_memory_with_price_metadata()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, MemJson); });
        using var mm = Client(handler);

        await mm.Financial.IngestPriceAsync(new PriceInput("AAPL", 150, AsOf: "2026-01-01"));

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/admin/memory", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("AAPL close 150 @ 2026-01-01", root.GetProperty("content").GetString());
        Assert.Equal("financial", root.GetProperty("category").GetString());
        Assert.Equal("sdk:financial", root.GetProperty("source").GetString());
        Assert.Equal("AAPL", root.GetProperty("metadata").GetProperty("price").GetProperty("ticker").GetString());
    }

    // ── ingestPrices (concurrent batch) ─────────────────────────────────────────

    [Fact]
    public async Task IngestPrices_posts_each_bar_and_returns_all()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, MemJson));
        using var mm = Client(handler);

        var items = await mm.Financial.IngestPricesAsync([
            new PriceInput("AAPL", 150), new PriceInput("AAPL", 151), new PriceInput("AAPL", 152)]);

        Assert.Equal(3, items.Count);
        Assert.Equal(3, handler.Calls);
        Assert.All(handler.Requests, r => Assert.EndsWith("/admin/memory", r.RequestUri!.AbsolutePath));
    }

    // ── ingestFundamentals ──────────────────────────────────────────────────────

    [Fact]
    public async Task IngestFundamentals_posts_fundamental_metadata()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, MemJson); });
        using var mm = Client(handler);

        await mm.Financial.IngestFundamentalsAsync(new FundamentalInput("AAPL", PeRatio: 28.5, MarketCap: 3_000_000));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Fundamentals AAPL", doc.RootElement.GetProperty("content").GetString());
        var f = doc.RootElement.GetProperty("metadata").GetProperty("fundamental");
        Assert.Equal(28.5, f.GetProperty("peRatio").GetDouble());
        Assert.False(f.TryGetProperty("beta", out _)); // unset optional omitted
    }

    // ── ingestHolding ───────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestHolding_posts_subject_and_holding_metadata()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, MemJson); });
        using var mm = Client(handler);

        await mm.Financial.IngestHoldingAsync(new Subject("portfolio", "acct-123"),
            new HoldingInput("AAPL", 100, CostBasis: 150));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("Holding 100 AAPL", root.GetProperty("content").GetString());
        Assert.Equal("acct-123", root.GetProperty("metadata").GetProperty("subject").GetProperty("externalId").GetString());
        Assert.Equal(100, root.GetProperty("metadata").GetProperty("holding").GetProperty("shares").GetDouble());
    }

    // ── ingestNews ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestNews_labels_content_with_single_ticker()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, MemJson); });
        using var mm = Client(handler);

        await mm.Financial.IngestNewsAsync(new NewsInput("Apple beats earnings", Ticker: "AAPL", Sentiment: 0.7));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("News [AAPL]: Apple beats earnings", doc.RootElement.GetProperty("content").GetString());
        Assert.Equal("AAPL", doc.RootElement.GetProperty("metadata").GetProperty("newsEvent").GetProperty("ticker").GetString());
    }

    [Fact]
    public async Task IngestNews_joins_multiple_tickers_for_label()
    {
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, MemJson); });
        using var mm = Client(handler);

        await mm.Financial.IngestNewsAsync(new NewsInput("Sector rally", Tickers: ["AAPL", "MSFT"]));

        using var doc = JsonDocument.Parse(body);
        Assert.Equal("News [AAPL,MSFT]: Sector rally", doc.RootElement.GetProperty("content").GetString());
    }

    // ── getProfile ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetProfile_posts_lattice_financial_profile_and_parses()
    {
        const string json =
            """{"subject":{"kind":"ticker","externalId":"AAPL"},"indicators":[{"ticker":"AAPL","lastClose":152,"asOf":"2026-01-03","betaSource":"computed","sampleSize":200,"sourceMemoryIds":["m1"],"rsi14":61.2,"beta":1.1}],"fundamentals":[],"positions":[],"unpricedHoldings":[],"disclaimer":"not advice","generatedAt":"2026-01-03T00:00:00Z"}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, json); });
        using var mm = Client(handler);

        var profile = await mm.Financial.GetProfileAsync(new Subject("ticker", "AAPL"));

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/financial/profile", req.RequestUri!.AbsolutePath);
        Assert.Equal("AAPL", JsonDocument.Parse(body).RootElement.GetProperty("subject").GetProperty("externalId").GetString());
        Assert.Single(profile.Indicators);
        Assert.Equal(152, profile.Indicators[0].LastClose);
        Assert.Equal(61.2, profile.Indicators[0].Rsi14);
        Assert.Equal("computed", profile.Indicators[0].BetaSource);
        Assert.Null(profile.PortfolioRisk);
    }

    // ── predict ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Predict_posts_lattice_financial_predict_with_options()
    {
        const string json =
            """{"signals":[{"ticker":"AAPL","strategy":"trend","direction":"buy","score":0.4,"structuralConfidence":0.6,"reportedConfidence":0.45,"expectedReturn":0.03,"horizonDays":30,"basisClose":152,"dueAt":"2026-02-02T00:00:00Z","rationale":["sma cross"],"newsUsed":true,"sourceMemoryIds":["m1"],"predictionId":"pred1"}],"strategy":"trend","strategyReliability":0.75,"resolvedSample":40,"disclaimer":"not advice","generatedAt":"2026-01-03T00:00:00Z"}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, json); });
        using var mm = Client(handler);

        var res = await mm.Financial.PredictAsync(new Subject("ticker", "AAPL"), horizonDays: 30, persist: true);

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/financial/predict", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(30, doc.RootElement.GetProperty("horizonDays").GetInt32());
        Assert.True(doc.RootElement.GetProperty("persist").GetBoolean());
        Assert.Single(res.Signals);
        Assert.Equal("buy", res.Signals[0].Direction);
        Assert.Equal("pred1", res.Signals[0].PredictionId);
        Assert.Equal(0.75, res.StrategyReliability);
    }

    [Fact]
    public async Task Predict_omits_unset_options()
    {
        const string json =
            """{"signals":[],"strategy":"trend","strategyReliability":1,"resolvedSample":0,"disclaimer":"d","generatedAt":"2026-01-03T00:00:00Z"}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, json); });
        using var mm = Client(handler);

        await mm.Financial.PredictAsync(new Subject("ticker", "AAPL"));

        using var doc = JsonDocument.Parse(body);
        Assert.False(doc.RootElement.TryGetProperty("horizonDays", out _));
        Assert.False(doc.RootElement.TryGetProperty("persist", out _));
    }

    // ── reconcile ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reconcile_posts_lattice_financial_reconcile()
    {
        const string json =
            """{"scored":5,"hits":3,"misses":2,"stillPending":7,"generatedAt":"2026-01-03T00:00:00Z"}""";
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, json));
        using var mm = Client(handler);

        var res = await mm.Financial.ReconcileAsync();

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/financial/reconcile", req.RequestUri!.AbsolutePath);
        Assert.Equal(5, res.Scored);
        Assert.Equal(3, res.Hits);
        Assert.Equal(7, res.StillPending);
    }

    // ── getCalibration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCalibration_posts_lattice_financial_calibration_with_options()
    {
        const string json =
            """{"buckets":[{"lower":0.6,"upper":0.8,"predictions":10,"hits":7,"misses":3,"realizedHitRate":0.7,"hasData":true}],"strategy":"trend","strategyReliability":0.8,"totalResolved":40,"generatedAt":"2026-01-03T00:00:00Z"}""";
        string body = "";
        var handler = new StubHandler(r => { body = BodyOf(r); return StubHandler.Json(HttpStatusCode.OK, json); });
        using var mm = Client(handler);

        var report = await mm.Financial.GetCalibrationAsync(bucketCount: 5, strategy: "trend");

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/lattice/financial/calibration", req.RequestUri!.AbsolutePath);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(5, doc.RootElement.GetProperty("bucketCount").GetInt32());
        Assert.Equal("trend", doc.RootElement.GetProperty("strategy").GetString());
        Assert.Single(report.Buckets);
        Assert.Equal(0.7, report.Buckets[0].RealizedHitRate);
        Assert.Equal(40, report.TotalResolved);
    }
}
