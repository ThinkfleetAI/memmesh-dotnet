namespace MemMesh;

/// <summary>Per-call overrides threaded through the core send path. Pass one to
/// any resource method that accepts a <see cref="RequestOptions"/> to redirect a
/// single call to a different project or give it a different timeout, without
/// building a second client.</summary>
public sealed record RequestOptions
{
    /// <summary>Override the client's default project ID for this one call.</summary>
    public string? ProjectId { get; init; }

    /// <summary>Override the client's default request timeout for this one call.</summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>Runs before each request leaves the client. Mutate the request in
/// place to add headers or swap the bearer token — e.g. exchange the API key for
/// a short-lived Cognito JWT:
/// <code>
/// client.RequestInterceptors.Add(async (req, ct) =>
///     req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetJwtAsync(ct)));
/// </code></summary>
public delegate Task RequestInterceptor(HttpRequestMessage request, CancellationToken ct);

/// <summary>Runs after each response arrives, before its body is read or parsed.
/// Useful for logging, metrics, or inspecting response headers.</summary>
public delegate Task ResponseInterceptor(HttpResponseMessage response, CancellationToken ct);
