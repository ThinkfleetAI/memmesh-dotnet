namespace MemMesh;

/// <summary>Brains — the marketplace registry. Register, version, and manage the
/// brains a project publishes. A brain carries a Brain Card manifest (ontology,
/// provenance, coverage, eval, pricing) and a stable <c>ExternalId</c> slug the
/// Mesh Router addresses it by.
///
/// Once a brain is <c>PUBLISHED</c> + <c>PUBLIC</c>, any caller can consume it
/// over the hosted MCP endpoint (<c>/brains/{brainId}/mcp-server/http</c>);
/// consumption is an MCP connection, not a REST call, so it lives outside this
/// resource.
///
/// Mirrors the TS reference surface (<c>tf.brains.*</c>): create, the high-level
/// createFromProject path, cursor-paginated list, get, update/version, delete.
/// <code>
/// var brain = await mm.Brains.CreateAsync(new CreateBrainRequest(
///     ExternalId: "sec-edgar-financials", Name: "SEC EDGAR Financials",
///     Domain: "finance", Version: "2026.07.0",
///     Card: new BrainCard(Provenance: [new BrainProvenance("SEC EDGAR", "public-domain")])));
/// await mm.Brains.UpdateAsync(brain.Id,
///     new UpdateBrainRequest(Visibility: "PUBLIC", Status: "PUBLISHED"));
///
/// var page = await mm.Brains.ListAsync(limit: 20);
/// foreach (var b in page.Data) Console.WriteLine($"{b.ExternalId} {b.Status}");
/// </code></summary>
public sealed class BrainsService(MemMeshClient c)
{
    /// <summary>Register a new brain in the project's catalog.</summary>
    public Task<Brain> CreateAsync(CreateBrainRequest body, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<Brain>(HttpMethod.Post, "brains", body, options, ct);

    /// <summary>Create a brain from the calling project's memory — the easy,
    /// high-level path. Where <see cref="CreateAsync"/> wants a full
    /// <see cref="CreateBrainRequest"/>, this builds a sensible one for you from
    /// just a slug + name (plus optional domain / version / visibility) and an
    /// empty-but-valid Brain Card. Coverage is computed server-side from the
    /// project's own memory, so you don't pass it. The brain is created as a
    /// <c>DRAFT</c> + <c>PRIVATE</c>; publishing and pricing are deliberate,
    /// separate steps.</summary>
    public Task<Brain> CreateFromProjectAsync(string externalId, string name,
        string? domain = null, string? version = null, string? visibility = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        // Empty-but-valid card: an empty provenance list and empty coverage. The
        // server recomputes coverage from the project's memory; a real licensed
        // provenance source is only required to publish PUBLIC (a separate step).
        var body = new CreateBrainRequest(
            ExternalId: externalId,
            Name: name,
            Domain: domain,
            Version: version ?? "1.0.0",
            Visibility: visibility ?? "PRIVATE",
            Card: new BrainCard(Provenance: [], Coverage: new BrainCoverage()));
        return CreateAsync(body, options, ct);
    }

    /// <summary>List the project's brains (cursor-paginated). Pass the returned
    /// page's <c>Next</c> cursor back as <paramref name="cursor"/> for the
    /// following page; <c>Next</c>/<c>Previous</c> are null at the respective
    /// ends. To stream every brain and follow cursors automatically, use
    /// <see cref="MemMeshClient.ListAllAsync{T}"/> over the <c>brains</c> path.</summary>
    public Task<SeekPage<Brain>> ListAsync(int? limit = null, string? cursor = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var path = "brains" + (limit is not null ? $"?limit={limit}" : "");
        return c.GetPageAsync<Brain>(path, cursor, options, ct);
    }

    /// <summary>Fetch one brain by id. 404s if it doesn't exist or belongs to a
    /// different project.</summary>
    public Task<Brain> GetAsync(string brainId, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.Send<Brain>(HttpMethod.Get, $"brains/{Uri.EscapeDataString(brainId)}", null, options, ct);

    /// <summary>Update / version a brain (name, version, visibility, status, card,
    /// …).</summary>
    public Task<Brain> UpdateAsync(string brainId, UpdateBrainRequest body,
        RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<Brain>(HttpMethod.Patch, $"brains/{Uri.EscapeDataString(brainId)}", body, options, ct);

    /// <summary>Delete a brain from the catalog.</summary>
    public Task DeleteAsync(string brainId, RequestOptions? options = null,
        CancellationToken ct = default)
        => c.SendVoid(HttpMethod.Delete, $"brains/{Uri.EscapeDataString(brainId)}", null, options, ct);
}
