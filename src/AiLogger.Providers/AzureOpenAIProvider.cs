using AiLogger.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiLogger.Providers
{
    public class AzureOpenAIProvider : AIProviderBase
    {
        private readonly string? _endpoint;
        private readonly string? _apiKey;
        private readonly string _deployment;
        private static readonly HttpClient _http = new();

        public AzureOpenAIProvider(ILogger<AzureOpenAIProvider> logger, IConfiguration configuration) : base(logger)
        {
            var section = configuration.GetSection("AiProviders:AzureOpenAI");

            var configuredEndpoint = Normalize(section.GetValue<string>("Endpoint"));
            var configuredKey = Normalize(section.GetValue<string>("ApiKey"));
            var configuredDeployment = Normalize(section.GetValue<string>("Deployment"));
            var configuredTimeoutSeconds = section.GetValue<int?>("TimeoutSeconds");
            var envTimeout = Normalize(Environment.GetEnvironmentVariable("AZURE_OPENAI_TIMEOUT_SECONDS"));

            _endpoint = configuredEndpoint ?? Normalize(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"));
            _apiKey = configuredKey ?? Normalize(Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")) ?? Normalize(Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY"));
            _deployment = configuredDeployment
                          ?? Normalize(Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT"))
                          ?? "gpt-4o-mini";

            if (configuredTimeoutSeconds is null && int.TryParse(envTimeout, NumberStyles.Integer, CultureInfo.InvariantCulture, out var envTimeoutSeconds))
            {
                configuredTimeoutSeconds = envTimeoutSeconds;
            }

            if (configuredTimeoutSeconds is { } timeoutSeconds)
            {
                if (timeoutSeconds <= 0)
                {
                    _http.Timeout = Timeout.InfiniteTimeSpan;
                    _logger.LogInformation("AzureOpenAI HttpClient timeout disabled (infinite) based on configuration.");
                }
                else
                {
                    var clamped = Math.Min(timeoutSeconds, 1800);
                    _http.Timeout = TimeSpan.FromSeconds(clamped);
                    _logger.LogInformation("AzureOpenAI HttpClient timeout configured to {Seconds} seconds.", clamped);
                }
            }
            else if (_http.Timeout == TimeSpan.FromSeconds(100))
            {
                _http.Timeout = TimeSpan.FromMinutes(10);
                _logger.LogInformation("AzureOpenAI HttpClient timeout increased to {Minutes} minutes by default.", 10);
            }

            if (string.IsNullOrWhiteSpace(_endpoint))
            {
                _logger.LogWarning("AzureOpenAI endpoint is not configured. Set AiProviders:AzureOpenAI:Endpoint or AZURE_OPENAI_ENDPOINT.");
            }
            else
            {
                _logger.LogInformation("AzureOpenAI endpoint resolved to {Endpoint} with deployment {Deployment}.", _endpoint, _deployment);
            }

            var apiKeyConfigured = !string.IsNullOrWhiteSpace(_apiKey);
            if (!apiKeyConfigured)
            {
                _logger.LogWarning("AzureOpenAI API key is not configured. Set AiProviders:AzureOpenAI:ApiKey or AZURE_OPENAI_API_KEY.");
            }
            else
            {
                _logger.LogDebug("AzureOpenAI API key configuration detected (value masked).");
            }
        }

        private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        public override string ProviderName => "AzureOpenAI";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint);

        private bool CanCall => IsConfigured && !string.IsNullOrWhiteSpace(_apiKey);

    public override async Task<string> AnalyzeTextAsync(string text, SensitiveDataOptions options, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("Azure OpenAI not configured (missing AZURE_OPENAI_ENDPOINT). Returning empty analysis.");
                return JsonSerializer.Serialize(new { provider = ProviderName, configured = false, detectedItems = Array.Empty<object>() });
            }

            if (!CanCall)
            {
                return JsonSerializer.Serialize(new { provider = ProviderName, error = "Missing AZURE_OPENAI_KEY" });
            }

            var systemPrompt = ProviderSystemPrompts.Analysis;

            try
            {
                var apiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-08-01-preview";
                var url = $"{_endpoint!.TrimEnd('/')}/openai/deployments/{_deployment}/chat/completions?api-version={apiVersion}";
                var payload = new
                {
                    messages = new object[] {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = text }
                    },
                    temperature = 0.0,
                    response_format = new { type = "json_object" }
                };
                var payloadJson = JsonSerializer.Serialize(payload);
                using var resp = await PostWithRetryAsync(url, payloadJson, cancellationToken).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Azure OpenAI analyze call failed: {Status} {Body}", resp.StatusCode, body);
                    return JsonSerializer.Serialize(new { provider = ProviderName, error = resp.StatusCode.ToString() });
                }
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var content = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content").GetString();
                    return content ?? "{\"items\":[],\"model\":\"unknown\"}";
                }
                catch (Exception)
                {
                    return body; // raw fallback
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure OpenAI analyze call failed.");
                return JsonSerializer.Serialize(new { provider = ProviderName, error = ex.Message });
            }
        }

        public override async Task<SanitizationResult> SanitizeAsync(string text, SanitizationOptions options, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return new SanitizationResult
                {
                    OriginalText = text,
                    SanitizedText = text,
                    Success = false,
                    Error = "Provider not configured"
                };
            }

            if (!CanCall)
            {
                return new SanitizationResult { OriginalText = text, SanitizedText = text, Success = false, Error = "Missing configuration (endpoint/api key)." };
            }

            var chunks = SplitIntoChunks(text, MaxTokensPerChunk);
            if (chunks.Count == 0)
            {
                return new SanitizationResult { OriginalText = text, SanitizedText = text, Success = true };
            }

            if (chunks.Count == 1)
            {
                return await SanitizeChunkAsync(text, cancellationToken, chunkIndex: 1, totalChunks: 1).ConfigureAwait(false);
            }

            _logger.LogInformation("Sanitizing large log in {ChunkCount} chunks.", chunks.Count);

            var sanitizedBuilder = new StringBuilder(text.Length);
            var combinedMappings = new List<MappingEntry>();

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var chunkResult = await SanitizeChunkAsync(chunk, cancellationToken, i + 1, chunks.Count).ConfigureAwait(false);
                if (!chunkResult.Success)
                {
                    _logger.LogWarning("Sanitization failed for chunk {Chunk}/{Total}: {Error}", i + 1, chunks.Count, chunkResult.Error ?? "Unknown");
                    return new SanitizationResult
                    {
                        OriginalText = text,
                        SanitizedText = text,
                        Success = false,
                        Error = $"Chunk {i + 1}/{chunks.Count} failed: {chunkResult.Error ?? "Unknown error"}"
                    };
                }

                sanitizedBuilder.Append(chunkResult.SanitizedText);
                if (chunkResult.Mappings.Count > 0)
                {
                    combinedMappings.AddRange(chunkResult.Mappings);
                }
            }

            return new SanitizationResult
            {
                OriginalText = text,
                SanitizedText = sanitizedBuilder.ToString(),
                Mappings = combinedMappings,
                Success = true
            };
        }

    private async Task<SanitizationResult> SanitizeChunkAsync(string chunk, CancellationToken cancellationToken, int chunkIndex, int totalChunks)
        {
            var systemPrompt = ProviderSystemPrompts.Sanitization;

            try
            {
                var apiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-08-01-preview";
                var url = $"{_endpoint!.TrimEnd('/')}/openai/deployments/{_deployment}/chat/completions?api-version={apiVersion}";
                var payload = new
                {
                    messages = new object[] {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = chunk }
                    },
                    temperature = 0.1,
                    response_format = new { type = "json_object" }
                };
                var payloadJson = JsonSerializer.Serialize(payload);

                _logger.LogInformation(
                    "Calling Azure OpenAI for chunk {Chunk}/{Total} (~{Tokens} tokens).",
                    chunkIndex,
                    totalChunks,
                    EstimateTokenCount(chunk));

                using var resp = await PostWithRetryAsync(url, payloadJson, cancellationToken).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Azure OpenAI sanitize chunk {Chunk}/{Total} failed: {Status} {Body}", chunkIndex, totalChunks, resp.StatusCode, body);
                    return new SanitizationResult { OriginalText = chunk, SanitizedText = chunk, Success = false, Error = resp.StatusCode.ToString() };
                }
                var content = body;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    content = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content").GetString() ?? body;
                }
                catch (Exception) { }

                string sanitizedText = chunk;
                List<MappingEntry> mappings = new();
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("sanitizedText", out var st))
                        {
                            sanitizedText = st.GetString() ?? sanitizedText;
                        }
                        if (doc.RootElement.TryGetProperty("mappings", out var mArr) && mArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in mArr.EnumerateArray())
                            {
                                var type = m.TryGetProperty("type", out var t) ? t.GetString() ?? "Unknown" : "Unknown";
                                var original = m.TryGetProperty("original", out var o) ? o.GetString() ?? string.Empty : string.Empty;
                                var replacement = m.TryGetProperty("replacement", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                                if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(replacement))
                                {
                                    mappings.Add(new MappingEntry(type, original, replacement));
                                }
                            }
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "Failed to parse sanitization JSON for chunk {Chunk}/{Total}; returning best-effort result.", chunkIndex, totalChunks);
                }

                return new SanitizationResult
                {
                    OriginalText = chunk,
                    SanitizedText = sanitizedText,
                    Mappings = mappings,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure OpenAI sanitize chunk {Chunk}/{Total} call failed.", chunkIndex, totalChunks);
                return new SanitizationResult { OriginalText = chunk, SanitizedText = chunk, Success = false, Error = ex.Message };
            }
        }

        private static List<string> SplitIntoChunks(string text, int maxTokens)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new List<string>();
            }

            var maxChars = Math.Max(maxTokens * 4, 4_000);
            var chunks = new List<string>();
            var position = 0;

            while (position < text.Length)
            {
                var remaining = text.Length - position;
                var length = Math.Min(maxChars, remaining);
                var end = position + length;

                if (end < text.Length)
                {
                    var boundary = FindChunkBoundary(text, position, end, length);
                    if (boundary > position)
                    {
                        end = boundary;
                    }
                }

                chunks.Add(text.Substring(position, end - position));
                position = end;
            }

            return chunks;
        }

        private static int FindChunkBoundary(string text, int start, int suggestedEnd, int searchLength)
        {
            var newlineBreak = text.LastIndexOf('\n', suggestedEnd - 1, searchLength);
            if (newlineBreak >= start)
            {
                return newlineBreak + 1;
            }

            var carriageBreak = text.LastIndexOf('\r', suggestedEnd - 1, searchLength);
            if (carriageBreak >= start)
            {
                return carriageBreak + 1;
            }

            var spaceBreak = text.LastIndexOf(' ', suggestedEnd - 1, searchLength);
            if (spaceBreak >= start + searchLength / 2)
            {
                return spaceBreak + 1;
            }

            return suggestedEnd;
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

        private async Task<HttpResponseMessage> PostWithRetryAsync(string url, string payloadJson, CancellationToken cancellationToken)
        {
            if (_apiKey is null)
            {
                throw new InvalidOperationException("Azure OpenAI API key is not configured.");
            }

            const int maxAttempts = 3;
            var delay = TimeSpan.FromSeconds(2);

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("api-key", _apiKey);

                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Azure OpenAI request attempt {Attempt}/{MaxAttempts} failed. Retrying after {Delay}s.", attempt, maxAttempts, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = CalculateNextDelay(delay);
                    continue;
                }

                if (ShouldRetry(response) && attempt < maxAttempts)
                {
                    var wait = GetRetryAfter(response) ?? delay;
                    _logger.LogWarning("Azure OpenAI request attempt {Attempt}/{MaxAttempts} returned {Status}. Retrying after {Delay}s.", attempt, maxAttempts, response.StatusCode, wait.TotalSeconds);
                    response.Dispose();
                    await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                    delay = CalculateNextDelay(wait);
                    continue;
                }

                return response;
            }

            throw new InvalidOperationException("Azure OpenAI request retry logic exhausted without a response.");
        }

        private static bool ShouldRetry(HttpResponseMessage response)
        {
            if (response is null)
            {
                return false;
            }

            return response.StatusCode == HttpStatusCode.TooManyRequests
                || response.StatusCode == HttpStatusCode.RequestTimeout
                || response.StatusCode == HttpStatusCode.InternalServerError
                || response.StatusCode == HttpStatusCode.BadGateway
                || response.StatusCode == HttpStatusCode.ServiceUnavailable
                || response.StatusCode == HttpStatusCode.GatewayTimeout;
        }

        private static TimeSpan CalculateNextDelay(TimeSpan current)
        {
            var nextSeconds = Math.Min(current.TotalSeconds * 2, 60);
            return TimeSpan.FromSeconds(nextSeconds < 1 ? 1 : nextSeconds);
        }

        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                var header = values.FirstOrDefault();
                if (header is not null)
                {
                    if (int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                    {
                        return TimeSpan.FromSeconds(seconds);
                    }

                    if (DateTimeOffset.TryParse(header, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var targetTime))
                    {
                        var delta = targetTime - DateTimeOffset.UtcNow;
                        if (delta > TimeSpan.Zero)
                        {
                            return delta;
                        }
                    }
                }
            }

            if (response.Headers.TryGetValues("azure-openai-quota-minutely-reset", out var resetValues))
            {
                var reset = resetValues.FirstOrDefault();
                if (double.TryParse(reset, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }

            return null;
        }

        private const int MaxTokensPerChunk = 60_000;
    }
}
