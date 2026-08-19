using System.Text.Json;
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
    [property: JsonPropertyName("supersededById")] string? SupersededById,
    // Free-form metadata carried on the item. Kept as raw JSON so callers can
    // read provenance keys (e.g. sourceMemoryIds, which ExplainAsync resolves)
    // without the SDK having to model every producer's payload. Null when the
    // server omits it.
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, JsonElement>? Metadata = null,
    // Creation timestamp (ISO-8601). Optional so older readers stay compatible;
    // the consent surface sorts candidate records by it to find the active one.
    [property: JsonPropertyName("created")] string? Created = null);

/// <summary>What <see cref="MemoryService.ObserveAsync"/> returns: the memories
/// the engine chose to keep (empty when the turn was filler — that is still a
/// success) plus how many extraction candidates it found before the dedupe /
/// budget pass. <c>Saved.Count &lt;= CandidateCount</c>.</summary>
public sealed record ObserveResponse
{
    [JsonPropertyName("saved")] public List<MemoryItem> Saved { get; init; } = new();
    [JsonPropertyName("candidateCount")] public int CandidateCount { get; init; }
}

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

/// <summary>Outcome of a LLM deductive-consolidation pass: how many subjects
/// were scanned and how the resulting one-facet observations broke down into
/// new / reinforced / contradicted.</summary>
public sealed record ConsolidateResult(
    [property: JsonPropertyName("subjectsConsidered")] int SubjectsConsidered,
    [property: JsonPropertyName("observationsCreated")] int ObservationsCreated,
    [property: JsonPropertyName("observationsUpdated")] int ObservationsUpdated,
    [property: JsonPropertyName("observationsSuperseded")] int ObservationsSuperseded,
    [property: JsonPropertyName("durationMs")] int DurationMs);

/// <summary>Result of one embedding-backfill pass — how many items were
/// vectorized. Call repeatedly until <c>Embedded</c> is 0 to drain a corpus.</summary>
public sealed record BackfillEmbeddingsResult(
    [property: JsonPropertyName("embedded")] int Embedded);

/// <summary>A feedback record attached to a memory item — <c>positive</c>
/// reinforces, <c>negative</c> counts toward the auto-flag threshold.</summary>
public sealed record MemoryFeedback(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("memoryId")] string MemoryId,
    [property: JsonPropertyName("responseId")] string? ResponseId,
    [property: JsonPropertyName("rating")] string Rating,
    [property: JsonPropertyName("comment")] string? Comment,
    [property: JsonPropertyName("createdByUserId")] string? CreatedByUserId,
    [property: JsonPropertyName("created")] string Created);

/// <summary>Right-to-explanation payload: a memory item plus the raw source
/// memories that produced it (empty for non-derived items). Resolved client-side
/// from the item's <c>metadata.sourceMemoryIds</c> via point lookups.</summary>
public sealed record MemoryExplanation(
    [property: JsonPropertyName("memory")] MemoryItem Memory,
    [property: JsonPropertyName("sourceMemories")] IReadOnlyList<MemoryItem> SourceMemories);

// ── Lattice: behavioral pattern intelligence ────────────────────────────────
// Mirrors thinkfleet-memory-sdk/src/types/lattice.ts. The engine surfaces these
// at /api/v1/projects/{projectId}/lattice/*.

/// <summary>Repeatable cadence a behavior pattern fires on.</summary>
public sealed record Cadence(
    [property: JsonPropertyName("periodDays")] double? PeriodDays = null,
    [property: JsonPropertyName("dayOfWeek")] int? DayOfWeek = null,
    [property: JsonPropertyName("timeOfDayLocal")] string? TimeOfDayLocal = null,
    [property: JsonPropertyName("timezone")] string? Timezone = null);

/// <summary>Structured metadata carried on a mined behavior pattern.
/// <c>PatternKind</c> is one of recurring_event / day_of_week / time_of_day /
/// entity_preference / entity_bundle / declining_engagement / offer_responsiveness.</summary>
public sealed record BehaviorPatternMetadata(
    [property: JsonPropertyName("patternKind")] string PatternKind,
    [property: JsonPropertyName("contactId")] string ContactId,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("observationCount")] int ObservationCount,
    [property: JsonPropertyName("observationWindowDays")] int ObservationWindowDays,
    [property: JsonPropertyName("lastObservedAt")] string LastObservedAt,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("entityExternalIds")] IReadOnlyList<string>? EntityExternalIds = null,
    [property: JsonPropertyName("entityKind")] string? EntityKind = null,
    [property: JsonPropertyName("eventType")] string? EventType = null,
    [property: JsonPropertyName("cadence")] Cadence? Cadence = null,
    [property: JsonPropertyName("nextExpectedAt")] string? NextExpectedAt = null,
    [property: JsonPropertyName("toleranceMinutes")] int? ToleranceMinutes = null);

/// <summary>A learned behavior pattern. <c>Summary</c> mirrors the underlying
/// memory item's content.</summary>
public sealed record BehaviorPatternRecord(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("projectId")] string? ProjectId,
    [property: JsonPropertyName("contactId")] string ContactId,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("metadata")] BehaviorPatternMetadata Metadata,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("created")] string Created,
    [property: JsonPropertyName("updated")] string Updated);

/// <summary>Per-contact failure during a bulk extraction run.</summary>
public sealed record ContactExtractError(
    [property: JsonPropertyName("contactId")] string ContactId,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("error")] string Error);

/// <summary>Outcome of a pattern (re-)extraction / mining run.</summary>
public sealed record ExtractPatternsResult(
    [property: JsonPropertyName("contactsProcessed")] int ContactsProcessed,
    [property: JsonPropertyName("patternsCreated")] int PatternsCreated,
    [property: JsonPropertyName("patternsRefreshed")] int PatternsRefreshed,
    [property: JsonPropertyName("patternsDeactivated")] int PatternsDeactivated,
    [property: JsonPropertyName("durationMs")] int DurationMs,
    [property: JsonPropertyName("errors")] IReadOnlyList<ContactExtractError>? Errors = null);

/// <summary>One page of a contact's behavior patterns. Pass
/// <c>NextCursor</c> back as <c>cursor</c> for the next page (null when
/// exhausted).</summary>
public sealed record ListContactPatternsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<BehaviorPatternRecord> Data,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

// ── Lattice context bundle ──────────────────────────────────────────────────

public sealed record LatticeContextContact(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("displayName")] string? DisplayName = null,
    [property: JsonPropertyName("email")] string? Email = null,
    [property: JsonPropertyName("phone")] string? Phone = null,
    [property: JsonPropertyName("segment")] string? Segment = null,
    [property: JsonPropertyName("tags")] IReadOnlyList<string>? Tags = null,
    [property: JsonPropertyName("lifetimeValue")] double? LifetimeValue = null,
    [property: JsonPropertyName("lastInteractionAt")] string? LastInteractionAt = null);

public sealed record LatticeContextEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("occurredAt")] string OccurredAt,
    [property: JsonPropertyName("data")] IReadOnlyDictionary<string, JsonElement>? Data = null);

public sealed record LatticeContextMemory(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("importance")] double Importance,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("created")] string Created);

public sealed record LatticeContextEntity(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, JsonElement> Metadata);

public sealed record LatticeContextEdge(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("sourceEntityId")] string SourceEntityId,
    [property: JsonPropertyName("targetEntityId")] string TargetEntityId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("weight")] double? Weight = null);

