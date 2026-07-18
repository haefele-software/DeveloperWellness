using DeveloperWellness.Domain.Options;
using Microsoft.Extensions.Options;

namespace DeveloperWellness.Web.Services;

/// <summary>
/// Fail-fast start-up validation for <see cref="WellnessOptions"/> (tasks.md T009). Delegates every rule
/// to <see cref="WellnessOptions.Validate"/> and aggregates every violation into one
/// <see cref="ValidateOptionsResult"/>, so a single start-up failure names every problem at once rather
/// than failing on the first one found.
/// </summary>
public sealed class WellnessOptionsValidation : IValidateOptions<WellnessOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, WellnessOptions options)
    {
        var errors = options.Validate();

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
