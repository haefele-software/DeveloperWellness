using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.OpenAI;
using DeveloperWellness.Application.Ports;
using DeveloperWellness.Domain.Model;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Infrastructure.Ai;

/// <summary>
/// Live <see cref="IAiInsightService"/> backed by the organisation's Foundry model deployment via
/// <see cref="IChatClient"/> (Microsoft.Extensions.AI over Azure.AI.OpenAI, research R3). Authenticates
/// with a plain API key from <see cref="AiOptions"/> and never performs interactive or Entra ID sign-in.
/// The supportive system prompt and output constraints in <see cref="SystemPrompt"/> are owned by this
/// application rather than by any hosted-agent configuration, so every summary honours FR-015 to FR-017
/// regardless of how the underlying deployment itself is configured.
/// </summary>
public sealed class FoundryAiInsightService(IOptions<AiOptions> options) : IAiInsightService
{
    /// <summary>
    /// The app-owned instructions sent as the system message on every summary request. Strict about
    /// grounding (never inventing a figure that was not supplied), wellbeing-first framing, and the
    /// roughly 120-word output bound (FR-015 to FR-017, FR-022).
    /// </summary>
    private const string SystemPrompt = """
        You are Pulse's supportive, non-judgemental engineering-wellbeing analyst. You write short
        summaries that help a manager understand how a developer or project is doing, from a
        wellbeing-first point of view, never from a productivity, ranking, or performance-review point
        of view.

        Follow these rules strictly:
        - Use ONLY the figures given to you in the user's message. If a figure is not provided, do not
          invent it, estimate it, or guess at it.
        - Never rank, score, or compare people against each other, and never imply one person is better
          or worse than another.
        - When a flagged signal is mentioned, describe it gently, as context for a caring conversation,
          never as a verdict, a criticism, or a warning.
        - Write at most 120 words of plain prose. No headings, no bullet points, no markdown formatting,
          no numbered lists.
        - Keep a warm, human, wellbeing-first tone throughout.
        """;

    /// <summary>Modest output cap keeping responses comfortably inside the roughly 120-word bound (FR-017).</summary>
    private const int MaxOutputTokens = 260;

    /// <summary>Low temperature for consistent, grounded phrasing rather than creative variation.</summary>
    private const float Temperature = 0.4f;

    /// <summary>User-presentable message for every failure class that must never leak an Azure or OpenAI exception type.</summary>
    private const string UnavailableMessage = "The summary service isn't reachable right now. Everything else keeps working.";

    /// <summary>Number of comments sent per tone-classification chat call, against the same deployment (research R3).</summary>
    private const int ToneBatchSize = 25;

    /// <summary>Defensive truncation applied to each comment body before it is sent to the model (FR-022).</summary>
    private const int MaxCommentBodyLength = 500;

    /// <summary>Bounded output cap: comfortably covers a strict JSON array of up to <see cref="ToneBatchSize"/> short string labels.</summary>
    private const int ToneMaxOutputTokens = 300;

    /// <summary>Low, near-deterministic temperature for consistent classification rather than creative variation.</summary>
    private const float ToneTemperature = 0.1f;

    /// <summary>User-presentable message for tone-analysis failures; never leaks an Azure or OpenAI exception type.</summary>
    private const string ToneUnavailableMessage = "The tone analysis service isn't reachable right now. Everything else keeps working.";

    /// <summary>
    /// The app-owned instructions sent as the system message on every tone-classification batch. Demands
    /// a strict JSON array with no surrounding prose, and names the exact four labels this adapter and
    /// <see cref="ToneResponseParser"/> agree on (FR-018, FR-020).
    /// </summary>
    private const string ToneSystemPrompt = """
        You are a strict tone-classification function for pull-request review comments. You never
        explain your reasoning and never add any commentary, preamble, or trailing text. You respond
        with ONLY a JSON array of strings, and nothing else.
        """;

    private static readonly ChatOptions ToneChatOptions = new()
    {
        MaxOutputTokens = ToneMaxOutputTokens,
        Temperature = ToneTemperature,
    };

    private static readonly JsonSerializerOptions GroundingJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _clientLock = new();
    private IChatClient? _chatClient;