/// <summary>Full retrieval bundle for a contact — profile, active patterns,
/// recent events, recent memories, and (optionally) the entity/edge graph.</summary>
public sealed record LatticeContextBundle(
    [property: JsonPropertyName("contactId")] string ContactId,
    [property: JsonPropertyName("contact")] LatticeContextContact Contact,
    [property: JsonPropertyName("activePatterns")] IReadOnlyList<BehaviorPatternRecord> ActivePatterns,
    [property: JsonPropertyName("recentEvents")] IReadOnlyList<LatticeContextEvent> RecentEvents,
    [property: JsonPropertyName("recentMemories")] IReadOnlyList<LatticeContextMemory> RecentMemories,
    [property: JsonPropertyName("entities")] IReadOnlyList<LatticeContextEntity>? Entities = null,
    [property: JsonPropertyName("edges")] IReadOnlyList<LatticeContextEdge>? Edges = null);

// ── Monitor ─────────────────────────────────────────────────────────────────

/// <summary>One pattern that failed during a monitor tick.</summary>
public sealed record MonitorTickFailure(
    [property: JsonPropertyName("patternId")] string PatternId,
    [property: JsonPropertyName("error")] string Error);

/// <summary>Outcome of one pattern-break monitor tick.</summary>
public sealed record MonitorTickResult(
    [property: JsonPropertyName("patternsChecked")] int PatternsChecked,
    [property: JsonPropertyName("patternsBroken")] int PatternsBroken,
    [property: JsonPropertyName("breaksEmitted")] int BreaksEmitted,
    [property: JsonPropertyName("durationMs")] int DurationMs,
    [property: JsonPropertyName("capped")] bool Capped,
    [property: JsonPropertyName("failures")] IReadOnlyList<MonitorTickFailure> Failures);

/// <summary>Pattern-break monitor health — last tick + how many patterns are due.</summary>
public sealed record MonitorStatus(
    [property: JsonPropertyName("lastTickAt")] string? LastTickAt,
    [property: JsonPropertyName("lastTickDurationMs")] int? LastTickDurationMs,
    [property: JsonPropertyName("patternsDue")] int PatternsDue,
    [property: JsonPropertyName("activePatternCount")] int ActivePatternCount);

// ── Predict ─────────────────────────────────────────────────────────────────

/// <summary>A declaratively-specified prediction target (v2). Declare *what* to
/// predict; the engine selects the model family from <c>Kind</c>. <c>Kind</c> is
/// one of event_occurrence / numeric / event_time / anomaly.</summary>
public sealed record PredictionTarget(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("eventType")] string? EventType = null,
    [property: JsonPropertyName("attributeKey")] string? AttributeKey = null,
    [property: JsonPropertyName("lookbackDays")] int? LookbackDays = null);

/// <summary>One projected event derived from one active behavior pattern.</summary>
public sealed record PredictedEvent(
    [property: JsonPropertyName("patternId")] string PatternId,
    [property: JsonPropertyName("patternKind")] string PatternKind,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("expectedAt")] string ExpectedAt,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("windowMinutes")] int WindowMinutes,
    [property: JsonPropertyName("sourceMemoryIds")] IReadOnlyList<string> SourceMemoryIds,
    [property: JsonPropertyName("confidenceLower")] double? ConfidenceLower = null,
    [property: JsonPropertyName("confidenceUpper")] double? ConfidenceUpper = null);

/// <summary>The single calibrated estimate for a declared <c>target</c>. Always
/// check <c>Abstained</c> first: when true, treat it as "unknown", never as
/// "no/low risk".</summary>
public sealed record TargetPrediction(
    [property: JsonPropertyName("targetKind")] string TargetKind,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("probability")] double Probability,
    [property: JsonPropertyName("probabilityLower")] double ProbabilityLower,
    [property: JsonPropertyName("probabilityUpper")] double ProbabilityUpper,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("valueLower")] double ValueLower,
    [property: JsonPropertyName("valueUpper")] double ValueUpper,
    [property: JsonPropertyName("expectedAt")] string ExpectedAt,
    [property: JsonPropertyName("expectedAtLower")] string ExpectedAtLower,
    [property: JsonPropertyName("expectedAtUpper")] string ExpectedAtUpper,
    [property: JsonPropertyName("daysUntil")] double DaysUntil,
    [property: JsonPropertyName("anomalyScore")] double AnomalyScore,
    [property: JsonPropertyName("isAnomaly")] bool IsAnomaly,
    [property: JsonPropertyName("abstained")] bool Abstained,
    [property: JsonPropertyName("abstentionReason")] string AbstentionReason,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("evidenceMemoryIds")] IReadOnlyList<string> EvidenceMemoryIds);

/// <summary>Result of a <c>predict</c> call. Pattern-projection mode fills
/// <c>Predictions</c>; declared-target mode fills <c>TargetPrediction</c>.</summary>
public sealed record PredictResult(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("predictions")] IReadOnlyList<PredictedEvent> Predictions,
    [property: JsonPropertyName("activePatternCount")] int ActivePatternCount,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt,
    [property: JsonPropertyName("durationMs")] int DurationMs,
    [property: JsonPropertyName("eventsEmitted")] int? EventsEmitted = null,
    [property: JsonPropertyName("abstained")] bool? Abstained = null,
    [property: JsonPropertyName("abstentionReason")] string? AbstentionReason = null,
    [property: JsonPropertyName("targetPrediction")] TargetPrediction? TargetPrediction = null);

// ── Profile ─────────────────────────────────────────────────────────────────

/// <summary>A risk signal surfaced on a subject's profile.</summary>
public sealed record RiskIndicator(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("severity")] double Severity,
    [property: JsonPropertyName("sourcePatternId")] string SourcePatternId);

/// <summary>Behavioral profile snapshot — "who is this subject" — aggregating the
/// subject's active patterns. Non-temporal counterpart to <c>PredictResult</c>.</summary>
public sealed record SubjectProfile(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("rfmSegment")] string? RfmSegment,
    [property: JsonPropertyName("recencyScore")] double? RecencyScore,
    [property: JsonPropertyName("frequencyScore")] double? FrequencyScore,
    [property: JsonPropertyName("monetaryScore")] double? MonetaryScore,
    [property: JsonPropertyName("topEntity")] string? TopEntity,
    [property: JsonPropertyName("cadenceSummary")] string? CadenceSummary,
    [property: JsonPropertyName("risks")] IReadOnlyList<RiskIndicator> Risks,
    [property: JsonPropertyName("contributingPatternIds")] IReadOnlyList<string> ContributingPatternIds,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt,
    [property: JsonPropertyName("durationMs")] int DurationMs);

// ── Cohort ──────────────────────────────────────────────────────────────────

/// <summary>One subject whose behavior is similar to the cohort target.</summary>
public sealed record CohortMember(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("similarity")] double Similarity,
    [property: JsonPropertyName("rfmSegment")] string RfmSegment,
    [property: JsonPropertyName("patternKinds")] IReadOnlyList<string> PatternKinds);

/// <summary>Top-K nearest-neighbor cohort for a subject.</summary>
public sealed record GetCohortResponse(
    [property: JsonPropertyName("target")] Subject Target,
    [property: JsonPropertyName("members")] IReadOnlyList<CohortMember> Members,
    [property: JsonPropertyName("candidateCount")] int CandidateCount,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt,
    [property: JsonPropertyName("durationMs")] int DurationMs);

/// <summary>One cohort-aggregated prediction — "people like the target also did
/// X", fully traceable via <c>SupportingSubjects</c> + <c>SourceMemoryIds</c>.</summary>
public sealed record CohortPrediction(
    [property: JsonPropertyName("patternKind")] string PatternKind,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("expectedAt")] string ExpectedAt,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("windowMinutes")] int WindowMinutes,
    [property: JsonPropertyName("supportingSubjects")] IReadOnlyList<Subject> SupportingSubjects,
    [property: JsonPropertyName("sourceMemoryIds")] IReadOnlyList<string> SourceMemoryIds);

