namespace MemMesh;

/// <summary>The knowledge graph built from observed memory — the structural
/// half of what MemMesh stores.
///
/// Observing text doesn't only produce embeddable rows; extraction also
/// resolves entities and writes typed edges between them. That graph is what
/// reaches a fact no single memory states outright ("who does Sarah report
/// to?" answered from <c>sarah -[member_of]-&gt; team</c> plus
/// <c>team -[led_by]-&gt; priya</c>).
///
/// Every route here is admin-tier (<c>/admin/memory/...</c>); a project-scoped
/// key gets a 403.
///
/// Read-only by design. Entities and edges are written by extraction when you
/// <see cref="MemoryService.ObserveAsync"/>; the server's manual create/retire
/// routes exist for annotation tooling, and exposing them here would invite
/// hand-maintained graphs — the work the engine exists to do for you.
/// <code>
/// var st = await mm.Graph.StatsAsync();
/// Console.WriteLine($"{st.EntityCount} entities, {st.EdgeCount} edges");
///
/// var ents = await mm.Graph.ListEntitiesAsync(search: "Sarah", limit: 1);
/// var chain = await mm.Graph.TraverseAsync(ents[0].Id, hops: 2,
///     predicates: ["member_of", "led_by"]);
/// </code></summary>
public sealed class GraphService(MemMeshClient c)
{
    /// <summary>Aggregate counts for the whole graph.
    ///
    /// Prefer this over <c>(await ListEntitiesAsync()).Count</c> for any "how
    /// big is it" question: these are SQL <c>COUNT(*)</c>s over the full table,
    /// where the list routes page and would report the page size as the
    /// total.</summary>
    public Task<GraphStats> StatsAsync(RequestOptions? options = null, CancellationToken ct = default)
        => c.Send<GraphStats>(HttpMethod.Get, "admin/memory/graph/stats", null, options, ct);

    /// <summary>Entities, filtered by type/scope or a substring of name or
    /// alias. Unset filters are omitted from the query string.</summary>
    public Task<List<MemoryEntity>> ListEntitiesAsync(string? type = null, string? scope = null,
        string? search = null, int? limit = null, int? offset = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new QueryString()
            .Add("type", type).Add("scope", scope).Add("search", search)
            .Add("limit", limit).Add("offset", offset);
        return c.Send<List<MemoryEntity>>(HttpMethod.Get, $"admin/memory/entities{q}", null, options, ct);
    }

    /// <summary>One entity plus its 1-hop neighbourhood.</summary>
    public Task<EntityWithEdges> GetEntityAsync(string entityId, string? asOf = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new QueryString().Add("asOf", asOf);
        return c.Send<EntityWithEdges>(HttpMethod.Get, $"admin/memory/entities/{entityId}{q}",
            null, options, ct);
    }

    /// <summary>Every currently-valid edge. Use for rendering a whole small
    /// graph; for a large one, seed from an entity and
    /// <see cref="TraverseAsync"/> instead.</summary>
    public Task<List<GraphTraversalEdge>> ListEdgesAsync(string? asOf = null, int? limit = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var q = new QueryString().Add("asOf", asOf).Add("limit", limit);
        return c.Send<List<GraphTraversalEdge>>(HttpMethod.Get, $"admin/memory/graph/edges{q}",
            null, options, ct);
    }

    /// <summary>Walk out from a seed entity (1-3 hops).
    ///
    /// This is the multi-hop path: the edges returned here connect facts no
    /// single memory states together, which is how a question gets answered
    /// from a chain rather than from one lucky vector hit.</summary>
    public Task<List<GraphTraversalEdge>> TraverseAsync(string entityId, int? hops = null,
        IEnumerable<string>? predicates = null, string? asOf = null,
        RequestOptions? options = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["entityId"] = entityId };
        if (hops is not null) body["hops"] = hops;
        if (predicates is not null) body["predicates"] = predicates;
        if (asOf is not null) body["asOf"] = asOf;
        return c.Send<List<GraphTraversalEdge>>(HttpMethod.Post, "admin/memory/graph/traverse",
            body, options, ct);
    }
}

/// <summary>Builds a query string, skipping unset values and percent-encoding
/// the rest.
///
/// The encoding is not cosmetic: <c>search</c> carries user input, and an
/// unescaped <c>&amp;</c> would truncate the filter server-side and quietly
/// return the wrong page.</summary>
internal sealed class QueryString
{
    private readonly List<string> _parts = [];

    public QueryString Add(string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        return this;
    }

    public QueryString Add(string key, int? value)
        => value is null ? this : Add(key, value.Value.ToString());

    public override string ToString() => _parts.Count == 0 ? "" : "?" + string.Join("&", _parts);
}
