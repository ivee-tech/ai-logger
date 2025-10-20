using System.Collections.Generic;

namespace AiLogger.Core;

public sealed class SensitiveDataAnalysis
{
    public IReadOnlyList<SensitiveItem> Items { get; init; } = new List<SensitiveItem>();
    public string RawModelResponse { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Error { get; init; }
}

public sealed record SensitiveItem(string Type, string Value, int Start, int Length);