/// <summary>Cohort-aware predictions plus the cohort they were aggregated from.</summary>
public sealed record PredictByCohortResponse(
    [property: JsonPropertyName("target")] Subject Target,
    [property: JsonPropertyName("cohort")] IReadOnlyList<CohortMember> Cohort,
    [property: JsonPropertyName("predictions")] IReadOnlyList<CohortPrediction> Predictions,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt,
    [property: JsonPropertyName("durationMs")] int DurationMs);

// ── Estimate (deterministic estimators, e.g. PhenoAge bio-age) ───────────────

/// <summary>One signal's signed contribution to an estimate.</summary>
public sealed record ScoreContributor(
    [property: JsonPropertyName("signal")] string Signal,
    [property: JsonPropertyName("contribution")] double Contribution);

/// <summary>A wellness estimate — never a diagnosis. When <c>Ok</c> is false, the
/// required signals that had no reading are in <c>MissingSignals</c>.</summary>
public sealed record EstimateResult(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("estimatorId")] string EstimatorId,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("contributors")] IReadOnlyList<ScoreContributor> Contributors,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("provenance")] IReadOnlyList<string> Provenance,
    [property: JsonPropertyName("framing")] string Framing,
    [property: JsonPropertyName("disclaimer")] string Disclaimer,
    [property: JsonPropertyName("missingSignals")] IReadOnlyList<string> MissingSignals);

// ── Calibration ─────────────────────────────────────────────────────────────

/// <summary>One confidence band mapped to its realized hit-rate.</summary>
public sealed record CalibrationBucket(
    [property: JsonPropertyName("lower")] double Lower,
    [property: JsonPropertyName("upper")] double Upper,
    [property: JsonPropertyName("patterns")] int Patterns,
    [property: JsonPropertyName("predictions")] int Predictions,
    [property: JsonPropertyName("hits")] int Hits,
    [property: JsonPropertyName("misses")] int Misses,
    [property: JsonPropertyName("realizedHitRate")] double RealizedHitRate,
    [property: JsonPropertyName("hasData")] bool HasData);

/// <summary>Prediction-calibration report: are "80% confident" predictions right
/// ~80% of the time?</summary>
public sealed record CalibrationReport(
    [property: JsonPropertyName("buckets")] IReadOnlyList<CalibrationBucket> Buckets,
    [property: JsonPropertyName("totalPatterns")] int TotalPatterns,
    [property: JsonPropertyName("totalPredictions")] int TotalPredictions);

// ── Learning: closed-loop decision → action → outcome ────────────────────────
// Mirrors thinkfleet-memory-sdk/src/resources/learning.ts. The engine surfaces
// these at /api/v1/projects/{projectId}/lattice/*.

/// <summary>A causal input to a decision — the pattern/prediction/observation the
/// actor was reacting to. <c>Weight</c> splits credit across multiple inputs
/// (0/unset is treated as 1.0). <c>RefType</c> defaults to "pattern".</summary>
public sealed record ProvenanceRef(
    [property: JsonPropertyName("memoryId")] string MemoryId,
    [property: JsonPropertyName("refType")] string? RefType = null,
    [property: JsonPropertyName("weight")] double? Weight = null);

/// <summary>A recorded decision and its causal provenance.</summary>
public sealed record DecisionRecord(
    [property: JsonPropertyName("decisionId")] string DecisionId,
    [property: JsonPropertyName("subject")] Subject? Subject,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("decisionType")] string DecisionType,
    [property: JsonPropertyName("policy")] string Policy,
    [property: JsonPropertyName("informedBy")] IReadOnlyList<ProvenanceRef> InformedBy,
    [property: JsonPropertyName("actionType")] string ActionType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("occurredAt")] string OccurredAt,
    [property: JsonPropertyName("created")] string Created);

/// <summary>Result of recording a decision. <c>Decision</c> is null when the
/// engine returns no record.</summary>
public sealed record RecordDecisionResult(
    [property: JsonPropertyName("decision")] DecisionRecord? Decision);

/// <summary>One informing ref re-weighted by an outcome — the before/after
/// calibrated confidence and the running hit/miss tally.</summary>
public sealed record CalibrationUpdate(
    [property: JsonPropertyName("refId")] string RefId,
    [property: JsonPropertyName("refType")] string RefType,
    [property: JsonPropertyName("priorConfidence")] double PriorConfidence,
    [property: JsonPropertyName("posteriorConfidence")] double PosteriorConfidence,
    [property: JsonPropertyName("hits")] int Hits,
    [property: JsonPropertyName("misses")] int Misses);

/// <summary>Result of recording an outcome — which informing refs were
/// re-weighted, and by how much.</summary>
public sealed record RecordOutcomeResult(
    [property: JsonPropertyName("outcomeId")] string OutcomeId,
    [property: JsonPropertyName("updates")] IReadOnlyList<CalibrationUpdate> Updates);

/// <summary>A recorded outcome of a decision.</summary>
public sealed record OutcomeRecord(
    [property: JsonPropertyName("outcomeId")] string OutcomeId,
    [property: JsonPropertyName("decisionId")] string DecisionId,
    [property: JsonPropertyName("subject")] Subject? Subject,
    [property: JsonPropertyName("decisionType")] string DecisionType,
    [property: JsonPropertyName("actionType")] string ActionType,
    [property: JsonPropertyName("outcomeType")] string OutcomeType,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("reward")] double Reward,
    [property: JsonPropertyName("occurredAt")] string OccurredAt,
    [property: JsonPropertyName("realizedAt")] string RealizedAt);

/// <summary>One "what worked" aggregation row — support, success rate, average
/// reward, and the Beta-Binomial posterior mean of the success rate.</summary>
public sealed record EffectivenessRow(
    [property: JsonPropertyName("groupKey")] string GroupKey,
    [property: JsonPropertyName("n")] int N,
    [property: JsonPropertyName("successRate")] double SuccessRate,
    [property: JsonPropertyName("avgReward")] double AvgReward,
    [property: JsonPropertyName("confidence")] double Confidence);

// ── Behaviors: emergent behavior discovery ───────────────────────────────────
// Mirrors thinkfleet-memory-sdk/src/resources/behaviors.ts.

/// <summary>One emergent behavior — a cohesive cluster of subjects the engine
/// grouped because they behave alike, with the statistics that justify treating
/// it as real. <c>Size</c> may exceed <c>MemberSubjects.Count</c> when capped by
/// <c>maxMembers</c>.</summary>
public sealed record DiscoveredBehavior(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("prevalence")] double Prevalence,
    [property: JsonPropertyName("stability")] double Stability,
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("memberSubjects")] IReadOnlyList<Subject> MemberSubjects,
    [property: JsonPropertyName("exemplarEvidence")] IReadOnlyList<string> ExemplarEvidence);

/// <summary>Result of a discovery run. <c>Behaviors</c> is sorted by prevalence
/// then stability; empty means the engine abstained (not enough signal), never
/// "there are no behaviors".</summary>
public sealed record DiscoverResult(
    [property: JsonPropertyName("behaviors")] IReadOnlyList<DiscoveredBehavior> Behaviors,
    [property: JsonPropertyName("subjectsAnalyzed")] int SubjectsAnalyzed,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt,
    [property: JsonPropertyName("durationMs")] int DurationMs);

// ── Brains (marketplace registry) ─────────────────────────────────────────────

/// <summary>Provenance of the facts in a brain — where they came from and under
/// what license.</summary>
public sealed record BrainProvenance(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null);

