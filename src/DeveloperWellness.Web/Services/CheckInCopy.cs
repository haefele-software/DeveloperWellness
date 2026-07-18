namespace DeveloperWellness.Web.Services;

/// <summary>
/// Shared plain-language copy for check-in counts, so the "N people might need a check-in" family of
/// phrases pluralizes identically everywhere it appears: the Overview and Team overview headlines, the
/// check-in roster headline, the shell's alert pill, and the Overview's "might need a check-in" KPI unit.
/// </summary>
public static class CheckInCopy
{
    /// <summary>
    /// "Nobody appears to need a check-in right now" at zero, otherwise "1 person might need a check-in"
    /// or "{n} people might need a check-in" (Team overview and check-in roster headlines).
    /// </summary>
    public static string Headline(int flaggedCount) => flaggedCount switch
    {
        0 => "Nobody appears to need a check-in right now",
        1 => "1 person might need a check-in",
        _ => $"{flaggedCount} people might need a check-in",
    };

    /// <summary>
    /// "1 person newly needs a check-in" or "{n} people newly need a check-in" (shell alert pill; only
    /// rendered when <paramref name="unseenCount"/> is greater than zero).
    /// </summary>
    public static string AlertText(int unseenCount) =>
        unseenCount == 1 ? "1 person newly needs a check-in" : $"{unseenCount} people newly need a check-in";

    /// <summary>"person" for exactly one, otherwise "people" (KPI tile units and similar count-adjacent labels).</summary>
    public static string PersonOrPeople(int count) => count == 1 ? "person" : "people";
}
