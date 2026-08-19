using System.Globalization;
using System.Text.Json;

namespace MemMesh;

/// <summary>Subject-level consent / opt-out.
///
/// Records consent decisions as memory items of <c>type='consent'</c> so the
/// audit log captures every change. The Rust mining engine honors opt-outs at the
/// start of every mine pass — opted-out subjects are skipped, and their behavior
/// patterns aren't generated.
///
/// Foundations of the EU AI Act / GDPR Art. 22 compliance story: subject-level
/// (per-person / per-team / per-workspace) opt-out; audit-traceable (every
/// opt-out is a confirmed memory item); reversible (opt-in re-enables mining,
/// without restoring prior patterns — those must be re-mined to honor the gap).
///
/// Implementation note: this surface has no dedicated endpoints. It is
/// implemented entirely client-side over the admin memory CRUD — consent is
/// written and read as memory items — exactly mirroring the TS
/// <c>ConsentResource</c>. When the engine's dedicated <c>subject_consent</c>
/// table lands, this contract does not change.</summary>
public sealed class ConsentService(MemMeshClient c)
{
    /// <summary>Mark a subject as opted-out. Mining and recall must honor this:
    /// the engine skips opted-out subjects at mine time.
    /// <code>
    /// await mm.Consent.OptOutAsync(new Subject("contact", "sarah-pizza"),
    ///     reason: "GDPR Art. 17 request 2026-05-25");
    /// </code></summary>
    public async Task<ConsentStatus> OptOutAsync(Subject subject, string? reason = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        // Supersede any prior consent record so the audit log shows the history
        // but only one row is "active".
        await SupersedePriorConsentAsync(subject, options, ct).ConfigureAwait(false);

        var now = IsoNow();
        var memory = await CreateConsentMemoryAsync(
            $"[consent] {subject.Kind}:{subject.ExternalId} opted out",
            new Dictionary<string, object?>
            {
                ["subject"] = subject,
                ["optedOut"] = true,
                ["optedOutAt"] = now,
                ["reason"] = reason,
                ["recordKind"] = "consent",
            }, options, ct).ConfigureAwait(false);

        return new ConsentStatus(subject, OptedOut: true, OptedOutAt: now, Reason: reason, MemoryId: memory.Id);
    }

    /// <summary>Restore consent for a subject. Mining resumes from the next pass.
    /// Prior patterns are NOT auto-restored — they must be re-mined so the gap
    /// during opt-out is honored.</summary>
    public async Task<ConsentStatus> OptInAsync(Subject subject, RequestOptions? options = null,
        CancellationToken ct = default)
    {
        await SupersedePriorConsentAsync(subject, options, ct).ConfigureAwait(false);

        var now = IsoNow();
        var memory = await CreateConsentMemoryAsync(
            $"[consent] {subject.Kind}:{subject.ExternalId} opted in",
            new Dictionary<string, object?>
            {
                ["subject"] = subject,
                ["optedOut"] = false,
                ["optedOutAt"] = null,
                ["reason"] = null,
                ["recordKind"] = "consent",
            }, options, ct).ConfigureAwait(false);

        return new ConsentStatus(subject, OptedOut: false, OptedOutAt: null, Reason: null, MemoryId: memory.Id);
    }

    /// <summary>Read the current consent status for a subject. Returns
    /// <c>OptedOut = false</c> (default) if no consent record exists.</summary>
    public async Task<ConsentStatus> GetStatusAsync(Subject subject, RequestOptions? options = null,
        CancellationToken ct = default)
    {
        var active = await FindActiveConsentAsync(subject, options, ct).ConfigureAwait(false);
        if (active is null)
            return new ConsentStatus(subject, OptedOut: false, OptedOutAt: null, Reason: null, MemoryId: null);

        var md = active.Metadata;
        return new ConsentStatus(
            subject,
            OptedOut: ReadBool(md, "optedOut"),
            OptedOutAt: ReadString(md, "optedOutAt"),
            Reason: ReadString(md, "reason"),
            MemoryId: active.Id);
    }

    // ── private helpers ──────────────────────────────────────────────────────

    // Mirror the TS createMemory closure: spread content/type/scope/importance/
    // category/metadata onto the admin memory create body.
    private Task<MemoryItem> CreateConsentMemoryAsync(string content,
        IDictionary<string, object?> metadata, RequestOptions? options, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["content"] = content,
            ["type"] = "consent",
            ["scope"] = "project",
            ["importance"] = 10,
            ["category"] = "consent",
            ["metadata"] = metadata,
        };
        return c.Send<MemoryItem>(HttpMethod.Post, "admin/memory", body, options, ct);
    }

    private async Task<MemoryItem?> FindActiveConsentAsync(Subject subject,
        RequestOptions? options, CancellationToken ct)
    {
        // The engine caps `limit` at 500 (querystring/limit must be <= 500); 1000
        // hard-fails every consent lookup.
        var all = await c.Send<List<MemoryItem>>(HttpMethod.Get, "admin/memory?limit=500",
            null, options, ct).ConfigureAwait(false);

        return all
            .Where(m => m.Type == "consent" && SubjectMatches(m, subject))
            .OrderByDescending(m => ParseCreated(m.Created))
            .FirstOrDefault();
    }

    private async Task SupersedePriorConsentAsync(Subject subject, RequestOptions? options,
        CancellationToken ct)
    {
        var prior = await FindActiveConsentAsync(subject, options, ct).ConfigureAwait(false);
        if (prior is not null)
            // Hard-delete the prior row. The audit log keeps the historical trail;
            // we don't need the old row in the active set anymore.
            await c.SendVoid(HttpMethod.Delete, $"admin/memory/{prior.Id}", null, options, ct)
                .ConfigureAwait(false);
    }

    private static bool SubjectMatches(MemoryItem item, Subject subject)
    {
        if (item.Metadata is null ||
            !item.Metadata.TryGetValue("subject", out var s) ||
            s.ValueKind != JsonValueKind.Object)
            return false;
        var kind = s.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String
            ? k.GetString() : null;
        var externalId = s.TryGetProperty("externalId", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() : null;
        return kind == subject.Kind && externalId == subject.ExternalId;
    }

    // toISOString() parity: millisecond precision, UTC 'Z' suffix.
    private static string IsoNow() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseCreated(string? created) =>
        DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt : DateTimeOffset.MinValue;

    private static bool ReadBool(IReadOnlyDictionary<string, JsonElement>? md, string key) =>
        md is not null && md.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement>? md, string key) =>
        md is not null && md.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
