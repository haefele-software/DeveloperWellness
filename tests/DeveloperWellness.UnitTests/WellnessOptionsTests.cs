using DeveloperWellness.Domain.Options;

namespace DeveloperWellness.UnitTests;

public class WellnessOptionsTests
{
    private static WellnessOptions CreateValid() => new()
    {
        OrganisationTimeZone = "UTC",
    };

    [Fact]
    public void Validate_WithDefaultsAndAValidTimeZone_ProducesNoErrors()
    {
        var options = CreateValid();

        var errors = options.Validate();

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Validate_WithOutOfHoursThresholdOutsideZeroToOneInclusive_ProducesNamedError(decimal value)
    {
        var options = CreateValid();
        options.OutOfHoursThreshold = value;

        var errors = options.Validate();

        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.OutOfHoursThreshold), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.1)]
    public void Validate_WithNegativeToneThresholdOutsideZeroToOneInclusive_ProducesNamedError(decimal value)
    {
        var options = CreateValid();
        options.NegativeToneThreshold = value;

        var errors = options.Validate();

        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.NegativeToneThreshold), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.1)]
    public void Validate_WithChangesRequestedThresholdOutsideZeroToOneInclusive_ProducesNamedError(decimal value)
    {
        var options = CreateValid();
        options.ChangesRequestedThreshold = value;

        var errors = options.Validate();

        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.ChangesRequestedThreshold), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithThresholdExactlyOne_IsAllowedBecauseRangeIsInclusiveOfOne()
    {
        var options = CreateValid();
        options.OutOfHoursThreshold = 1.0m;

        var errors = options.Validate();

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(nameof(WellnessOptions.MinPrEvents))]
    [InlineData(nameof(WellnessOptions.SpreadThinThreshold))]
    [InlineData(nameof(WellnessOptions.MinAnalysedComments))]
    [InlineData(nameof(WellnessOptions.MinPrSample))]
    [InlineData(nameof(WellnessOptions.RepoCap))]
    [InlineData(nameof(WellnessOptions.BranchCap))]
    [InlineData(nameof(WellnessOptions.ToneCommentCap))]
    [InlineData(nameof(WellnessOptions.TrendWeeks))]
    public void Validate_WithCapOrGuardBelowOne_ProducesNamedError(string propertyName)
    {
        var options = CreateValid();
        SetIntProperty(options, propertyName, 0);

        var errors = options.Validate();

        Assert.Contains(errors, e => e.StartsWith(propertyName, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithWorkingHoursStartNotBeforeEnd_ProducesNamedError()
    {
        var options = CreateValid();
        options.WorkingHoursStart = new TimeOnly(18, 0);
        options.WorkingHoursEnd = new TimeOnly(9, 0);

        var errors = options.Validate();

        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.WorkingHoursStart), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithWorkingHoursStartEqualToEnd_ProducesNamedError()
    {
        var options = CreateValid();
        var sameTime = new TimeOnly(12, 0);
        options.WorkingHoursStart = sameTime;
        options.WorkingHoursEnd = sameTime;

        var errors = options.Validate();

        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.WorkingHoursStart), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(0)]
    public void Validate_WithPeriodDaysDefaultNotSevenFourteenOrThirty_ProducesNamedError(int days)
    {
        var options = CreateValid();
        options.PeriodDaysDefault = days;

        var errors = options.Validate();

        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.PeriodDaysDefault), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    [InlineData(30)]
    public void Validate_WithPeriodDaysDefaultSevenFourteenOrThirty_ProducesNoPeriodError(int days)
    {
        var options = CreateValid();
        options.PeriodDaysDefault = days;

        var errors = options.Validate();

        Assert.DoesNotContain(errors, e => e.StartsWith(nameof(WellnessOptions.PeriodDaysDefault), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithEmptyOrganisationTimeZone_ProducesNamedError()
    {
        var options = CreateValid();
        options.OrganisationTimeZone = string.Empty;

        var errors = options.Validate();

        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.OrganisationTimeZone), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithUnresolvableOrganisationTimeZone_ProducesErrorNamingTheBadId()
    {
        var options = CreateValid();
        options.OrganisationTimeZone = "Not/A_Real_Zone";

        var errors = options.Validate();

        Assert.Contains(errors, e => e.Contains("Not/A_Real_Zone", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("America/New_York")]
    public void Validate_WithResolvableIanaOrWindowsTimeZoneId_ProducesNoTimeZoneError(string timeZoneId)
    {
        var options = CreateValid();
        options.OrganisationTimeZone = timeZoneId;

        var errors = options.Validate();

        Assert.DoesNotContain(errors, e => e.StartsWith(nameof(WellnessOptions.OrganisationTimeZone), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithMultipleViolations_ReturnsAllOfThemRatherThanFailingFast()
    {
        var options = CreateValid();
        options.OutOfHoursThreshold = 0m;
        options.RepoCap = 0;
        options.OrganisationTimeZone = string.Empty;

        var errors = options.Validate();

        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.OutOfHoursThreshold), StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.RepoCap), StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith(nameof(WellnessOptions.OrganisationTimeZone), StringComparison.Ordinal));
    }

    private static void SetIntProperty(WellnessOptions options, string propertyName, int value)
    {
        switch (propertyName)
        {
            case nameof(WellnessOptions.MinPrEvents):
                options.MinPrEvents = value;
                break;
            case nameof(WellnessOptions.SpreadThinThreshold):
                options.SpreadThinThreshold = value;
                break;
            case nameof(WellnessOptions.MinAnalysedComments):
                options.MinAnalysedComments = value;
                break;
            case nameof(WellnessOptions.MinPrSample):
                options.MinPrSample = value;
                break;
            case nameof(WellnessOptions.RepoCap):
                options.RepoCap = value;
                break;
            case nameof(WellnessOptions.BranchCap):
                options.BranchCap = value;
                break;
            case nameof(WellnessOptions.ToneCommentCap):
                options.ToneCommentCap = value;
                break;
            case nameof(WellnessOptions.TrendWeeks):
                options.TrendWeeks = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "Unknown property.");
        }
    }
}
