using System;
using System.Collections.Generic;

namespace AiLogger.Api.Contracts;

public sealed record LogAnalysisResponseDto(
    string SanitizedContent,
    IReadOnlyList<SensitiveFindingDto> SensitiveFindings,
    AnalysisMetadataDto Metadata,
    IReadOnlyList<string> Warnings
);

public sealed record SensitiveFindingDto(
    string Category,
    string ReplacementKey,
    string? Sample,
    int Occurrences,
    double? Confidence
);

public sealed record AnalysisMetadataDto(
    string OriginalFileName,
    string Provider,
    DateTimeOffset ProcessedAt,
    double DurationMs,
    TokenUsageDto TokenUsage
);

public sealed record TokenUsageDto(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens
);
