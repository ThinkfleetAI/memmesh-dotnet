using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MemMesh;

/// <summary>
/// Official .NET client for MemMesh — memory + prediction for AI agents.
/// <code>
/// var mm = new MemMeshClient("sk-...", "proj_...");
/// await mm.Memory.ObserveAsync(text: "Sarah prefers email over phone.");
/// var hits = await mm.Memory.SearchAsync("how to reach sarah", limit: 5);
/// </code>
/// Requests to 429 and 5xx are retried with exponential backoff and jitter
/// (honoring <c>Retry-After</c>); non-2xx responses surface as typed subclasses
/// of <see cref="MemMeshException"/>.
/// </summary>
public sealed class MemMeshClient : IDisposable
{
    internal readonly HttpClient Http;
    internal readonly string ProjectId;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly int _maxRetries;
    private readonly TimeSpan _timeout;
    private readonly bool _ownsHttp;

    /// <summary>Middleware run before each request — mutate the request to add
    /// headers or swap the bearer (e.g. for a Cognito JWT). Applied in order.</summary>
    public IList<RequestInterceptor> RequestInterceptors { get; } = new List<RequestInterceptor>();

    /// <summary>Middleware run after each response arrives, before its body is
    /// parsed. Applied in order.</summary>
    public IList<ResponseInterceptor> ResponseInterceptors { get; } = new List<ResponseInterceptor>();

    public MemoryService Memory { get; }
    public LatticeService Lattice { get; }
    public ContextService Context { get; }
    public EventsService Events { get; }
    public AlertsService Alerts { get; }
    public LearningService Learning { get; }
    public BehaviorsService Behaviors { get; }
    public TypedService Typed { get; }
    public ComplianceService Compliance { get; }
    public HealthService Health { get; }
    public FinancialService Financial { get; }
    public BrainsService Brains { get; }
    public ConsentService Consent { get; }
    /// <summary>The knowledge graph extraction builds from observed memory.</summary>
    public GraphService Graph { get; }

    /// <param name="apiKey">Project API key (<c>sk-...</c>).</param>
    /// <param name="projectId">Default project for all calls; override per-call via <see cref="RequestOptions"/>.</param>
    /// <param name="baseUrl">API root. Default <c>https://app.memmesh.ai</c>.</param>
    /// <param name="httpClient">Bring your own <see cref="HttpClient"/> (its lifetime stays yours). When omitted the client owns one.</param>
    /// <param name="maxRetries">Retries on 429 + 5xx + network errors. Default 2.</param>
    /// <param name="timeout">Default per-request timeout. Default 30s.</param>
    public MemMeshClient(string apiKey, string projectId,
        string baseUrl = "https://app.memmesh.ai", HttpClient? httpClient = null,
        int maxRetries = 2, TimeSpan? timeout = null)
    {
        if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("apiKey is required");
        if (string.IsNullOrEmpty(projectId)) throw new ArgumentException("projectId is required");

        _apiKey = apiKey;
        ProjectId = projectId;
        _baseUrl = baseUrl.TrimEnd('/');
        _maxRetries = Math.Max(0, maxRetries);
        _timeout = timeout ?? TimeSpan.FromSeconds(30);

        _ownsHttp = httpClient is null;
        Http = httpClient ?? new HttpClient();
        // Per-call timeouts are enforced with a linked CancellationTokenSource, so
        // the HttpClient's own global timeout must not pre-empt them.
        if (_ownsHttp) Http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        Memory = new MemoryService(this);
        Lattice = new LatticeService(this);
        Context = new ContextService(this);
        Events = new EventsService(this);
        Alerts = new AlertsService(this);
        Learning = new LearningService(this);
        Behaviors = new BehaviorsService(this);
        Typed = new TypedService(this);
        Compliance = new ComplianceService(this);
        Health = new HealthService(this);
        Financial = new FinancialService(this);
        Brains = new BrainsService(this);
        Consent = new ConsentService(this);
        Graph = new GraphService(this);
    }

    // Relative to the project scope (no leading slash on `path`).
    internal Task<T> Send<T>(HttpMethod method, string path, object? body = null,
        CancellationToken ct = default)
        => Send<T>(method, path, body, null, ct);