/// <summary>Coverage the induced reasoning layer advertises on a Brain Card — the
/// procedure/checklist/decomposition memories that make a brain worth more than a
/// plain dataset. A facts-only brain reports <c>Total = 0</c>.</summary>
public sealed record BrainReasoningCoverage(
    [property: JsonPropertyName("procedures"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Procedures = null,
    [property: JsonPropertyName("checklists"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Checklists = null,
    [property: JsonPropertyName("decompositions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Decompositions = null,
    [property: JsonPropertyName("total"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Total = null);

/// <summary>Aggregate coverage a Brain Card advertises — subject/fact counts, the
/// induced reasoning layer, and a freshness marker.</summary>
public sealed record BrainCoverage(
    [property: JsonPropertyName("subjects"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Subjects = null,
    [property: JsonPropertyName("facts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Facts = null,
    [property: JsonPropertyName("reasoning"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BrainReasoningCoverage? Reasoning = null,
    [property: JsonPropertyName("freshness"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Freshness = null);

/// <summary>Benchmark evaluation surfaced on a Brain Card.</summary>
public sealed record BrainEvaluation(
    [property: JsonPropertyName("benchmark"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Benchmark = null,
    [property: JsonPropertyName("score"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Score = null);

/// <summary>Pricing hint surfaced on a Brain Card.</summary>
public sealed record BrainPricing(
    [property: JsonPropertyName("model"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Model = null,
    [property: JsonPropertyName("unit"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Unit = null);

/// <summary>The Brain Card manifest (stored on the brain, surfaced in the
/// catalog). Optional fields are omitted from the wire when null.</summary>
public sealed record BrainCard(
    [property: JsonPropertyName("ontologyRef"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OntologyRef = null,
    [property: JsonPropertyName("provenance"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<BrainProvenance>? Provenance = null,
    [property: JsonPropertyName("changelogRef"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ChangelogRef = null,
    [property: JsonPropertyName("coverage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BrainCoverage? Coverage = null,
    [property: JsonPropertyName("evaluation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BrainEvaluation? Evaluation = null,
    [property: JsonPropertyName("predictEnabled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PredictEnabled = null,
    [property: JsonPropertyName("pricing"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BrainPricing? Pricing = null);

/// <summary>A Brain — a publishable/consumable unit of memory: a Brain Card
/// manifest plus a stable <c>ExternalId</c> slug the Mesh Router addresses it by.
/// Consumption of a published brain happens over the hosted MCP endpoint, not a
/// REST call, so it lives outside this SDK surface.</summary>
public sealed record Brain(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("domain")] string? Domain,
    [property: JsonPropertyName("brainInterface")] string BrainInterface,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("rightsAttested")] bool RightsAttested,
    [property: JsonPropertyName("card")] BrainCard? Card,
    [property: JsonPropertyName("created")] string Created,
    [property: JsonPropertyName("updated")] string Updated);

/// <summary>Register-a-brain payload. Optional fields are omitted from the wire
/// when null (mirrors the TS request shape).</summary>
public sealed record CreateBrainRequest(
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("domain"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Domain = null,
    [property: JsonPropertyName("version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Version = null,
    [property: JsonPropertyName("visibility"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Visibility = null,
    [property: JsonPropertyName("rightsAttested"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? RightsAttested = null,
    [property: JsonPropertyName("card"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BrainCard? Card = null);

/// <summary>Update / version a brain. Every field is optional; only the ones you
/// set are sent.</summary>
public sealed record UpdateBrainRequest(
    [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonPropertyName("domain"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Domain = null,
    [property: JsonPropertyName("version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Version = null,
    [property: JsonPropertyName("visibility"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Visibility = null,
    [property: JsonPropertyName("status"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status = null,
    [property: JsonPropertyName("rightsAttested"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? RightsAttested = null,
    [property: JsonPropertyName("card"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BrainCard? Card = null);

// ── Consent (subject-level opt-out) ───────────────────────────────────────────

/// <summary>Current consent status for a subject. <c>OptedOut</c> defaults to
/// <c>false</c> when no consent record exists. <c>MemoryId</c> links to the
/// underlying consent memory item for audit.</summary>
public sealed record ConsentStatus(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("optedOut")] bool OptedOut,
    [property: JsonPropertyName("optedOutAt")] string? OptedOutAt,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("memoryId")] string? MemoryId);

// ── Alerts (user-defined alert rules) ─────────────────────────────────────────
// Mirrors thinkfleet-memory-sdk/src/resources/alerts.ts. Surfaced at
// /api/v1/projects/{projectId}/memory-alerts (fires nested under /{id}/fires).

/// <summary>What makes an alert rule fire. <c>Kind</c> is one of engine-event /
/// segment-change / pattern-emerged; the other fields are populated per kind —
/// <c>EventTypes</c> for engine-event, <c>From</c>/<c>To</c> for segment-change,
/// <c>PatternKind</c> for pattern-emerged.</summary>
public sealed record AlertTrigger(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("eventTypes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? EventTypes = null,
    [property: JsonPropertyName("from"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? From = null,
    [property: JsonPropertyName("to"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? To = null,
    [property: JsonPropertyName("patternKind"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PatternKind = null);

/// <summary>Narrows which firing events a rule matches. <c>SubjectExternalIdPattern</c>
/// is a glob (<c>vip-*</c>); <c>MetadataMatch</c> is dot-path equality against the
/// event payload.</summary>
public sealed record AlertFilter(
    [property: JsonPropertyName("subjectKind"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SubjectKind = null,
    [property: JsonPropertyName("subjectExternalIdPattern"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SubjectExternalIdPattern = null,
    [property: JsonPropertyName("categories"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Categories = null,
    [property: JsonPropertyName("metadataMatch"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, object?>? MetadataMatch = null);

/// <summary>The <c>memory</c>-channel writeback template — writes the firing event
/// as an OBSERVATION memory item the next context build surfaces to the LLM.</summary>
public sealed record NotificationChannelWriteAs(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("scope"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Scope = null);

/// <summary>How a fired alert is delivered. <c>Kind</c> is <c>webhook</c>
/// (<c>Url</c> + optional HMAC <c>Secret</c>) or <c>memory</c> (<c>WriteAs</c>).</summary>
public sealed record NotificationChannel(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null,
    [property: JsonPropertyName("secret"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Secret = null,
    [property: JsonPropertyName("writeAs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] NotificationChannelWriteAs? WriteAs = null);

/// <summary>Rate-limiting for a rule. <c>DedupOn</c> is one of subject /
/// subject+rule / rule.</summary>
public sealed record ThrottleConfig(
    [property: JsonPropertyName("maxPerHour"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxPerHour = null,
    [property: JsonPropertyName("cooldownMinutes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CooldownMinutes = null,
    [property: JsonPropertyName("dedupOn"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DedupOn = null);

/// <summary>A stored alert rule — "tell me when X happens, this way".</summary>
public sealed record AlertRule(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("projectId")] string ProjectId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("trigger")] AlertTrigger Trigger,
    [property: JsonPropertyName("filter")] AlertFilter? Filter,
    [property: JsonPropertyName("notify")] IReadOnlyList<NotificationChannel> Notify,
    [property: JsonPropertyName("throttle")] ThrottleConfig? Throttle,
    [property: JsonPropertyName("created")] string Created,
    [property: JsonPropertyName("updated")] string Updated);

/// <summary>Create-an-alert-rule payload. Optional fields are omitted from the
/// wire when null.</summary>
public sealed record CreateAlertRuleRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("trigger")] AlertTrigger Trigger,
    [property: JsonPropertyName("notify")] IReadOnlyList<NotificationChannel> Notify,
    [property: JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonPropertyName("enabled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Enabled = null,
    [property: JsonPropertyName("filter"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AlertFilter? Filter = null,
    [property: JsonPropertyName("throttle"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ThrottleConfig? Throttle = null);

/// <summary>Patch an alert rule — only the fields you set are sent (null-out of a
/// field is not expressible; use <see cref="CreateAlertRuleRequest"/> semantics).</summary>
public sealed record UpdateAlertRuleRequest(
    [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
    [property: JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonPropertyName("enabled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Enabled = null,
    [property: JsonPropertyName("trigger"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AlertTrigger? Trigger = null,
    [property: JsonPropertyName("filter"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AlertFilter? Filter = null,
    [property: JsonPropertyName("notify"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<NotificationChannel>? Notify = null,
    [property: JsonPropertyName("throttle"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ThrottleConfig? Throttle = null);

/// <summary>One channel's delivery outcome for a fire.</summary>
public sealed record AlertDeliveryResult(
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error = null);

/// <summary>One recorded firing of an alert rule.</summary>
public sealed record AlertFire(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("alertRuleId")] string AlertRuleId,
    [property: JsonPropertyName("eventId")] string? EventId,
    [property: JsonPropertyName("dedupeKey")] string DedupeKey,
    [property: JsonPropertyName("deliveryResults")] IReadOnlyList<AlertDeliveryResult> DeliveryResults,
    [property: JsonPropertyName("firedAt")] string FiredAt);

// ── Events (durable memory event log) ─────────────────────────────────────────
// Mirrors thinkfleet-memory-sdk/src/resources/events.ts. Poll reads from
// /memory-events; emit writes to /lattice/events/emit.

/// <summary>One row from the memory event log. <c>SourceMemoryIds</c> /
/// <c>SourcePatternId</c> are provenance pointers back to the raw memories that
/// produced the event. <c>Severity</c> is info / warn / critical.</summary>
public sealed record MemoryEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("subject")] Subject? Subject,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("payload")] IReadOnlyDictionary<string, JsonElement> Payload,
    [property: JsonPropertyName("sourceMemoryIds")] IReadOnlyList<string> SourceMemoryIds,
    [property: JsonPropertyName("sourcePatternId")] string? SourcePatternId,
    [property: JsonPropertyName("emittedByPack")] string? EmittedByPack,
    [property: JsonPropertyName("occurredAt")] string OccurredAt);

/// <summary>Append an event to the durable log. Optional fields are omitted from
/// the wire when null. <c>Severity</c> defaults to "info" server-side.</summary>
public sealed record EmitEventRequest(
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("subject"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Subject? Subject = null,
    [property: JsonPropertyName("severity"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Severity = null,
    [property: JsonPropertyName("payloadJson"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PayloadJson = null,
    [property: JsonPropertyName("sourceMemoryIds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? SourceMemoryIds = null,
    [property: JsonPropertyName("sourcePatternId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourcePatternId = null);

/// <summary>The persisted event stub returned by an emit (null on a dedupe
/// collision).</summary>
public sealed record EmitEventResultEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("occurredAt")] string OccurredAt);

/// <summary>Outcome of an emit. <c>Emitted</c> is false when a dedupe collision
/// suppressed the insert; <c>AlertDispatches</c> counts the alert rules that
/// matched and dispatched.</summary>
public sealed record EmitEventResult(
    [property: JsonPropertyName("emitted")] bool Emitted,
    [property: JsonPropertyName("event")] EmitEventResultEvent? Event,
    [property: JsonPropertyName("alertDispatches")] int AlertDispatches);

// ── Typed attributes (structured/numeric data) ────────────────────────────────
// Mirrors thinkfleet-memory-sdk/src/resources/typed.ts. Surfaced under
// /api/v1/projects/{projectId}/memory-typed/*.

/// <summary>A registered attribute definition — drives input validation on
/// ingest. <c>DataType</c> is one of numeric / categorical / temporal / boolean;
/// values outside <c>MinValid</c>..<c>MaxValid</c> are quarantined.</summary>
public sealed record AttributeDef(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("attributeKey")] string AttributeKey,
    [property: JsonPropertyName("dataType")] string DataType,
    [property: JsonPropertyName("required")] bool Required,
    [property: JsonPropertyName("platformId")] string? PlatformId = null,
    [property: JsonPropertyName("projectId")] string? ProjectId = null,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("minValid")] double? MinValid = null,
    [property: JsonPropertyName("maxValid")] double? MaxValid = null,
    [property: JsonPropertyName("metadataJson")] string? MetadataJson = null);

/// <summary>Register-or-update an attribute definition. Optional fields are
/// omitted from the wire when null.</summary>
public sealed record RegisterAttributeRequest(
    [property: JsonPropertyName("attributeKey")] string AttributeKey,
    [property: JsonPropertyName("dataType")] string DataType,
    [property: JsonPropertyName("unit"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Unit = null,
    [property: JsonPropertyName("minValid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? MinValid = null,
    [property: JsonPropertyName("maxValid"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? MaxValid = null,
    [property: JsonPropertyName("required"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Required = null,
    [property: JsonPropertyName("metadata"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? Metadata = null);

/// <summary>One typed measurement of an attribute for a subject at a point in
/// time. Exactly one <c>Value*</c> field carries the reading. Optional fields are
/// omitted from the wire when null.</summary>
public sealed record TypedObservationInput(
    [property: JsonPropertyName("attributeKey")] string AttributeKey,
    [property: JsonPropertyName("subjectKind")] string SubjectKind,
    [property: JsonPropertyName("subjectExternalId")] string SubjectExternalId,
    [property: JsonPropertyName("observedAt")] string ObservedAt,
    [property: JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null,
    [property: JsonPropertyName("valueNumeric"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? ValueNumeric = null,
    [property: JsonPropertyName("valueText"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ValueText = null,
    [property: JsonPropertyName("valueBool"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ValueBool = null,
    [property: JsonPropertyName("valueTs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ValueTs = null,
    [property: JsonPropertyName("source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null,
    [property: JsonPropertyName("trust"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Trust = null);

/// <summary>A stored typed observation (input plus server-assigned id, scope, and
/// quality/status verdict). <c>Status</c> is accepted / quarantined.</summary>
public sealed record TypedObservation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("attributeKey")] string AttributeKey,
    [property: JsonPropertyName("subjectKind")] string SubjectKind,
    [property: JsonPropertyName("subjectExternalId")] string SubjectExternalId,
    [property: JsonPropertyName("observedAt")] string ObservedAt,
    [property: JsonPropertyName("platformId")] string? PlatformId = null,
    [property: JsonPropertyName("projectId")] string? ProjectId = null,
    [property: JsonPropertyName("valueNumeric")] double? ValueNumeric = null,
    [property: JsonPropertyName("valueText")] string? ValueText = null,
    [property: JsonPropertyName("valueBool")] bool? ValueBool = null,
    [property: JsonPropertyName("valueTs")] string? ValueTs = null,
    [property: JsonPropertyName("source")] string? Source = null,
    [property: JsonPropertyName("trust")] double? Trust = null,
    [property: JsonPropertyName("qualityScore")] double? QualityScore = null,
    [property: JsonPropertyName("status")] string? Status = null);

/// <summary>Outcome of a synchronous batch ingest. <c>QuarantineReasons</c> maps
/// observationId -&gt; reason.</summary>
public sealed record IngestReport(
    [property: JsonPropertyName("accepted")] int Accepted,
    [property: JsonPropertyName("quarantined")] int Quarantined,
    [property: JsonPropertyName("duplicates")] int Duplicates,
    [property: JsonPropertyName("quarantineReasons")] IReadOnlyDictionary<string, string> QuarantineReasons);

/// <summary>Outcome of an async enqueue — the count accepted onto the queue.</summary>
public sealed record EnqueueResult(
    [property: JsonPropertyName("enqueued")] int Enqueued);

/// <summary>Per-(subject, attribute) running statistics. <c>Mean</c> /
/// <c>Variance</c> / <c>Stddev</c> are derived on read.</summary>
public sealed record Accumulator(
    [property: JsonPropertyName("subjectKind")] string SubjectKind,
    [property: JsonPropertyName("subjectExternalId")] string SubjectExternalId,
    [property: JsonPropertyName("attributeKey")] string AttributeKey,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("sum")] double Sum,
    [property: JsonPropertyName("sumSq")] double SumSq,
    [property: JsonPropertyName("cumulative")] double Cumulative,
    [property: JsonPropertyName("minVal")] double? MinVal = null,
    [property: JsonPropertyName("maxVal")] double? MaxVal = null,
    [property: JsonPropertyName("lastVal")] double? LastVal = null,
    [property: JsonPropertyName("lastObservedAt")] string? LastObservedAt = null,
    [property: JsonPropertyName("ewma")] double? Ewma = null,
    [property: JsonPropertyName("ewmaVar")] double? EwmaVar = null,
    [property: JsonPropertyName("mean")] double? Mean = null,
    [property: JsonPropertyName("variance")] double? Variance = null,
    [property: JsonPropertyName("stddev")] double? Stddev = null);

// ── Health: biological age + condition prediction ────────────────────────────
// Mirrors thinkfleet-memory-sdk/src/types/health.ts. Health data IS memory data:
// record biomarkers / demographics / diagnoses as fact memories via /admin/memory
// and the engine derives the profile + cohort risk at /lattice/health/*.

/// <summary>Subject demographics. Latest values win.</summary>
public sealed record DemographicsInput(
    [property: JsonPropertyName("ageYears"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? AgeYears = null,
    [property: JsonPropertyName("sex"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sex = null,
    [property: JsonPropertyName("weightKg"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? WeightKg = null,
    [property: JsonPropertyName("heightCm"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? HeightCm = null,
    [property: JsonPropertyName("activity"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Activity = null);

/// <summary>An ICD-10 diagnosis. <c>Status</c> is active / resolved / historical.</summary>
public sealed record ConditionInput(
    [property: JsonPropertyName("icd10")] string Icd10,
    [property: JsonPropertyName("status"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status = null,
    [property: JsonPropertyName("onsetAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OnsetAt = null);

/// <summary>One contributing term of a biological-age estimate.</summary>
public sealed record HealthAgeComponent(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("yearsDelta")] double YearsDelta);

/// <summary>Biological ("health") age estimate for a subject. <c>Method</c> is
/// "phenoage_hybrid" or "composite". <c>MortalityScore</c> is a 10-year PhenoAge
/// mortality score (0..1) when available.</summary>
public sealed record BiologicalAge(
    [property: JsonPropertyName("biologicalAgeYears")] double BiologicalAgeYears,
    [property: JsonPropertyName("chronologicalAgeYears")] double ChronologicalAgeYears,
    [property: JsonPropertyName("deltaYears")] double DeltaYears,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("components")] IReadOnlyList<HealthAgeComponent> Components,
    [property: JsonPropertyName("mortalityScore")] double? MortalityScore = null);

/// <summary>A projected/current condition. <c>Basis</c> is "above_threshold_now"
/// or "threshold_projection"; <c>ProjectedOnsetAt</c> is set only for the latter.</summary>
public sealed record PredictedHealthCondition(
    [property: JsonPropertyName("condition")] string Condition,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("basis")] string Basis,
    [property: JsonPropertyName("biomarker")] string Biomarker,
    [property: JsonPropertyName("currentValue")] double CurrentValue,
    [property: JsonPropertyName("threshold")] double Threshold,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("sourceMemoryIds")] IReadOnlyList<string> SourceMemoryIds,
    [property: JsonPropertyName("projectedOnsetAt")] string? ProjectedOnsetAt = null);

/// <summary>A normalized biomarker reading on record.</summary>
public sealed record BiomarkerReading(
    [property: JsonPropertyName("biomarker")] string Biomarker,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("observedAt")] string ObservedAt);

/// <summary>Derived health profile: biological age + condition predictions +
/// latest biomarkers for a subject. Screening indicators, not a diagnosis.</summary>
public sealed record HealthProfile(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("predictedConditions")] IReadOnlyList<PredictedHealthCondition> PredictedConditions,
    [property: JsonPropertyName("diagnosedConditions")] IReadOnlyList<string> DiagnosedConditions,
    [property: JsonPropertyName("latestBiomarkers")] IReadOnlyList<BiomarkerReading> LatestBiomarkers,
    [property: JsonPropertyName("disclaimer")] string Disclaimer,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt,
    [property: JsonPropertyName("biologicalAge")] BiologicalAge? BiologicalAge = null);

/// <summary>Condition prevalence among the cohort most similar to a subject.</summary>
public sealed record CohortConditionRisk(
    [property: JsonPropertyName("condition")] string Condition,
    [property: JsonPropertyName("cohortPrevalence")] double CohortPrevalence,
    [property: JsonPropertyName("cohortSize")] int CohortSize,
    [property: JsonPropertyName("countWith")] int CountWith,
    [property: JsonPropertyName("meanSimilarity")] double MeanSimilarity,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("rationale")] string Rationale);

/// <summary>Cohort outcomes — epidemiological base rates for a subject.</summary>
public sealed record CohortHealthRisk(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("cohortSize")] int CohortSize,
    [property: JsonPropertyName("populationSize")] int PopulationSize,
    [property: JsonPropertyName("risks")] IReadOnlyList<CohortConditionRisk> Risks,
    [property: JsonPropertyName("disclaimer")] string Disclaimer,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt);

// ── Compliance: GDPR-grade export / erasure / audit / packs ──────────────────
// Mirrors thinkfleet-memory-sdk/src/resources/compliance.ts. Routes live under
// /memory-compliance/* and /memory-compliance-packs.

/// <summary>Row counts of a subject export bundle.</summary>
public sealed record ExportCounts(
    [property: JsonPropertyName("memories")] int Memories,
    [property: JsonPropertyName("patterns")] int Patterns,
    [property: JsonPropertyName("observations")] int Observations,
    [property: JsonPropertyName("events")] int Events,
    [property: JsonPropertyName("alertFires")] int AlertFires);

/// <summary>Everything held on a subject (Art. 15). Rows kept as raw JSON.</summary>
public sealed record ExportBundle(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("memories")] IReadOnlyList<JsonElement> Memories,
    [property: JsonPropertyName("patterns")] IReadOnlyList<JsonElement> Patterns,
    [property: JsonPropertyName("observations")] IReadOnlyList<JsonElement> Observations,
    [property: JsonPropertyName("events")] IReadOnlyList<JsonElement> Events,
    [property: JsonPropertyName("alert_fires")] IReadOnlyList<JsonElement> AlertFires,
    [property: JsonPropertyName("generated_at")] string GeneratedAt);

/// <summary>Art. 15 subject-access response: the bundle, its counts, and timing.</summary>
public sealed record ExportSubjectResponse(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("counts")] ExportCounts Counts,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt,
    [property: JsonPropertyName("durationMs")] double DurationMs,
    [property: JsonPropertyName("export")] ExportBundle? Export = null);

/// <summary>Art. 17 right-to-erasure response: what was (or would be) deleted.</summary>
public sealed record HardDeleteSubjectResponse(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("memoriesDeleted")] int MemoriesDeleted,
    [property: JsonPropertyName("patternsDeleted")] int PatternsDeleted,
    [property: JsonPropertyName("observationsDeleted")] int ObservationsDeleted,
    [property: JsonPropertyName("eventsDeleted")] int EventsDeleted,
    [property: JsonPropertyName("alertFiresDeleted")] int AlertFiresDeleted,
    [property: JsonPropertyName("dryRun")] bool DryRun,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt,
    [property: JsonPropertyName("durationMs")] double DurationMs,
    [property: JsonPropertyName("auditEventId")] string? AuditEventId = null);

/// <summary>A row from the memory_audit_event log (GDPR Art. 15 "who accessed
/// my data"). <c>EventType</c> is e.g. "read.search", "subject.hard_delete".</summary>
public sealed record AuditEvent(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("created")] string Created,
    [property: JsonPropertyName("actor")] string Actor,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("resultCount")] int ResultCount,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, JsonElement> Metadata,
    [property: JsonPropertyName("query")] string? Query = null,
    [property: JsonPropertyName("memoryIds")] string? MemoryIds = null);

/// <summary>An installed compliance pack and the memory classes / regulations it
/// claims jurisdiction over (e.g. HIPAA → "phi").</summary>
public sealed record CompliancePack(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("ownsClasses")] IReadOnlyList<string> OwnsClasses,
    [property: JsonPropertyName("regulatoryTags")] IReadOnlyList<string> RegulatoryTags);

/// <summary>Per-project enablement of a compliance pack. Packs absent from the
/// list fall back to the platform default set.</summary>
public sealed record ProjectPackEnablement(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("packId")] string PackId,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("config")] IReadOnlyDictionary<string, JsonElement> Config,
    [property: JsonPropertyName("created")] string Created,
    [property: JsonPropertyName("updated")] string Updated,
    [property: JsonPropertyName("enabledByUserId")] string? EnabledByUserId = null);

/// <summary>Enable / disable / reconfigure a compliance pack in the project.
/// Idempotent on <c>PackId</c>. <c>Config</c> is pack-owned opaque config.</summary>
public sealed record UpsertProjectPackRequest(
    [property: JsonPropertyName("packId")] string PackId,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("config"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, object?>? Config = null);

// ── Financial: indicators, portfolio risk, calibrated directional calls ──────
// Mirrors thinkfleet-memory-sdk/src/types/financial.ts. Ingest price bars /
// fundamentals / holdings / news as fact memories via /admin/memory; read
// derived indicators, risk, predictions, and calibration at /lattice/financial/*.
// Informational only — NOT investment advice.

/// <summary>One price bar (daily close). Market data — not subject-attributed.</summary>
public sealed record PriceInput(
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("close")] double Close,
    [property: JsonPropertyName("currency"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Currency = null,
    [property: JsonPropertyName("volume"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Volume = null,
    [property: JsonPropertyName("asOf"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AsOf = null);

/// <summary>Latest-wins fundamentals for a ticker.</summary>
public sealed record FundamentalInput(
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("peRatio"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? PeRatio = null,
    [property: JsonPropertyName("marketCap"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? MarketCap = null,
    [property: JsonPropertyName("dividendYield"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? DividendYield = null,
    [property: JsonPropertyName("eps"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Eps = null,
    [property: JsonPropertyName("debtToEquity"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? DebtToEquity = null,
    [property: JsonPropertyName("beta"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Beta = null,
    [property: JsonPropertyName("asOf"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AsOf = null);

/// <summary>A portfolio position. Restated, not summed — latest record wins.
/// <c>AssetClass</c> defaults to "equity" server-side.</summary>
public sealed record HoldingInput(
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("shares")] double Shares,
    [property: JsonPropertyName("costBasis"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? CostBasis = null,
    [property: JsonPropertyName("assetClass"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssetClass = null);

/// <summary>A news event. Tag one ticker or many; supply a sentiment in [-1, 1]
/// or omit to let the engine score the headline.</summary>
public sealed record NewsInput(
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("ticker"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Ticker = null,
    [property: JsonPropertyName("tickers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Tickers = null,
    [property: JsonPropertyName("sentiment"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Sentiment = null,
    [property: JsonPropertyName("source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null,
    [property: JsonPropertyName("publishedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PublishedAt = null);

/// <summary>Technical indicators for a ticker. <c>BetaSource</c> is
/// "none" / "computed" / "reported".</summary>
public sealed record TechnicalIndicators(
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("lastClose")] double LastClose,
    [property: JsonPropertyName("asOf")] string AsOf,
    [property: JsonPropertyName("betaSource")] string BetaSource,
    [property: JsonPropertyName("sampleSize")] int SampleSize,
    [property: JsonPropertyName("sourceMemoryIds")] IReadOnlyList<string> SourceMemoryIds,
    [property: JsonPropertyName("sma20")] double? Sma20 = null,
    [property: JsonPropertyName("sma50")] double? Sma50 = null,
    [property: JsonPropertyName("sma200")] double? Sma200 = null,
    [property: JsonPropertyName("ema12")] double? Ema12 = null,
    [property: JsonPropertyName("ema26")] double? Ema26 = null,
    [property: JsonPropertyName("rsi14")] double? Rsi14 = null,
    [property: JsonPropertyName("macd")] double? Macd = null,
    [property: JsonPropertyName("macdSignal")] double? MacdSignal = null,
    [property: JsonPropertyName("macdHistogram")] double? MacdHistogram = null,
    [property: JsonPropertyName("bollingerUpper")] double? BollingerUpper = null,
    [property: JsonPropertyName("bollingerMid")] double? BollingerMid = null,
    [property: JsonPropertyName("bollingerLower")] double? BollingerLower = null,
    [property: JsonPropertyName("bollingerPctB")] double? BollingerPctB = null,
    [property: JsonPropertyName("annualizedVolatility")] double? AnnualizedVolatility = null,
    [property: JsonPropertyName("trailingReturn")] double? TrailingReturn = null,
    [property: JsonPropertyName("maxDrawdown")] double? MaxDrawdown = null,
    [property: JsonPropertyName("sharpe")] double? Sharpe = null,
    [property: JsonPropertyName("beta")] double? Beta = null);

/// <summary>Latest fundamentals snapshot for a ticker.</summary>
public sealed record FundamentalSnapshot(
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("asOf")] string AsOf,
    [property: JsonPropertyName("sourceMemoryId")] string SourceMemoryId,
    [property: JsonPropertyName("peRatio")] double? PeRatio = null,
    [property: JsonPropertyName("marketCap")] double? MarketCap = null,
    [property: JsonPropertyName("dividendYield")] double? DividendYield = null,
    [property: JsonPropertyName("eps")] double? Eps = null,
    [property: JsonPropertyName("debtToEquity")] double? DebtToEquity = null,
    [property: JsonPropertyName("beta")] double? Beta = null);

/// <summary>A priced portfolio position.</summary>
public sealed record PortfolioPosition(
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("shares")] double Shares,
    [property: JsonPropertyName("lastClose")] double LastClose,
    [property: JsonPropertyName("marketValue")] double MarketValue,
    [property: JsonPropertyName("weight")] double Weight,
    [property: JsonPropertyName("assetClass")] string AssetClass,
    [property: JsonPropertyName("costBasis")] double? CostBasis = null,
    [property: JsonPropertyName("unrealizedPnl")] double? UnrealizedPnl = null);

/// <summary>Portfolio value by asset class.</summary>
public sealed record AssetAllocation(
    [property: JsonPropertyName("assetClass")] string AssetClass,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("weight")] double Weight);

/// <summary>Portfolio risk rollup. <c>ConcentrationHhi</c> is the Herfindahl
/// index of position weights [0, 1].</summary>
public sealed record PortfolioRisk(
    [property: JsonPropertyName("totalValue")] double TotalValue,
    [property: JsonPropertyName("concentrationHhi")] double ConcentrationHhi,
    [property: JsonPropertyName("allocations")] IReadOnlyList<AssetAllocation> Allocations,
    [property: JsonPropertyName("varMethod")] string VarMethod,
    [property: JsonPropertyName("weightedBeta")] double? WeightedBeta = null,
    [property: JsonPropertyName("weightedAnnualizedVolatility")] double? WeightedAnnualizedVolatility = null,
    [property: JsonPropertyName("valueAtRisk95_1d")] double? ValueAtRisk951d = null);

/// <summary>Indicators + (for a portfolio subject) risk rollup. Read-only and
/// forecast-free. Informational only — not investment advice.</summary>
public sealed record FinancialProfile(
    [property: JsonPropertyName("subject")] Subject Subject,
    [property: JsonPropertyName("indicators")] IReadOnlyList<TechnicalIndicators> Indicators,
    [property: JsonPropertyName("fundamentals")] IReadOnlyList<FundamentalSnapshot> Fundamentals,
    [property: JsonPropertyName("positions")] IReadOnlyList<PortfolioPosition> Positions,
    [property: JsonPropertyName("unpricedHoldings")] IReadOnlyList<string> UnpricedHoldings,
    [property: JsonPropertyName("disclaimer")] string Disclaimer,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt,
    [property: JsonPropertyName("portfolioRisk")] PortfolioRisk? PortfolioRisk = null);

/// <summary>A directional buy/sell/hold call. <c>ReportedConfidence</c> =
/// structural agreement × the strategy's realized reliability.</summary>
public sealed record FinancialSignal(
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("structuralConfidence")] double StructuralConfidence,
    [property: JsonPropertyName("reportedConfidence")] double ReportedConfidence,
    [property: JsonPropertyName("expectedReturn")] double ExpectedReturn,
    [property: JsonPropertyName("horizonDays")] int HorizonDays,
    [property: JsonPropertyName("basisClose")] double BasisClose,
    [property: JsonPropertyName("dueAt")] string DueAt,
    [property: JsonPropertyName("rationale")] IReadOnlyList<string> Rationale,
    [property: JsonPropertyName("newsUsed")] bool NewsUsed,
    [property: JsonPropertyName("sourceMemoryIds")] IReadOnlyList<string> SourceMemoryIds,
    [property: JsonPropertyName("predictionId")] string? PredictionId = null);

/// <summary>Result of a predict run. <c>StrategyReliability</c> is the reliability
/// multiplier applied, computed from <c>ResolvedSample</c> resolved calls.</summary>
public sealed record PredictFinancialResult(
    [property: JsonPropertyName("signals")] IReadOnlyList<FinancialSignal> Signals,
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("strategyReliability")] double StrategyReliability,
    [property: JsonPropertyName("resolvedSample")] int ResolvedSample,
    [property: JsonPropertyName("disclaimer")] string Disclaimer,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt);

/// <summary>Outcome of a reconcile pass over due predictions.</summary>
public sealed record ReconcileFinancialResult(
    [property: JsonPropertyName("scored")] int Scored,
    [property: JsonPropertyName("hits")] int Hits,
    [property: JsonPropertyName("misses")] int Misses,
    [property: JsonPropertyName("stillPending")] int StillPending,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt);

/// <summary>One confidence band of the calibration report.</summary>
public sealed record FinancialCalibrationBucket(
    [property: JsonPropertyName("lower")] double Lower,
    [property: JsonPropertyName("upper")] double Upper,
    [property: JsonPropertyName("predictions")] int Predictions,
    [property: JsonPropertyName("hits")] int Hits,
    [property: JsonPropertyName("misses")] int Misses,
    [property: JsonPropertyName("realizedHitRate")] double RealizedHitRate,
    [property: JsonPropertyName("hasData")] bool HasData);

/// <summary>Reported-vs-realized confidence, bucketed. The honesty proof.
/// <c>Strategy</c> is "all" when unfiltered.</summary>
public sealed record FinancialCalibrationReport(
    [property: JsonPropertyName("buckets")] IReadOnlyList<FinancialCalibrationBucket> Buckets,
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("strategyReliability")] double StrategyReliability,
    [property: JsonPropertyName("totalResolved")] int TotalResolved,
    [property: JsonPropertyName("generatedAt")] string GeneratedAt);

// ── Knowledge graph ─────────────────────────────────────────────────────────

/// <summary>A resolved thing — person, org, product, concept — filed under
/// <c>CanonicalName</c>, with <c>Aliases</c> resolving to it.</summary>
public sealed record MemoryEntity(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("canonicalName")] string CanonicalName,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("scope")] string? Scope = null,
    [property: JsonPropertyName("aliases")] List<string>? Aliases = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("projectId")] string? ProjectId = null,
    // The brain that first created this entity. Entities dedupe per project, so
    // this is provenance, NOT an isolation key — brain-scoped graph work filters
    // on the edge's brain, which the read routes apply server-side.
    [property: JsonPropertyName("brainId")] string? BrainId = null,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, JsonElement>? Metadata = null,
    [property: JsonPropertyName("validFrom")] string? ValidFrom = null,
    // Null while the entity is still current.
    [property: JsonPropertyName("validTo")] string? ValidTo = null,
    [property: JsonPropertyName("supersededById")] string? SupersededById = null);

/// <summary>An edge as the READ routes return it — hydrated, not the raw
/// <c>memory_edge</c> row. <c>Subject</c> and <c>Object</c> are resolved
/// entities rather than ids, plus a <c>Hop</c> counter.
///
/// This is the server's <c>GraphTraversalEdge</c>, returned by
/// <c>ListEdgesAsync</c>, <c>TraverseAsync</c>, and the edges of
/// <c>GetEntityAsync</c>. The raw row shape (<c>subjectId</c> / <c>objectId</c>)
/// is not exposed by any read route, so it is deliberately not modelled — a
/// type nothing returns is a trap.</summary>
public sealed record GraphTraversalEdge(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("subject")] MemoryEntity Subject,
    [property: JsonPropertyName("predicate")] string Predicate,
    // Null when ObjectLiteral carries the value instead.
    [property: JsonPropertyName("object")] MemoryEntity? Object = null,
    [property: JsonPropertyName("objectLiteral")] string? ObjectLiteral = null,
    [property: JsonPropertyName("weight")] double Weight = 0,
    [property: JsonPropertyName("validFrom")] string? ValidFrom = null,
    [property: JsonPropertyName("validTo")] string? ValidTo = null,
    [property: JsonPropertyName("sourceMemoryId")] string? SourceMemoryId = null,
    // Distance from the seed on a traverse — 1 for a direct neighbour.
    // ListEdges has no seed, so every edge comes back with Hop = 0.
    [property: JsonPropertyName("hop")] int Hop = 0);

/// <summary>Whether KG extraction is on, platform-wide and for this project.</summary>
public sealed record ExtractionState(
    [property: JsonPropertyName("platformEnabled")] bool PlatformEnabled = false,
    [property: JsonPropertyName("projectEnabled")] bool ProjectEnabled = false);

/// <summary>Aggregate graph counts.
///
/// <c>MemoriesWithEdges</c> against your total memory count is the useful ratio:
/// it says how much of what you remember made it into the graph rather than
/// remaining an isolated embedding. A low ratio usually means extraction is off
/// — check <c>Extraction</c> before concluding the corpus simply had no
/// relations in it.</summary>
public sealed record GraphStats
{
    [JsonPropertyName("entityCount")] public long EntityCount { get; init; }
    [JsonPropertyName("edgeCount")] public long EdgeCount { get; init; }
    /// <summary>Distinct memories that produced at least one edge.</summary>
    [JsonPropertyName("memoriesWithEdges")] public long MemoriesWithEdges { get; init; }
    [JsonPropertyName("retiredEntities")] public long RetiredEntities { get; init; }
    [JsonPropertyName("retiredEdges")] public long RetiredEdges { get; init; }
    /// <summary>Live entity counts keyed by entity type.</summary>
    [JsonPropertyName("entitiesByType")] public Dictionary<string, long> EntitiesByType { get; init; } = new();
    [JsonPropertyName("extraction")] public ExtractionState? Extraction { get; init; }
}

/// <summary>An entity plus its 1-hop neighbourhood.</summary>
public sealed record EntityWithEdges
{
    [JsonPropertyName("entity")] public MemoryEntity? Entity { get; init; }
    [JsonPropertyName("edges")] public List<GraphTraversalEdge> Edges { get; init; } = new();
}
