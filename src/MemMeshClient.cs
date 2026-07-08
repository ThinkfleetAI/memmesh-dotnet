using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MemMesh;

/// <summary>
/// Official .NET client for MemMesh — memory + prediction for AI agents.
/// <code>
/// var mm = new MemMeshClient("sk-...", "proj_...");
/// await mm.Memory.ObserveAsync("Prefers email over phone.",
///     subject: new Subject("contact", "sarah"));
/// var hits = await mm.Memory.SearchAsync("how to reach sarah", limit: 5);
/// </code>
/// </summary>
public sealed class MemMeshClient : IDisposable
{
    internal readonly HttpClient Http;
    internal readonly string ProjectId;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public MemoryService Memory { get; }
    public LatticeService Lattice { get; }
    public ContextService Context { get; }
    public EventsService Events { get; }
    public AlertsService Alerts { get; }
    public LearningService Learning { get; }
    public TypedService Typed { get; }
    public ComplianceService Compliance { get; }
    public HealthService Health { get; }

    public MemMeshClient(string apiKey, string projectId,
        string baseUrl = "https://memory.thinkfleet.ai", HttpClient? httpClient = null)
    {
        if (string.IsNullOrEmpty(apiKey)) throw new ArgumentException("apiKey is required");
        if (string.IsNullOrEmpty(projectId)) throw new ArgumentException("projectId is required");
        ProjectId = projectId;
        Http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        Http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/v1/projects/" + projectId + "/");
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        Memory = new MemoryService(this);
        Lattice = new LatticeService(this);
        Context = new ContextService(this);
        Events = new EventsService(this);
        Alerts = new AlertsService(this);
        Learning = new LearningService(this);
        Typed = new TypedService(this);
        Compliance = new ComplianceService(this);
        Health = new HealthService(this);
    }

    // Relative to the project-scoped BaseAddress (no leading slash).
    internal async Task<T> Send<T>(HttpMethod method, string path, object? body = null,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(method, path);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new MemMeshException((int)resp.StatusCode, text);
        return string.IsNullOrEmpty(text)
            ? default!
            : JsonSerializer.Deserialize<T>(text, Json)!;
    }

    internal Task SendVoid(HttpMethod method, string path, object? body = null,
        CancellationToken ct = default) => Send<object?>(method, path, body, ct);

    public void Dispose() => Http.Dispose();
}

/// <summary>A non-2xx response from the MemMesh API.</summary>
public sealed class MemMeshException(int status, string body)
    : Exception($"memmesh: {status} {body}")
{
    public int Status { get; } = status;
    public string Body { get; } = body;
}
