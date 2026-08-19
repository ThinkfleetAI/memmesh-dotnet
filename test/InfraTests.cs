using System.Net;
using System.Net.Http;
using Xunit;

namespace MemMesh.Tests;

public class InfraTests
{
    private const string ItemJson =
        """{"id":"m1","type":"fact","content":"hi","importance":5,"scope":"project","status":"active","confidence":0.9,"supersededById":null}""";

    private static MemMeshClient Client(HttpMessageHandler handler, int maxRetries = 2, TimeSpan? timeout = null) =>
        new("sk-test", "proj_1", "https://example.test", new HttpClient(handler),
            maxRetries: maxRetries, timeout: timeout);

    // ── Retry / backoff ───────────────────────────────────────────────────

    [Fact]
    public async Task Retries_429_then_succeeds()
    {
        var handler = new StubHandler(
            _ => StubHandler.Json(HttpStatusCode.TooManyRequests, "{}"),
            _ => StubHandler.Json(HttpStatusCode.OK, ItemJson));
        using var mm = Client(handler);

        var item = await mm.Memory.GetAsync("m1");

        Assert.Equal("m1", item.Id);
        Assert.Equal(2, handler.Calls); // one retry
    }

    [Fact]
    public async Task Retries_5xx_up_to_maxRetries_then_throws_ServerException()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.BadGateway, "{}"));
        using var mm = Client(handler, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<ServerException>(() => mm.Memory.GetAsync("m1"));

        Assert.Equal(502, ex.Status);
        Assert.Equal(3, handler.Calls); // initial + 2 retries
    }

    [Fact]
    public async Task RateLimit_surfaces_RetryAfter_and_does_not_retry_when_maxRetries_zero()
    {
        var handler = new StubHandler(_ =>
        {
            var r = StubHandler.Json(HttpStatusCode.TooManyRequests, "{}");
            r.Headers.Add("Retry-After", "2");
            return r;
        });
        using var mm = Client(handler, maxRetries: 0);

        var ex = await Assert.ThrowsAsync<RateLimitException>(() => mm.Memory.GetAsync("m1"));

        Assert.Equal(TimeSpan.FromSeconds(2), ex.RetryAfter);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Timeout_is_not_retried_and_throws_RequestTimeoutException()
    {
        var slow = new SlowHandler();
        using var mm = Client(slow, maxRetries: 2, timeout: TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAsync<RequestTimeoutException>(() => mm.Memory.GetAsync("m1"));
        Assert.Equal(1, slow.Calls); // timeouts are not retried
    }

    // ── Typed error mapping ───────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(AuthenticationException))]
    [InlineData(HttpStatusCode.Forbidden, typeof(AuthorizationException))]
    [InlineData(HttpStatusCode.NotFound, typeof(NotFoundException))]
    public async Task Maps_status_to_typed_exception(HttpStatusCode status, Type expected)
    {
        var handler = new StubHandler(_ => StubHandler.Json(status, """{"message":"nope"}"""));
        using var mm = Client(handler);

        var ex = await Assert.ThrowsAnyAsync<MemMeshException>(() => mm.Memory.GetAsync("m1"));
        Assert.IsType(expected, ex);
        Assert.Equal("nope", ex.Body);
    }

    [Fact]
    public async Task Validation_422_carries_params()
    {
        var handler = new StubHandler(_ => StubHandler.Json(
            (HttpStatusCode)422, """{"message":"bad","params":{"field":"name"}}"""));
        using var mm = Client(handler);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => mm.Memory.GetAsync("m1"));
        Assert.NotNull(ex.Params);
        Assert.True(ex.Params!.ContainsKey("field"));
    }

    // ── Interceptors ──────────────────────────────────────────────────────

    [Fact]
    public async Task RequestInterceptor_can_swap_the_bearer_token()
    {
        var handler = new StubHandler(_ => StubHandler.Json(HttpStatusCode.OK, ItemJson));
        using var mm = Client(handler);
        mm.RequestInterceptors.Add((req, _) =>
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "jwt-123");
            return Task.CompletedTask;
        });

        await mm.Memory.GetAsync("m1");

        Assert.Equal("Bearer jwt-123", handler.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ResponseInterceptor_runs_on_each_response()
    {
        var seen = 0;
        var handler = new StubHandler(
            _ => StubHandler.Json(HttpStatusCode.ServiceUnavailable, "{}"),
            _ => StubHandler.Json(HttpStatusCode.OK, ItemJson));
        using var mm = Client(handler);
        mm.ResponseInterceptors.Add((_, _) => { seen++; return Task.CompletedTask; });

        await mm.Memory.GetAsync("m1");

        Assert.Equal(2, seen); // once per response, including the retried 503
    }

    // ── Pagination ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAll_follows_cursors_and_flattens()
    {
        var page1 = $$"""{"data":[{{ItemJson}}],"next":"c2","previous":null}""";
        var page2 = $$"""{"data":[{{ItemJson}}],"next":null,"previous":"c1"}""";
        var handler = new StubHandler(
            _ => StubHandler.Json(HttpStatusCode.OK, page1),
            _ => StubHandler.Json(HttpStatusCode.OK, page2));
        using var mm = Client(handler);

        var all = new List<MemoryItem>();
        await foreach (var item in mm.ListAllAsync<MemoryItem>("admin/memory"))
            all.Add(item);

        Assert.Equal(2, all.Count);
        Assert.Equal(2, handler.Calls);
        Assert.DoesNotContain("cursor", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("cursor=c2", handler.Requests[1].RequestUri!.Query);
    }

    // ── Per-call options ──────────────────────────────────────────────────

    [Fact]
    public async Task RequestOptions_overrides_projectId_in_url()
    {
        var handler = new StubHandler(_ =>
            StubHandler.Json(HttpStatusCode.OK, """{"data":[],"next":null,"previous":null}"""));
        using var mm = Client(handler);

        await foreach (var _ in mm.ListAllAsync<MemoryItem>("admin/memory",
            new RequestOptions { ProjectId = "other_proj" })) { }

        Assert.Contains("/projects/other_proj/", handler.Requests[0].RequestUri!.AbsolutePath);
    }
}

/// <summary>Blocks until its request is cancelled, so the client's per-call
/// timeout fires.</summary>
internal sealed class SlowHandler : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
