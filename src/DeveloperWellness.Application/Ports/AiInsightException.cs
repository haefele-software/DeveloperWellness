namespace DeveloperWellness.Application.Ports;

/// <summary>
/// Thrown by <see cref="IAiInsightService"/> implementations when a summary or tone-classification
/// request fails outright. <see cref="Exception.Message"/> is always user-presentable.
/// </summary>
public sealed class AiInsightException : Exception
{
    /// <summary>Creates the exception with a user-presentable message.</summary>
    public AiInsightException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a user-presentable message and the underlying cause.</summary>
    public AiInsightException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
