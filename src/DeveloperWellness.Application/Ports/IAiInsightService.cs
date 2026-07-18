using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Application.Ports;

/// <summary>
/// Produces AI-generated summaries and comment-tone classifications (contracts/application-ports.md,
/// Port 2). Every UI surface degrades to a friendly unavailable state, and rosters note absent tone
/// signals, whenever <see cref="IsAvailable"/> is false (FR-014, SC-009).
/// </summary>
public interface IAiInsightService
{
    /// <summary>
    /// False when the service is unconfigured (no endpoint or API key); true once ready to serve
    /// requests. Never requires interactive sign-in (research R3).
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Produces a roughly 120-word, supportively worded summary for <paramref name="subject"/>, grounded
    /// only in the aggregated <paramref name="grounding"/> statistics — never raw repository content
    /// (FR-015 to FR-017, FR-022).
    /// </summary>
    /// <exception cref="AiInsightException">The request failed, with a user-presentable message.</exception>
    Task<AiSummary> SummariseAsync(AiSubject subject, SummaryGrounding grounding, CancellationToken cancellationToken);

    /// <summary>
    /// Classifies the tone of each comment body, returning exactly one <see cref="ToneClass"/> per input,
    /// in the same order. Unparseable results map to <see cref="ToneClass.Unanalysed"/>, never
    /// <see cref="ToneClass.Negative"/>. On partial batch failure, returns the classified prefix so the
    /// caller can report analysed-versus-total (FR-020). The caller is responsible for batching and for
    /// enforcing the 200-comment cap before calling.
    /// </summary>
    /// <exception cref="AiInsightException">The request failed outright, with a user-presentable message.</exception>
    Task<IReadOnlyList<ToneClass>> ClassifyToneAsync(IReadOnlyList<string> commentBodies, CancellationToken cancellationToken);
}
