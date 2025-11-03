using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AiLogger.Core;

public interface ILogSanitizationPipeline
{
    Task<SanitizationPipelineResult> SanitizeAsync(
        string content,
        string? preferredProvider,
        SensitiveDataOptions detectionOptions,
        SanitizationOptions sanitizationOptions,
        CancellationToken cancellationToken = default);
}

public sealed record SanitizationPipelineResult(
    string OriginalText,
    string PrefilteredText,
    string SanitizedText,
    IReadOnlyList<MappingEntry> Mappings,
    string ProviderName,
    string? AiProviderName,
    bool UsedAiSuccessfully,
    string? AiError,
    string? AnalysisJson,
    int LocalReplacementCount);

public sealed class LogSanitizationPipeline : ILogSanitizationPipeline
{
    private readonly ILocalSensitiveDataDetector _detector;
    private readonly IAIProviderFactory _providerFactory;
    private readonly ILogger<LogSanitizationPipeline> _logger;

    public LogSanitizationPipeline(
        ILocalSensitiveDataDetector detector,
        IAIProviderFactory providerFactory,
        ILogger<LogSanitizationPipeline> logger)
    {
        _detector = detector;
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public async Task<SanitizationPipelineResult> SanitizeAsync(
        string content,
        string? preferredProvider,
        SensitiveDataOptions detectionOptions,
        SanitizationOptions sanitizationOptions,
        CancellationToken cancellationToken = default)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }
        detectionOptions ??= new SensitiveDataOptions();
        sanitizationOptions ??= new SanitizationOptions();

        var localResult = _detector.DetectAndReplace(content, detectionOptions);
        _logger.LogInformation("Local detector replaced {Count} items prior to AI processing.", localResult.Mappings.Count);

        var estimatedTokens = EstimateTokenCount(localResult.PrefilteredText);
        if (estimatedTokens > MaxModelTokens)
        {
            _logger.LogWarning(
                "Prefiltered content estimated at {Tokens} tokens exceeds the single-request limit. Provider will process the log in chunks; consider splitting very large files for faster processing.",
                estimatedTokens);
        }

        IAIProvider aiProvider;
        try
        {
            aiProvider = _providerFactory.GetProvider(preferredProvider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve AI provider. Falling back to local-only result.");
            return CreateFallbackResult(content, localResult, "LocalDetector", aiError: ex.Message, analysisJson: null);
        }

        _logger.LogInformation("Selected AI provider {Provider} for sanitization.", aiProvider.ProviderName);

        string? analysisJson = null;
        try
        {
            analysisJson = await aiProvider.AnalyzeTextAsync(localResult.PrefilteredText, detectionOptions, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("AI analysis output length: {Length}", analysisJson?.Length ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI analysis failed for provider {Provider}. Continuing with sanitization.", aiProvider.ProviderName);
        }

        SanitizationResult aiResult;
        try
        {
            aiResult = await aiProvider.SanitizeAsync(localResult.PrefilteredText, sanitizationOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI sanitization call threw for provider {Provider}.", aiProvider.ProviderName);
            return CreateFallbackResult(content, localResult, aiProvider.ProviderName, aiError: ex.Message, analysisJson: analysisJson);
        }

        if (!aiResult.Success)
        {
            _logger.LogWarning("AI sanitization failed; falling back to local-only sanitization. Provider={Provider}, Error={Error}", aiProvider.ProviderName, aiResult.Error ?? "Unknown");
            return CreateFallbackResult(content, localResult, aiProvider.ProviderName, aiError: aiResult.Error, analysisJson: analysisJson);
        }

        _logger.LogInformation(
            "AI provider {Provider} produced sanitized output with {Count} proposed mappings.",
            aiProvider.ProviderName,
            aiResult.Mappings.Count);

        if (aiResult.Mappings.Count == 0)
        {
            _logger.LogInformation("AI provider {Provider} returned no new mappings (sanitized text length {Length}).", aiProvider.ProviderName, aiResult.SanitizedText.Length);
        }
        else
        {
            foreach (var mapping in aiResult.Mappings.Take(5))
            {
                _logger.LogDebug(
                    "AI mapping candidate: Type={Type}, Replacement={Replacement}, OriginalLength={OriginalLength}",
                    mapping.Type,
                    mapping.Replacement,
                    mapping.Original?.Length ?? 0);
            }
            if (aiResult.Mappings.Count > 5)
            {
                _logger.LogDebug("Additional AI mappings truncated from log output ({Remaining} more).", aiResult.Mappings.Count - 5);
            }
        }

        var sanitizedText = aiResult.SanitizedText;
        var filteredMappings = new List<MappingEntry>();
        foreach (var mapping in aiResult.Mappings)
        {
            if (IsFalsePositiveHostname(mapping))
            {
                if (!string.IsNullOrEmpty(mapping.Replacement) && sanitizedText.Contains(mapping.Replacement, StringComparison.Ordinal))
                {
                    sanitizedText = sanitizedText.Replace(mapping.Replacement, mapping.Original, StringComparison.Ordinal);
                }
                continue;
            }
            filteredMappings.Add(mapping);
        }

        var merged = new List<MappingEntry>(localResult.Mappings);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in localResult.Mappings)
        {
            seen.Add(m.Original);
        }
        foreach (var m in filteredMappings)
        {
            if (!seen.Contains(m.Original))
            {
                merged.Add(m);
                seen.Add(m.Original);
            }
        }

        var aiUniqueCount = merged.Count - localResult.Mappings.Count;
        _logger.LogInformation(
            "Merged mapping set contains {MergedCount} entries ({LocalCount} local, {AiUniqueCount} from AI).",
            merged.Count,
            localResult.Mappings.Count,
            Math.Max(aiUniqueCount, 0));

        return new SanitizationPipelineResult(
            OriginalText: content,
            PrefilteredText: localResult.PrefilteredText,
            SanitizedText: sanitizedText,
            Mappings: merged,
            ProviderName: aiProvider.ProviderName,
            AiProviderName: aiProvider.ProviderName,
            UsedAiSuccessfully: true,
            AiError: null,
            AnalysisJson: analysisJson,
            LocalReplacementCount: localResult.Mappings.Count);
    }

    private SanitizationPipelineResult CreateFallbackResult(
        string originalText,
        LocalDetectionResult localResult,
        string? attemptedProvider,
        string? aiError,
        string? analysisJson)
    {
        return new SanitizationPipelineResult(
            OriginalText: originalText,
            PrefilteredText: localResult.PrefilteredText,
            SanitizedText: localResult.PrefilteredText,
            Mappings: localResult.Mappings,
            ProviderName: "LocalDetector",
            AiProviderName: attemptedProvider,
            UsedAiSuccessfully: false,
            AiError: aiError,
            AnalysisJson: analysisJson,
            LocalReplacementCount: localResult.Mappings.Count);
    }

    private static bool IsFalsePositiveHostname(MappingEntry mapping)
    {
        if (!string.Equals(mapping.Type, "Hostname", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = mapping.Original?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.Contains(' '))
        {
            return true;
        }

        if (value.Contains(':'))
        {
            var segments = value.Split(':');
            if (segments.Length >= 2 && segments.All(s => s.All(char.IsDigit)))
            {
                return true;
            }
        }

        return false;
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var estimated = (int)Math.Ceiling(text.Length / 4.0);
        return Math.Max(estimated, 1);
    }

    private const int MaxModelTokens = 128_000;
}
