using System.Globalization;

namespace MemMesh;

/// <summary>Typed attributes — structured/numeric data the engine reasons over
/// (credit scores, sensor readings, balances) instead of opaque metadata. Register
/// an attribute's schema once, then ingest observations: each is validated against
/// the definition (accepted or quarantined) and accepted numeric values are folded
/// into per-subject accumulators you can read back with running
/// mean/variance/min/max/cumulative.
///
/// Mirrors the TS reference surface (<c>tf.typed.*</c>); everything lives under
/// <c>/memory-typed/*</c>.
/// <code>
/// await mm.Typed.RegisterAttributeAsync(new RegisterAttributeRequest(
///     "credit_score", "numeric", MinValid: 300, MaxValid: 850));
/// var report = await mm.Typed.IngestAsync([new TypedObservationInput(
///     "credit_score", "contact", "sarah", DateTime.UtcNow.ToString("O"), ValueNumeric: 650)]);
/// var acc = await mm.Typed.AccumulatorAsync("contact", "sarah", "credit_score");
/// Console.WriteLine(acc.Mean); // 650
/// </code></summary>
public sealed class TypedService(MemMeshClient c)
{
    /// <summary>Register or update an attribute definition (type + plausibility range).</summary>
    public Task<AttributeDef> RegisterAttributeAsync(RegisterAttributeRequest body,
        RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<AttributeDef>(HttpMethod.Post, "memory-typed/attributes", body, options, ct);

    /// <summary>List registered attribute definitions for the project.</summary>
    public async Task<List<AttributeDef>> ListAttributesAsync(string? attributeKey = null,
        int? limit = null, int? offset = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (attributeKey is not null) q.Add($"attributeKey={Uri.EscapeDataString(attributeKey)}");
        if (limit is not null) q.Add($"limit={limit}");
        if (offset is not null) q.Add($"offset={offset}");
        var path = "memory-typed/attributes" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return await c.Send<List<AttributeDef>>(HttpMethod.Get, path, null, options, ct).ConfigureAwait(false);
    }

    /// <summary>Ingest a batch of typed observations synchronously and return the
    /// report (accepted / quarantined / duplicate counts + quarantine reasons).</summary>
    public Task<IngestReport> IngestAsync(IEnumerable<TypedObservationInput> observations,
        RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<IngestReport>(HttpMethod.Post, "memory-typed/observations",
            new Dictionary<string, object?> { ["observations"] = observations }, options, ct);

    /// <summary>Queue a batch for asynchronous ingest (the scalable path for high
    /// volume). Returns the count accepted onto the queue.</summary>
    public Task<EnqueueResult> EnqueueAsync(IEnumerable<TypedObservationInput> observations,
        RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<EnqueueResult>(HttpMethod.Post, "memory-typed/observations/enqueue",
            new Dictionary<string, object?> { ["observations"] = observations }, options, ct);

    /// <summary>Query raw observations by subject, attribute, time window, and value
    /// range.</summary>
    public async Task<List<TypedObservation>> QueryObservationsAsync(string? subjectKind = null,
        string? subjectExternalId = null, string? attributeKey = null, string? since = null,
        string? until = null, double? minValue = null, double? maxValue = null, string? status = null,
        int? limit = null, int? offset = null, RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (subjectKind is not null) q.Add($"subjectKind={Uri.EscapeDataString(subjectKind)}");
        if (subjectExternalId is not null) q.Add($"subjectExternalId={Uri.EscapeDataString(subjectExternalId)}");
        if (attributeKey is not null) q.Add($"attributeKey={Uri.EscapeDataString(attributeKey)}");
        if (since is not null) q.Add($"since={Uri.EscapeDataString(since)}");
        if (until is not null) q.Add($"until={Uri.EscapeDataString(until)}");
        if (minValue is not null) q.Add($"minValue={minValue.Value.ToString(CultureInfo.InvariantCulture)}");
        if (maxValue is not null) q.Add($"maxValue={maxValue.Value.ToString(CultureInfo.InvariantCulture)}");
        if (status is not null) q.Add($"status={Uri.EscapeDataString(status)}");
        if (limit is not null) q.Add($"limit={limit}");
        if (offset is not null) q.Add($"offset={offset}");
        var path = "memory-typed/observations" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        return await c.Send<List<TypedObservation>>(HttpMethod.Get, path, null, options, ct).ConfigureAwait(false);
    }

    /// <summary>Read the running statistics for a subject + attribute.</summary>
    public async Task<Accumulator> AccumulatorAsync(string subjectKind, string subjectExternalId,
        string attributeKey, RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new[]
        {
            $"subjectKind={Uri.EscapeDataString(subjectKind)}",
            $"subjectExternalId={Uri.EscapeDataString(subjectExternalId)}",
            $"attributeKey={Uri.EscapeDataString(attributeKey)}",
        };
        var path = "memory-typed/accumulator?" + string.Join("&", q);
        return await c.Send<Accumulator>(HttpMethod.Get, path, null, options, ct).ConfigureAwait(false);
    }
}
