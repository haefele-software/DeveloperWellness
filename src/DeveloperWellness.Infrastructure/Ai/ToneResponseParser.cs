using System.Text.Json;
using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Infrastructure.Ai;

/// <summary>
/// Parses a Foundry chat response that is expected to contain a strict JSON array of tone labels
/// ("positive", "neutral", "negative", "unanalysable") into <see cref="ToneClass"/> values, tolerating
/// the ways a chat model's raw text output can deviate from that strict contract (FR-018, FR-020,
/// research R3). Never throws: any response this cannot make sense of maps to
/// <see cref="ToneClass.Unanalysed"/> for every expected entry, because an unparseable response was
/// analysed-but-unusable, not a failed request — that distinction belongs to the caller, which only
/// throws or returns a partial prefix when the chat call itself fails (FR-020).
/// </summary>
public static class ToneResponseParser
{
    /// <summary>
    /// Parses <paramref name="responseText"/> into exactly <paramref name="expectedCount"/>
    /// <see cref="ToneClass"/> values, one per comment sent in the batch, in the same order.
    /// </summary>
    /// <param name="responseText">The raw chat response text for one batch.</param>
    /// <param name="expectedCount">The number of comments sent in this batch.</param>
    /// <returns>
    /// Exactly <paramref name="expectedCount"/> entries. Each recovered array element maps
    /// case-insensitively: "positive", "neutral", "negative" to the matching <see cref="ToneClass"/>;
    /// "unanalysable" or any other token to <see cref="ToneClass.Unanalysed"/>. A recovered array shorter
    /// than <paramref name="expectedCount"/> is padded with <see cref="ToneClass.Unanalysed"/> entries; a
    /// longer array is truncated to the expected length. A response that cannot be parsed as a JSON array
    /// at all yields <see cref="ToneClass.Unanalysed"/> for every entry — leniency applies per element,
    /// never a blanket <see cref="ToneClass.Negative"/> default.
    /// </returns>
    public static IReadOnlyList<ToneClass> Parse(string responseText, int expectedCount)
    {
        if (expectedCount <= 0)
        {
            return [];
        }

        var tokens = TryExtractArray(responseText);

        var result = new List<ToneClass>(expectedCount);
        for (var i = 0; i < expectedCount; i++)
        {
            result.Add(tokens is not null && i < tokens.Count ? MapToken(tokens[i]) : ToneClass.Unanalysed);
        }

        return result;
    }

    /// <summary>
    /// Attempts to recover the JSON string array from <paramref name="responseText"/>, tolerating a
    /// surrounding Markdown code fence and leading or trailing whitespace or prose. Returns null when no
    /// array can be recovered at all.
    /// </summary>
    private static List<string>? TryExtractArray(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        var candidate = StripCodeFences(responseText.Trim());

        return TryParseJsonArray(candidate) ?? TryParseJsonArray(ExtractBracketedSubstring(candidate));
    }

    /// <summary>Removes a leading and trailing Markdown code fence (with an optional language tag) if present.</summary>
    private static string StripCodeFences(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var afterOpenFence = text.IndexOf('\n');
        if (afterOpenFence < 0)
        {
            return text;
        }

        var body = text[(afterOpenFence + 1)..];
        var closeFenceIndex = body.LastIndexOf("```", StringComparison.Ordinal);
        return closeFenceIndex >= 0 ? body[..closeFenceIndex] : body;
    }

    /// <summary>
    /// Last-resort recovery for a response with stray prose around the array: the substring from the
    /// first '[' to the last ']'. Returns null when no bracket pair is present.
    /// </summary>
    private static string? ExtractBracketedSubstring(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    /// <summary>Parses <paramref name="text"/> as a JSON array; returns null on any parse failure or non-array root.</summary>
    private static List<string>? TryParseJsonArray(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var values = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                values.Add(element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : element.ToString());
            }

            return values;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Maps one array element case-insensitively; anything other than the three named classes is unanalysed.</summary>
    private static ToneClass MapToken(string token) => token.Trim().ToLowerInvariant() switch
    {
        "positive" => ToneClass.Positive,
        "neutral" => ToneClass.Neutral,
        "negative" => ToneClass.Negative,
        _ => ToneClass.Unanalysed,
    };
}
