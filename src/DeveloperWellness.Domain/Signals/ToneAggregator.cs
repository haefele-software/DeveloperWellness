using DeveloperWellness.Domain.Model;
using DeveloperWellness.Domain.Options;

namespace DeveloperWellness.Domain.Signals;

/// <summary>
/// Builds each comment author's tone distribution and the organisation-level sentiment reading from
/// already-classified comments (data-model.md ToneAggregate and SentimentReading; FR-018, FR-019, FR-020,
/// FR-039; design contract sections 4.3 and 5). Pure, synchronous, and reference-free like every Domain
/// signal calculator: it takes classification results as input and never calls the AI service itself.
/// </summary>
/// <remarks>
/// <see cref="DeveloperLogin.Unmatched"/> is skipped entirely, both from <see cref="ToneAggregation.ByAuthor"/>
/// and from the organisation-level <see cref="ToneAggregation.Sentiment"/> totals, mirroring
/// <see cref="SpreadThinCalculator"/>: tone is never assessed for activity that cannot be attributed to a
/// roster member.
/// </remarks>
public static class ToneAggregator
{
    /// <summary>
    /// Groups <paramref name="classified"/> by non-<see cref="DeveloperLogin.Unmatched"/> author, counts
    /// each author's tone distribution, and raises <c>FlagKind.NegativeTone</c> per FR-019's guard: the
    /// negative share of the author's analysed comments must strictly exceed
    /// <see cref="WellnessOptions.NegativeToneThreshold"/> and their analysed count must be at least
    /// <see cref="WellnessOptions.MinAnalysedComments"/>. Also builds the organisation-level
    /// <see cref="SentimentReading"/> across every author's analysed comments (FR-039).
    /// </summary>
    /// <param name="classified">
    /// Every classified comment for the scope and period, as (author, tone) pairs. One entry per comment
    /// sent through classification, including <see cref="ToneClass.Unanalysed"/> results.
    /// </param>
    /// <param name="totalAuthoredCounts">
    /// Each author's total authored-comment count for the period (classified or not), used to populate
    /// <see cref="ToneAggregate.TotalCount"/>. An author missing from this dictionary falls back to their
    /// classified-comment count, and the result is never allowed to sit below that count either way — a
    /// defensive floor, since the tone cap and partial analysis only ever shrink the analysed sample, never
    /// grow it past what the author actually wrote.
    /// </param>
    /// <param name="options">Supplies <see cref="WellnessOptions.NegativeToneThreshold"/> and <see cref="WellnessOptions.MinAnalysedComments"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="classified"/>, <paramref name="totalAuthoredCounts"/>, or <paramref name="options"/> is null.
    /// </exception>
    public static ToneAggregation Calculate(
        IReadOnlyList<(DeveloperLogin Author, ToneClass Tone)> classified,
        IReadOnlyDictionary<DeveloperLogin, int> totalAuthoredCounts,
        WellnessOptions options)
    {
        ArgumentNullException.ThrowIfNull(classified);
        ArgumentNullException.ThrowIfNull(totalAuthoredCounts);
        ArgumentNullException.ThrowIfNull(options);

        var byAuthor = new Dictionary<DeveloperLogin, ToneAggregate>();
        var totalAnalysed = 0;
        var totalPositive = 0;
        var totalNeutral = 0;
        var totalNegative = 0;

        var groups = classified
            .Where(entry => !entry.Author.IsUnmatched)
            .GroupBy(entry => entry.Author);

        foreach (var group in groups)
        {
            var author = group.Key;
            var positive = group.Count(entry => entry.Tone == ToneClass.Positive);
            var neutral = group.Count(entry => entry.Tone == ToneClass.Neutral);
            var negative = group.Count(entry => entry.Tone == ToneClass.Negative);
            var unanalysed = group.Count(entry => entry.Tone == ToneClass.Unanalysed);

            var analysedCount = positive + neutral + negative;
            var classifiedCount = analysedCount + unanalysed;
            var totalCount = Math.Max(totalAuthoredCounts.GetValueOrDefault(author, classifiedCount), classifiedCount);
            var negativeShare = analysedCount > 0 ? (decimal)negative / analysedCount : 0m;
            var flagged = negativeShare > options.NegativeToneThreshold && analysedCount >= options.MinAnalysedComments;
            var flag = flagged
                ? new WellbeingFlag(FlagKind.NegativeTone, BuildReason(negative, analysedCount, totalCount, negativeShare))
                : null;

            byAuthor[author] = new ToneAggregate(
                positive, neutral, negative, unanalysed, analysedCount, totalCount, negativeShare, flagged, flag);

            totalAnalysed += analysedCount;
            totalPositive += positive;
            totalNeutral += neutral;
            totalNegative += negative;
        }

        var sentiment = totalAnalysed > 0
            ? new SentimentReading(
                (decimal)totalPositive / totalAnalysed,
                (decimal)totalNeutral / totalAnalysed,
                (decimal)totalNegative / totalAnalysed,
                Available: true)
            : new SentimentReading(Positive: 0m, Neutral: 0m, Negative: 0m, Available: false);

        return new ToneAggregation(byAuthor, sentiment);
    }

    /// <summary>
    /// Builds the design contract's frustration-mention reason (section 4.3): observation, then the
    /// analysed-sample note only when <paramref name="totalCount"/> exceeds <paramref name="analysedCount"/>
    /// (FR-020), then the supportive "climate, not character" framing. No apostrophe contractions, per
    /// project convention for user-facing copy.
    /// </summary>
    private static string BuildReason(int negative, int analysedCount, int totalCount, decimal negativeShare)
    {
        var percent = (int)Math.Round(negativeShare * 100m, MidpointRounding.AwayFromZero);
        var sampleNote = totalCount > analysedCount
            ? $" ({analysedCount} of {totalCount} written were analysed)"
            : string.Empty;

        return $"{negative} of {analysedCount} analysed comments ({percent}%) read more negative than usual this period{sampleNote}. " +
               "It is climate, not character — review pressure is usually the cause.";
    }
}

/// <summary>
/// The result of one <see cref="ToneAggregator.Calculate"/> call: every author's tone distribution plus
/// the organisation-level sentiment reading (data-model.md ToneAggregate and SentimentReading).
/// </summary>
public sealed record ToneAggregation(IReadOnlyDictionary<DeveloperLogin, ToneAggregate> ByAuthor, SentimentReading Sentiment);
