namespace DeveloperWellness.Domain.Model;

/// <summary>
/// One comment author's tone distribution for a scope and period (data-model.md ToneAggregate; FR-018,
/// FR-019, FR-020). Never aggregated or displayed per project — this is per author only, and its only
/// consumer is the check-in roster's frustration mention (FR-018). Built by
/// <see cref="Signals.ToneAggregator"/> from classified comments plus each author's total authored-comment
/// count for the period, so <see cref="TotalCount"/> can exceed <see cref="AnalysedCount"/> whenever the
/// tone analysis cap (<c>WellnessOptions.ToneCommentCap</c>) or partial analysis leaves some of an author's
/// comments unclassified (FR-020).
/// </summary>
/// <param name="Positive">Comments classified as positive.</param>
/// <param name="Neutral">Comments classified as neutral.</param>
/// <param name="Negative">Comments classified as negative.</param>
/// <param name="Unanalysed">
/// Comments that could not be classified; excluded from <see cref="AnalysedCount"/> and never treated as
/// negative (FR-018 edge case).
/// </param>
/// <param name="AnalysedCount">The classified sample size: <see cref="Positive"/> + <see cref="Neutral"/> + <see cref="Negative"/>.</param>
/// <param name="TotalCount">
/// Every comment this author wrote in the period, whether or not it was analysed; never below
/// <see cref="AnalysedCount"/> + <see cref="Unanalysed"/>.
/// </param>
/// <param name="NegativeShare">The negative share of <see cref="AnalysedCount"/> only; 0 when <see cref="AnalysedCount"/> is 0.</param>
/// <param name="Flagged">
/// True when <see cref="NegativeShare"/> strictly exceeds the configured threshold and <see cref="AnalysedCount"/>
/// meets the configured minimum (FR-019).
/// </param>
/// <param name="Flag">The resulting <c>FlagKind.NegativeTone</c> flag when <see cref="Flagged"/> is true; otherwise null.</param>
public sealed record ToneAggregate(
    int Positive,
    int Neutral,
    int Negative,
    int Unanalysed,
    int AnalysedCount,
    int TotalCount,
    decimal NegativeShare,
    bool Flagged,
    WellbeingFlag? Flag);

/// <summary>
/// The organisation-level review-comment tone distribution across every analysed comment from every
/// author, excluding the unmatched bucket (data-model.md SentimentReading; FR-039). Rendered only on the
/// Overview, never per project or per developer beyond the roster's frustration mention (FR-018).
/// </summary>
/// <param name="Positive">
/// The positive share of every analysed comment, organisation-wide; an exact (unrounded) fraction in
/// [0, 1] so the three shares sum to as close to 1.0 as decimal arithmetic allows.
/// </param>
/// <param name="Neutral">The neutral share of every analysed comment, organisation-wide; exact (unrounded), same basis as <see cref="Positive"/>.</param>
/// <param name="Negative">The negative share of every analysed comment, organisation-wide; exact (unrounded), same basis as <see cref="Positive"/>.</param>
/// <param name="Available">False when no comment anywhere was analysed, in which case the three shares are 0 rather than meaningful.</param>
public sealed record SentimentReading(decimal Positive, decimal Neutral, decimal Negative, bool Available);
