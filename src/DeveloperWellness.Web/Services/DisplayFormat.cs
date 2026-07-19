using System.Globalization;
using DeveloperWellness.Domain.Model;

namespace DeveloperWellness.Web.Services;

/// <summary>
/// Shared display formatting repeated, byte-identically, across Overview, CheckIns, TeamOverview,
/// DeveloperDetail, ProjectDetail, and Quality: the avatar circle's initials and deterministic colour, and
/// the whole-number percentage used for every share-based KPI, table cell, and bar value.
/// </summary>
public static class DisplayFormat
{
    /// <summary>Up to two initials from a display name (first plus last word), for the avatar circle.</summary>
    public static string Initials(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "?";
        }

        return parts.Length == 1
            ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
            : $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
    }

    /// <summary>A deterministic HSL colour derived from the login, so the same person always gets the same avatar colour.</summary>
    public static string AvatarColor(DeveloperLogin login)
    {
        var hue = ((StableHash(login.Value) % 360) + 360) % 360;
        return $"hsl({hue.ToString(CultureInfo.InvariantCulture)}, 55%, 45%)";
    }

    /// <summary>The share as a whole-number percentage (e.g. "42%"), or an em dash when <paramref name="share"/> is null.</summary>
    public static string FormatShare(decimal? share) =>
        share is { } value ? $"{Math.Round(value * 100m, MidpointRounding.AwayFromZero):0}%" : "—";

    /// <summary>
    /// A simple, process-independent string hash (unlike <see cref="string.GetHashCode()"/>, which is
    /// randomised per process), so the avatar hue is stable across app restarts and test runs.
    /// </summary>
    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in value)
            {
                hash = hash * 31 + c;
            }

            return hash;
        }
    }
}
