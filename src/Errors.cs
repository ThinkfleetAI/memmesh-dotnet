namespace MemMesh;

/// <summary>Base type for every error the MemMesh API surfaces. Catch this to
/// handle any failure uniformly; catch a subclass to branch on the HTTP status.
/// <c>Status</c> is the HTTP status (0 for transport-level failures),
/// <c>Code</c> the machine-readable error code, and <c>Params</c> any structured
/// validation detail the server returned.</summary>
public class MemMeshException : Exception
{
    public int Status { get; }
    public string Body { get; }
    public string Code { get; }
    public IReadOnlyDictionary<string, object?>? Params { get; }

    public MemMeshException(int status, string body, string code = "UNKNOWN",
        IReadOnlyDictionary<string, object?>? @params = null)
        : base($"memmesh: {status} {body}")
    {
        Status = status;
        Body = body;
        Code = code;
        Params = @params;
    }
}

/// <summary>401 — invalid or missing API key.</summary>
public sealed class AuthenticationException(string body = "Invalid or missing API key")
    : MemMeshException(401, body, "AUTHENTICATION_ERROR");

/// <summary>403 — the key is valid but lacks permission for this resource.</summary>
public sealed class AuthorizationException(string body = "Insufficient permissions")
    : MemMeshException(403, body, "AUTHORIZATION_ERROR");

/// <summary>404 — resource not found.</summary>
public sealed class NotFoundException(string body = "Resource not found")
    : MemMeshException(404, body, "NOT_FOUND");

/// <summary>400 / 422 — the request failed validation. <c>Params</c> carries the
/// per-field detail when the server supplies it.</summary>
public sealed class ValidationException(int status, string body = "Validation failed",
    IReadOnlyDictionary<string, object?>? @params = null)
    : MemMeshException(status, body, "VALIDATION_ERROR", @params);

/// <summary>429 — rate limited. <c>RetryAfter</c> is the server-advised wait when
/// a <c>Retry-After</c> header was present; the client already honors it on retry.</summary>
public sealed class RateLimitException(string body = "Rate limit exceeded", TimeSpan? retryAfter = null)
    : MemMeshException(429, body, "RATE_LIMIT")
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>5xx — the server failed to fulfill an otherwise valid request. Retried
/// with backoff before it surfaces.</summary>
public sealed class ServerException(int status, string body = "Internal server error")
    : MemMeshException(status, body, "SERVER_ERROR");

/// <summary>The request exceeded its timeout (default 30s, per-call overridable).
/// Not retried — a slow endpoint is unlikely to be faster on an immediate retry.</summary>
public sealed class RequestTimeoutException(string body = "Request timed out")
    : MemMeshException(0, body, "TIMEOUT");

/// <summary>A transport-level failure (DNS, connection reset, TLS) with no HTTP
/// response. Retried with backoff before it surfaces.</summary>
public sealed class NetworkException(string body = "Network error")
    : MemMeshException(0, body, "NETWORK_ERROR");
