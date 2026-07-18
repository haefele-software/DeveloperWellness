namespace DeveloperWellness.Domain.Options;

/// <summary>
/// Wellness signal configuration (data-model.md WellnessOptions). A plain, framework-free record with
/// mutable properties so it binds directly from <c>IConfiguration</c> in the Web layer without this
/// project taking a dependency on Microsoft.Extensions.Options; <see cref="Validate"/> performs the
/// start-up checks the Web layer wires into its own options-validation pipeline.
/// </summary>
public sealed record WellnessOptions
{
    /// <summary>The configuration section name this record binds from.</summary>
    public const string SectionName = "Wellness";

    /// <summary>Allowed values for <see cref="PeriodDaysDefault"/>.</summary>
    private static readonly int[] AllowedPeriodDays = [7, 14, 30];

    /// <summary>When true, activity and AI data come from the deterministic demo adapters (FR-013). Defaults to true (R5).</summary>
    public bool DemoMode { get; set; } = true;

    /// <summary>The start of the working day in organisation-local time. Defaults to 09:00.</summary>
    public TimeOnly WorkingHoursStart { get; set; } = new(9, 0);

    /// <summary>The end of the working day in organisation-local time. Defaults to 18:00.</summary>
    public TimeOnly WorkingHoursEnd { get; set; } = new(18, 0);

    /// <summary>The days of the week considered working days. Defaults to Monday through Friday.</summary>
    public IReadOnlyList<DayOfWeek> WorkingDays { get; set; } =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
    ];

    /// <summary>
    /// The organisation's time zone id (IANA or Windows; .NET 10 resolves either). Required; validated
    /// via <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>.
    /// </summary>
    public string OrganisationTimeZone { get; set; } = string.Empty;

    /// <summary>Out-of-hours commit share above which <c>FlagKind.OverworkCommits</c> is raised. Defaults to 0.25.</summary>
    public decimal OutOfHoursThreshold { get; set; } = 0.25m;

    /// <summary>Minimum PR events (opens plus reviews) required before the after-hours PR flag can apply. Defaults to 3.</summary>
    public int MinPrEvents { get; set; } = 3;

    /// <summary>Distinct active projects at or above which <c>FlagKind.SpreadThin</c> is raised (organisation scope only). Defaults to 4.</summary>
    public int SpreadThinThreshold { get; set; } = 4;

    /// <summary>Negative-tone share above which <c>FlagKind.NegativeTone</c> is raised. Defaults to 0.20.</summary>
    public decimal NegativeToneThreshold { get; set; } = 0.20m;

    /// <summary>Minimum analysed comments required before tone results are surfaced. Defaults to 10.</summary>
    public int MinAnalysedComments { get; set; } = 10;

    /// <summary>Changes-requested share above which the rushing signal's rework condition is met. Defaults to 0.40.</summary>
    public decimal ChangesRequestedThreshold { get; set; } = 0.40m;

    /// <summary>Minimum pull-request sample size before the rushing signal is evaluated. Defaults to 3.</summary>
    public int MinPrSample { get; set; } = 3;

    /// <summary>Maximum number of most-recently-pushed repositories fetched per scope. Defaults to 10.</summary>
    public int RepoCap { get; set; } = 10;

    /// <summary>Maximum number of branches scanned per repository for commit collection. Defaults to 20.</summary>
    public int BranchCap { get; set; } = 20;

    /// <summary>Maximum number of comments sent for tone classification per fetch. Defaults to 200.</summary>
    public int ToneCommentCap { get; set; } = 200;

    /// <summary>Number of weeks of commit history used for the development trend. Defaults to 12.</summary>
    public int TrendWeeks { get; set; } = 12;

    /// <summary>The default period length in days when none is selected. Must be 7, 14, or 30. Defaults to 14.</summary>
    public int PeriodDaysDefault { get; set; } = 14;

    /// <summary>
    /// Validates every rule from data-model.md: thresholds in (0, 1]; caps and guards at least 1;
    /// <see cref="WorkingHoursStart"/> before <see cref="WorkingHoursEnd"/>; <see cref="PeriodDaysDefault"/>
    /// in {7, 14, 30}; <see cref="OrganisationTimeZone"/> resolvable. Collects every violation rather than
    /// failing fast, so a single start-up failure message can name every problem at once.
    /// </summary>
    /// <returns>The list of human-readable error messages; empty when every rule passes.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        ValidateThreshold(nameof(OutOfHoursThreshold), OutOfHoursThreshold, errors);
        ValidateThreshold(nameof(NegativeToneThreshold), NegativeToneThreshold, errors);
        ValidateThreshold(nameof(ChangesRequestedThreshold), ChangesRequestedThreshold, errors);

        ValidateCap(nameof(MinPrEvents), MinPrEvents, errors);
        ValidateCap(nameof(SpreadThinThreshold), SpreadThinThreshold, errors);
        ValidateCap(nameof(MinAnalysedComments), MinAnalysedComments, errors);
        ValidateCap(nameof(MinPrSample), MinPrSample, errors);
        ValidateCap(nameof(RepoCap), RepoCap, errors);
        ValidateCap(nameof(BranchCap), BranchCap, errors);
        ValidateCap(nameof(ToneCommentCap), ToneCommentCap, errors);
        ValidateCap(nameof(TrendWeeks), TrendWeeks, errors);

        if (WorkingHoursStart >= WorkingHoursEnd)
        {
            errors.Add(
                $"{nameof(WorkingHoursStart)} ({WorkingHoursStart}) must be earlier than {nameof(WorkingHoursEnd)} ({WorkingHoursEnd}).");
        }

        if (Array.IndexOf(AllowedPeriodDays, PeriodDaysDefault) < 0)
        {
            errors.Add($"{nameof(PeriodDaysDefault)} must be 7, 14, or 30 (was {PeriodDaysDefault}).");
        }

        if (string.IsNullOrWhiteSpace(OrganisationTimeZone))
        {
            errors.Add($"{nameof(OrganisationTimeZone)} is required.");
        }
        else
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(OrganisationTimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                errors.Add($"{nameof(OrganisationTimeZone)} '{OrganisationTimeZone}' could not be resolved to a known time zone id.");
            }
            catch (InvalidTimeZoneException)
            {
                errors.Add($"{nameof(OrganisationTimeZone)} '{OrganisationTimeZone}' is corrupt or invalid on this system.");
            }
        }

        return errors;
    }

    /// <summary>Adds an error when <paramref name="value"/> is not in the range (0, 1].</summary>
    private static void ValidateThreshold(string name, decimal value, List<string> errors)
    {
        if (value <= 0 || value > 1)
        {
            errors.Add($"{name} must be greater than 0 and less than or equal to 1 (was {value}).");
        }
    }

    /// <summary>Adds an error when <paramref name="value"/> is less than 1.</summary>
    private static void ValidateCap(string name, int value, List<string> errors)
    {
        if (value < 1)
        {
            errors.Add($"{name} must be at least 1 (was {value}).");
        }
    }
}
