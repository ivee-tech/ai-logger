using AiLogger.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;

namespace AiLogger.Providers
{
    public class AzureOpenAIProvider : AIProviderBase
    {
        private readonly string? _endpoint;
    private readonly string? _apiKey;
    private readonly string _deployment;
    private static readonly HttpClient _http = new HttpClient();

        public AzureOpenAIProvider(ILogger<AzureOpenAIProvider> logger) : base(logger)
        {
            _endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            _apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            _deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini"; // sensible default
        }

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

                        var systemPrompt = """
You extract potentially sensitive data from raw log text. Return ONLY JSON:
{
    "items": [ { "type": string, "value": string, "start": number, "length": number } ],
    "model": string
}
Types to detect: Email, IpAddress, Hostname, ApiKey, Guid. If none, items = [].
""";

            try
            {
                var apiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-08-01-preview";
                var url = $"{_endpoint!.TrimEnd('/')}/openai/deployments/{_deployment}/chat/completions?api-version={apiVersion}";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("api-key", _apiKey);
                var payload = new
                {
                    messages = new object[] {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = text }
                    },
                    temperature = 0.0,
                    response_format = new { type = "json_object" }
                };
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
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

                        var systemPrompt = """
You sanitize log text. Output ONLY JSON:
{
    "sanitizedText": string,
    "mappings": [ { "type": string, "original": string, "replacement": string } ]
}
Guidelines: Replace sensitive values (emails, ip addresses, hostnames, api keys, guids) with realistic mock equivalents. Preserve timestamps and log levels.
""";

            try
            {
                var apiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-08-01-preview";
                var url = $"{_endpoint!.TrimEnd('/')}/openai/deployments/{_deployment}/chat/completions?api-version={apiVersion}";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("api-key", _apiKey);
                var payload = new
                {
                    messages = new object[] {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = text }
                    },
                    temperature = 0.1,
                    response_format = new { type = "json_object" }
                };
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Azure OpenAI sanitize call failed: {Status} {Body}", resp.StatusCode, body);
                    return new SanitizationResult { OriginalText = text, SanitizedText = text, Success = false, Error = resp.StatusCode.ToString() };
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

                string sanitizedText = text;
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
                    _logger.LogWarning(parseEx, "Failed to parse sanitization JSON; returning best-effort result.");
                }

                return new SanitizationResult
                {
                    OriginalText = text,
                    SanitizedText = sanitizedText,
                    Mappings = mappings,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure OpenAI sanitize call failed.");
                return new SanitizationResult { OriginalText = text, SanitizedText = text, Success = false, Error = ex.Message };
            }
        }
    }
}