    /// <summary>Core send with per-call <see cref="RequestOptions"/> (project /
    /// timeout override). Every resource method funnels through here.</summary>
    internal async Task<T> Send<T>(HttpMethod method, string path, object? body,
        RequestOptions? options, CancellationToken ct = default)
    {
        var text = await SendRaw(method, path, body, options, ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(text)
            ? default!
            : JsonSerializer.Deserialize<T>(text, Json)!;
    }

    internal Task SendVoid(HttpMethod method, string path, object? body = null,
        CancellationToken ct = default) => Send<object?>(method, path, body, null, ct);

    internal Task SendVoid(HttpMethod method, string path, object? body,
        RequestOptions? options, CancellationToken ct = default)
        => Send<object?>(method, path, body, options, ct);

    /// <summary>Fetch one seek-paginated page, appending <paramref name="cursor"/>
    /// when present. Resources compose this with
    /// <see cref="Pagination.ListAllAsync{T}"/>.</summary>
    internal Task<SeekPage<T>> GetPageAsync<T>(string path, string? cursor,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var full = cursor is null
            ? path
            : $"{path}{(path.Contains('?') ? '&' : '?')}cursor={Uri.EscapeDataString(cursor)}";
        return Send<SeekPage<T>>(HttpMethod.Get, full, null, options, ct);
    }

    /// <summary>Stream every row of a seek-paginated endpoint, following cursors
    /// until exhausted.</summary>
    public IAsyncEnumerable<T> ListAllAsync<T>(string path,
        RequestOptions? options = null, CancellationToken ct = default)
        => Pagination.ListAllAsync<T>((cursor, token) => GetPageAsync<T>(path, cursor, options, token), ct);

    // Retry loop, interceptors, per-call timeout, and status → typed-exception mapping.
    private async Task<string> SendRaw(HttpMethod method, string path, object? body,
        RequestOptions? options, CancellationToken ct)
    {
        var projectId = options?.ProjectId ?? ProjectId;
        var timeout = options?.Timeout ?? _timeout;
        var url = $"{_baseUrl}/api/v1/projects/{projectId}/{path}";

        MemMeshException? lastError = null;

        for (var attempt = 0; attempt <= _maxRetries; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(BackoffDelay(attempt, lastError), ct).ConfigureAwait(false);

            using var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            if (body is not null) req.Content = JsonContent.Create(body, options: Json);

            foreach (var interceptor in RequestInterceptors)
                await interceptor(req, ct).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            HttpResponseMessage resp;
            try
            {
                resp = await Http.SendAsync(req, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // A slow endpoint is unlikely to answer faster on an immediate
                // retry — surface the timeout rather than burning attempts.
                throw new RequestTimeoutException($"Request timed out after {timeout.TotalMilliseconds:0}ms");
            }
            catch (HttpRequestException ex)
            {
                lastError = new NetworkException($"Network error: {ex.Message}");
                if (attempt < _maxRetries) continue;
                throw lastError;
            }

            using (resp)
            {
                foreach (var interceptor in ResponseInterceptors)
                    await interceptor(resp, ct).ConfigureAwait(false);

                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode) return text;

                var status = (int)resp.StatusCode;
                var (message, code, prms) = ParseError(text, status);

                if (status == 429)
                {
                    lastError = new RateLimitException(message, ParseRetryAfter(resp));
                    if (attempt < _maxRetries) continue;
                    throw lastError;
                }

                if (status >= 500)
                {
                    lastError = new ServerException(status, message);
                    if (attempt < _maxRetries) continue;
                    throw lastError;
                }

                throw status switch
                {
                    401 => new AuthenticationException(message),
                    403 => new AuthorizationException(message),
                    404 => new NotFoundException(message),
                    400 or 422 => new ValidationException(status, message, prms),
                    _ => new MemMeshException(status, message, code, prms),
                };
            }
        }

        throw lastError ?? new MemMeshException(0, "Request failed");
    }

    // TS parity: min(500 * 2^(attempt-1), 10000) ms, jittered to 50–100% of that.
    // A server-advised Retry-After (capped at 30s) wins when present.
    private static TimeSpan BackoffDelay(int attempt, MemMeshException? lastError)
    {
        if (lastError is RateLimitException { RetryAfter: { } ra } && ra > TimeSpan.Zero)
            return ra <= TimeSpan.FromSeconds(30) ? ra : TimeSpan.FromSeconds(30);

        var baseMs = Math.Min(500d * Math.Pow(2, attempt - 1), 10000d);
        var jittered = baseMs * (0.5 + Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(jittered);
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra is null) return null;
        if (ra.Delta is { } d) return d;
        if (ra.Date is { } date)
        {
            var diff = date - DateTimeOffset.UtcNow;
            return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
        }
        return null;
    }

    private static (string message, string code, IReadOnlyDictionary<string, object?>? prms)
        ParseError(string body, int status)
    {
        var message = $"HTTP {status}";
        var code = "UNKNOWN";
        IReadOnlyDictionary<string, object?>? prms = null;

        if (string.IsNullOrWhiteSpace(body)) return (message, code, prms);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (body, code, prms);

            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                message = m.GetString()!;
            else if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                message = e.GetString()!;

            if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String)
                code = c.GetString()!;

            if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
                prms = p.Deserialize<Dictionary<string, object?>>(Json);
        }
        catch (JsonException)
        {
            message = body;
        }

        return (message, code, prms);
    }

    public void Dispose()
    {
        if (_ownsHttp) Http.Dispose();
    }
}
