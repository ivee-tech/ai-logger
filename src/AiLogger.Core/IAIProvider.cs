using System.Threading;
using System.Threading.Tasks;

namespace AiLogger.Core;

public sealed class SensitiveDataOptions
{
    public bool DetectEmails { get; init; } = true;
    public bool DetectIpAddresses { get; init; } = true;
    public bool DetectHostnames { get; init; } = true;
    public bool DetectApiKeys { get; init; } = true;
    public bool DetectGuids { get; init; } = true;
}

public sealed class SanitizationResult
{
    public string OriginalText { get; init; } = string.Empty;
    public string SanitizedText { get; init; } = string.Empty;
    public IReadOnlyList<MappingEntry> Mappings { get; init; } = new List<MappingEntry>();
    public bool Success { get; init; }
    public string? Error { get; init; }
}

public sealed record MappingEntry(string Type, string Original, string Replacement);

public interface IAIProvider
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    Task<string> AnalyzeTextAsync(string text, SensitiveDataOptions options, CancellationToken cancellationToken = default);
    Task<SanitizationResult> SanitizeAsync(string text, SanitizationOptions options, CancellationToken cancellationToken = default);
}
