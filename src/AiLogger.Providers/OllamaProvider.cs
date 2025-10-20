using AiLogger.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiLogger.Providers;

/// <summary>
/// Local AI provider using an Ollama server (default http://localhost:11434).
/// Leverages the /api/chat endpoint with JSON formatting to perform analysis and sanitization.
/// </summary>
public sealed class OllamaProvider : AIProviderBase
{
    private static readonly HttpClient _http = new();
    private readonly string _endpoint;
    private readonly string _model;

    public OllamaProvider(ILogger<OllamaProvider> logger) : base(logger)
    {
        _endpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")?.TrimEnd('/') ?? "http://localhost:11434";
        _model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama3"; // choose a commonly available local model
    }

    public override string ProviderName => "Ollama";
    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint) && !string.IsNullOrWhiteSpace(_model);

    public override async Task<string> AnalyzeTextAsync(string text, SensitiveDataOptions options, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return JsonSerializer.Serialize(new { provider = ProviderName, configured = false, items = Array.Empty<object>() });
        }

        // System prompt instructing the model to ONLY output JSON
        var systemPrompt = """
You are a log privacy assistant. Extract potentially sensitive items from the user content. Output ONLY JSON:
{"items":[{"type":string,"value":string,"start":number,"length":number}],"model":string}
Types: Email, IpAddress, Hostname, ApiKey, Guid. If none, items=[].
""";

        try
        {
            var url = _endpoint + "/api/chat";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            var payload = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = text }
                },
                // Ollama specific: format for JSON (some models respect this for structured output)
                format = "json", // fallback; if unsupported, we'll try to parse anyway
                stream = false
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama analyze failed: {Status} {Body}", resp.StatusCode, body);
                return JsonSerializer.Serialize(new { provider = ProviderName, error = resp.StatusCode.ToString() });
            }
            try
            {
                using var doc = JsonDocument.Parse(body);
                var content = doc.RootElement.TryGetProperty("message", out var msg) &&
                              msg.TryGetProperty("content", out var cont)
                    ? cont.GetString()
                    : null;
                return content ?? body; // content should already be JSON
            }
            catch (Exception)
            {
                return body; // raw fallback
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ollama analyze call failed.");
            return JsonSerializer.Serialize(new { provider = ProviderName, error = ex.Message });
        }
    }

    public override async Task<SanitizationResult> SanitizeAsync(string text, SanitizationOptions options, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new SanitizationResult { OriginalText = text, SanitizedText = text, Success = false, Error = "Provider not configured" };
        }

        var systemPrompt = """
You sanitize log text. Output ONLY JSON:
{"sanitizedText":string,"mappings":[{"type":string,"original":string,"replacement":string}]}
Guidelines: Replace sensitive values (emails, ip addresses, hostnames, api keys, guids) with realistic mock equivalents. Preserve timestamps and log levels.
""";

        try
        {
            var url = _endpoint + "/api/chat";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            var payload = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = text }
                },
                format = "json",
                stream = false
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ollama sanitize failed: {Status} {Body}", resp.StatusCode, body);
                return new SanitizationResult { OriginalText = text, SanitizedText = text, Success = false, Error = resp.StatusCode.ToString() };
            }

            string content = body;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var cont))
                {
                    content = cont.GetString() ?? body;
                }
            }
            catch (Exception) { /* leave content */ }

            string sanitizedText = text;
            List<MappingEntry> mappings = new();
            try
            {
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("sanitizedText", out var st))
                        sanitizedText = st.GetString() ?? sanitizedText;
                    if (root.TryGetProperty("mappings", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in arr.EnumerateArray())
                        {
                            var type = m.TryGetProperty("type", out var t) ? t.GetString() ?? "Unknown" : "Unknown";
                            var original = m.TryGetProperty("original", out var o) ? o.GetString() ?? string.Empty : string.Empty;
                            var replacement = m.TryGetProperty("replacement", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                            if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(replacement))
                                mappings.Add(new MappingEntry(type, original, replacement));
                        }
                    }
                }
            }
            catch (Exception parseEx)
            {
                _logger.LogWarning(parseEx, "Failed to parse Ollama sanitization JSON.");
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
            _logger.LogError(ex, "Ollama sanitize call failed.");
            return new SanitizationResult { OriginalText = text, SanitizedText = text, Success = false, Error = ex.Message };
        }
    }
}
