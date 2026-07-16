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

/// <summary>A review-queue row: a memory plus why it needs a steward's
/// attention. <c>ReviewReason</c> is one of pending / flagged / low_confidence
/// / stale.</summary>
public sealed record ReviewQueueItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("importance")] double Importance,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("supersededById")] string? SupersededById,
    [property: JsonPropertyName("reviewReason")] string ReviewReason);

/// <summary>One step of a procedure. <c>Pitfall</c> is an optional warning.</summary>
public sealed record ProcedureStep(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("pitfall")] string? Pitfall = null);

/// <summary>Category-level precedence exception: for this category, this tier wins.</summary>
public sealed record PrecedenceOverride(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("winningTier")] string WinningTier);

/// <summary>Which memory wins when two disagree. Default ladder:
/// human_verified &gt; local &gt; licensed_brain &gt; base.</summary>
public sealed record PrecedencePolicy(
    [property: JsonPropertyName("defaultOrder")] List<string> DefaultOrder,
    [property: JsonPropertyName("scopeNearestWins")] bool ScopeNearestWins,
    [property: JsonPropertyName("overrides")] List<PrecedenceOverride> Overrides);

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

/// <summary>Outcome of ingesting one media item: the memories extracted from it
/// plus the text the model read and where the raw bytes were kept.</summary>
public sealed record IngestMediaResult(
    [property: JsonPropertyName("saved")] List<MemoryItem> Saved,
    [property: JsonPropertyName("candidateCount")] int CandidateCount,
    [property: JsonPropertyName("extractedText")] string ExtractedText,
    [property: JsonPropertyName("modality")] string Modality,
    [property: JsonPropertyName("blobUri")] string BlobUri);