    /// <inheritdoc />
    /// <remarks>False whenever any of <see cref="AiOptions.Endpoint"/>, <see cref="AiOptions.ApiKey"/>, or
    /// <see cref="AiOptions.DeploymentName"/> is unset (FR-014); this is the sole "unavailable" state for
    /// this adapter, so no separate placeholder implementation is needed.</remarks>
    public bool IsAvailable => IsConfigured(options.Value);

    /// <inheritdoc />
    public async Task<AiSummary> SummariseAsync(AiSubject subject, SummaryGrounding grounding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = options.Value;
        if (!IsConfigured(value))
        {
            throw new AiInsightException("The summary service isn't configured.");
        }

        var chatClient = GetOrCreateChatClient(value);

        var json = JsonSerializer.Serialize(grounding, GroundingJsonOptions);
        var userMessage =
            $"Subject: {grounding.SubjectDescriptor}\n\n" +
            "Grounding data (aggregated statistics only, JSON):\n" +
            json;

        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.System, SystemPrompt),
            new ChatMessage(ChatRole.User, userMessage),
        ];

        var chatOptions = new ChatOptions
        {
            MaxOutputTokens = MaxOutputTokens,
            Temperature = Temperature,
        };

        string text;
        try
        {
            var response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);
            text = response.Text.Trim();
        }
        catch (RequestFailedException ex)
        {
            throw new AiInsightException(UnavailableMessage, ex);
        }
        catch (ClientResultException ex)
        {
            throw new AiInsightException(UnavailableMessage, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiInsightException(UnavailableMessage, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiInsightException(UnavailableMessage, ex);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiInsightException(UnavailableMessage);
        }

        return new AiSummary(
            subject,
            ScopeFromGrounding(grounding),
            PeriodFromGrounding(grounding),
            text,
            generatedAt: DateTimeOffset.UtcNow,
            isDemo: false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Batches <paramref name="commentBodies"/> into groups of <see cref="ToneBatchSize"/> and issues one
    /// chat call per batch against the same deployment, demanding a strict JSON array of tone labels
    /// (research R3). Each batch's response is parsed leniently via <see cref="ToneResponseParser"/>: a
    /// response that cannot be parsed at all maps to <see cref="ToneClass.Unanalysed"/> for that whole
    /// batch rather than failing the request, because an unparseable response was analysed but unusable,
    /// not a failed call. Only the chat call itself failing (network or service failure) counts as a
    /// failure: if no batch has succeeded yet, the whole request throws <see cref="AiInsightException"/>;
    /// once at least one batch has succeeded, a later batch's call failure stops the loop and returns the
    /// classified prefix built so far — shorter than <paramref name="commentBodies"/> — so the caller can
    /// report analysed-versus-total (FR-020). Honours <paramref name="cancellationToken"/> between batches.
    /// </remarks>
    public async Task<IReadOnlyList<ToneClass>> ClassifyToneAsync(IReadOnlyList<string> commentBodies, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(commentBodies);

        var value = options.Value;
        if (!IsConfigured(value))
        {
            throw new AiInsightException("The tone analysis service isn't configured.");
        }

        if (commentBodies.Count == 0)
        {
            return [];
        }

        var chatClient = GetOrCreateChatClient(value);
        var results = new List<ToneClass>(commentBodies.Count);

        for (var offset = 0; offset < commentBodies.Count; offset += ToneBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchSize = Math.Min(ToneBatchSize, commentBodies.Count - offset);
            var batch = new List<string>(batchSize);
            for (var i = 0; i < batchSize; i++)
            {
                batch.Add(commentBodies[offset + i]);
            }

            string responseText;
            try
            {
                var response = await chatClient
                    .GetResponseAsync(BuildToneMessages(batch), ToneChatOptions, cancellationToken)
                    .ConfigureAwait(false);
                responseText = response.Text;
            }
            catch (RequestFailedException ex)
            {
                return CompleteOnBatchFailure(results, ex);
            }
            catch (ClientResultException ex)
            {
                return CompleteOnBatchFailure(results, ex);
            }
            catch (HttpRequestException ex)
            {
                return CompleteOnBatchFailure(results, ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                return CompleteOnBatchFailure(results, ex);
            }

            results.AddRange(ToneResponseParser.Parse(responseText, batchSize));
        }

        return results;
    }

    /// <summary>
    /// Resolves a batch chat-call failure per FR-020: if no batch has succeeded yet, escalates to a
    /// friendly <see cref="AiInsightException"/>; otherwise returns the classified prefix built from the
    /// batches that did succeed, stopping the loop rather than continuing past a failed batch.
    /// </summary>
    private static IReadOnlyList<ToneClass> CompleteOnBatchFailure(List<ToneClass> resultsSoFar, Exception cause) =>
        resultsSoFar.Count > 0
            ? resultsSoFar
            : throw new AiInsightException(ToneUnavailableMessage, cause);

    /// <summary>Builds the system and user chat messages demanding a strict JSON array for one batch.</summary>
    private static List<ChatMessage> BuildToneMessages(IReadOnlyList<string> batch) =>
    [
        new ChatMessage(ChatRole.System, ToneSystemPrompt),
        new ChatMessage(ChatRole.User, BuildToneUserPrompt(batch)),
    ];

    /// <summary>
    /// Numbers each comment 1..n and demands exactly n labels back, truncating each body defensively at
    /// <see cref="MaxCommentBodyLength"/> characters before it is sent (FR-022).
    /// </summary>
    private static string BuildToneUserPrompt(IReadOnlyList<string> batch)
    {
        var builder = new StringBuilder()
            .Append("Classify each numbered comment's tone. Respond with ONLY a JSON array of exactly ")
            .Append(batch.Count)
            .Append(" strings, each one of: \"positive\", \"neutral\", \"negative\", \"unanalysable\". No other text.\n\n");

        for (var i = 0; i < batch.Count; i++)
        {
            builder.Append(i + 1).Append(". ").Append(TruncateBody(batch[i])).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Defensive truncation so a single oversized comment body cannot balloon a batch's request size.</summary>
    private static string TruncateBody(string body)
    {
        var text = body ?? string.Empty;
        return text.Length <= MaxCommentBodyLength ? text : text[..MaxCommentBodyLength];
    }

    /// <summary>True when every one of the three connection settings is configured (FR-014).</summary>
    private static bool IsConfigured(AiOptions value) =>
        !string.IsNullOrWhiteSpace(value.Endpoint) &&
        !string.IsNullOrWhiteSpace(value.ApiKey) &&
        !string.IsNullOrWhiteSpace(value.DeploymentName);

    /// <summary>Returns the cached chat client, creating it under a lock on first use (thread-safe reuse across calls).</summary>
    private IChatClient GetOrCreateChatClient(AiOptions value)
    {
        if (_chatClient is { } existing)
        {
            return existing;
        }

        lock (_clientLock)
        {
            return _chatClient ??= new AzureOpenAIClient(new Uri(value.Endpoint!), new AzureKeyCredential(value.ApiKey!))
                .GetChatClient(value.DeploymentName!)
                .AsIChatClient();
        }
    }

    /// <summary>
    /// Reconstructs the scope this summary was generated for from <see cref="SummaryGrounding.ScopeLabel"/>,
    /// the only scope information the port's signature carries down to this adapter. "organisation"
    /// (case-insensitive) maps to <see cref="ScopeKey.Organisation"/>; anything else is treated as a
    /// project name (data-model.md documents <c>ScopeLabel</c> as exactly one of those two shapes).
    /// </summary>
    private static ScopeKey ScopeFromGrounding(SummaryGrounding grounding) =>
        string.Equals(grounding.ScopeLabel, "organisation", StringComparison.OrdinalIgnoreCase)
            ? ScopeKey.Organisation
            : ScopeKey.Project(grounding.ScopeLabel);

    /// <summary>
    /// Reconstructs the period this summary was generated for from <see cref="SummaryGrounding.PeriodDays"/>,
    /// the only period information the port's signature carries down to this adapter. The period is
    /// anchored to "now" because a summary is always requested for the currently displayed period, whose
    /// end is the moment of the request.
    /// </summary>
    private static Period PeriodFromGrounding(SummaryGrounding grounding) =>
        new(grounding.PeriodDays, DateTimeOffset.UtcNow);
}
