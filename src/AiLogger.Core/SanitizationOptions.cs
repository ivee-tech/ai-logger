namespace AiLogger.Core;

public sealed class SanitizationOptions
{
    public bool PreserveTimestamps { get; init; } = true;
    public bool PreserveLogLevel { get; init; } = true;
    public bool MaskEmails { get; init; } = true;
    public bool MaskIpAddresses { get; init; } = true;
    public bool MaskHostnames { get; init; } = true;
    public bool MaskApiKeys { get; init; } = true;
}
