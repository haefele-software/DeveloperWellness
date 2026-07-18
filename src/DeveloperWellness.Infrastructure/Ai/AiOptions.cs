namespace DeveloperWellness.Infrastructure.Ai;

/// <summary>
/// Foundry model deployment connection configuration (data-model.md AiOptions, research R3). Bound from
/// the "Ai" configuration section inside <see cref="DependencyInjection.AddInfrastructure"/>. Every
/// property is optional, and unlike <c>WellnessOptions</c> none is validated at application start-up:
/// absence of any one of them simply means <see cref="FoundryAiInsightService.IsAvailable"/> is false, and
/// every UI surface degrades to the friendly unavailable state (FR-014). Authentication is API-key only —
/// this application never performs interactive or Entra ID sign-in (research R3).
/// </summary>
public sealed record AiOptions
{
    /// <summary>The configuration section name this record binds from.</summary>
    public const string SectionName = "Ai";

    /// <summary>The Foundry model deployment endpoint URI. Optional; absence contributes to <c>IsAvailable = false</c>.</summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// The API key used to authenticate against the Foundry model deployment. Secret: keep this in
    /// user-secrets during development, or another external secret store in production, and never in
    /// <c>appsettings.json</c> or any file committed to source control. Optional; absence contributes to
    /// <c>IsAvailable = false</c>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The Foundry model deployment name. Optional; absence contributes to <c>IsAvailable = false</c>.</summary>
    public string? DeploymentName { get; set; }
}
