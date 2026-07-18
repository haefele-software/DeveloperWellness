namespace DeveloperWellness.Domain.Model;

/// <summary>
/// An AI-generated summary for a subject, scope, and period (data-model.md AiSummary; FR-015..FR-017).
/// Produced by <c>IAiInsightService.SummariseAsync</c> in the Application layer; kept here because it is
/// a domain model in its own right, not a port-specific transport shape.
/// </summary>
/// <remarks>
/// Invariant: wherever rendered, this summary MUST carry the label
/// "AI-generated · [scope] · [period]" (FR-017); it is never presented as a verdict.
/// </remarks>
public sealed record AiSummary
{
    /// <summary>Creates a summary with its mandatory, non-empty text.</summary>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null, empty, or whitespace.</exception>
    public AiSummary(AiSubject subject, ScopeKey scope, Period period, string text, DateTimeOffset generatedAt, bool isDemo)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Summary text must not be empty.", nameof(text));
        }

        Subject = subject;
        Scope = scope;
        Period = period;
        Text = text;
        GeneratedAt = generatedAt;
        IsDemo = isDemo;
    }

    /// <summary>The project or developer this summary describes.</summary>
    public AiSubject Subject { get; }

    /// <summary>The scope the summary was generated for.</summary>
    public ScopeKey Scope { get; }

    /// <summary>The period the summary covers.</summary>
    public Period Period { get; }

    /// <summary>The supportively worded summary text, roughly 120 words or fewer (FR-015..FR-017).</summary>
    public string Text { get; }

    /// <summary>When this summary was generated.</summary>
    public DateTimeOffset GeneratedAt { get; }

    /// <summary>True when this summary came from the deterministic demo adapter rather than the live AI service.</summary>
    public bool IsDemo { get; }
}
