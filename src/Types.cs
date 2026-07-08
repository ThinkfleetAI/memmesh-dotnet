using System.Text.Json.Serialization;

namespace MemMesh;

/// <summary>Who/what a memory or prediction is about.</summary>
public sealed record Subject(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("externalId")] string ExternalId);

public sealed record MemoryItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("importance")] double Importance,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("supersededById")] string? SupersededById);

public sealed record SearchResult(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("similarity")] double Similarity,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("importance")] double Importance);

public sealed record Insight(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("sourceIds")] IReadOnlyList<string> SourceIds,
    [property: JsonPropertyName("confidence")] double Confidence);

public sealed record ReflectResult(
    [property: JsonPropertyName("insights")] IReadOnlyList<Insight> Insights,
    [property: JsonPropertyName("sourcesConsidered")] int SourcesConsidered,
    [property: JsonPropertyName("dryRun")] bool DryRun);

public sealed record GraphEdge(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("subjectId")] string SubjectId,
    [property: JsonPropertyName("predicate")] string Predicate,
    [property: JsonPropertyName("objectId")] string? ObjectId,
    [property: JsonPropertyName("objectLiteral")] string? ObjectLiteral,
    [property: JsonPropertyName("weight")] double Weight,
    [property: JsonPropertyName("validFrom")] string ValidFrom,
    [property: JsonPropertyName("validTo")] string? ValidTo);

public sealed record DedupResult(
    [property: JsonPropertyName("scanned")] int Scanned,
    [property: JsonPropertyName("groups")] int Groups,
    [property: JsonPropertyName("superseded")] int Superseded);
