using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AiLogger.Core;

/// <summary>
/// Result of the local (regex-based) sensitive data pre-filter.
/// </summary>
public sealed class LocalDetectionResult
{
    public string OriginalText { get; init; } = string.Empty;
    public string PrefilteredText { get; init; } = string.Empty; // Text with local replacements applied
    public IReadOnlyList<MappingEntry> Mappings { get; init; } = new List<MappingEntry>();
}

/// <summary>
/// Contract for detecting & replacing sensitive tokens locally before sending to an AI model.
/// </summary>
public interface ILocalSensitiveDataDetector
{
    LocalDetectionResult DetectAndReplace(string text, SensitiveDataOptions options);
}

/// <summary>
/// Regex based implementation. Keeps logic lightweight & deterministic.
/// </summary>
public sealed class RegexLocalSensitiveDataDetector : ILocalSensitiveDataDetector
{
    // Pre-compiled regex patterns (case-insensitive where appropriate)
    private static readonly Regex EmailRegex = new("[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex GuidRegex = new("[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}", RegexOptions.Compiled);
    private static readonly Regex Ipv4Regex = new("\\b(?:[0-9]{1,3}\\.){3}[0-9]{1,3}\\b", RegexOptions.Compiled);
    private static readonly Regex HostnameRegex = new("[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\\.[a-zA-Z0-9-]{1,63})+", RegexOptions.Compiled);
    // Heuristic API key / token: long mixed-case base64ish strings length>=24
    private static readonly Regex ApiKeyRegex = new("[A-Za-z0-9-_]{24,64}", RegexOptions.Compiled);

    public LocalDetectionResult DetectAndReplace(string text, SensitiveDataOptions options)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new LocalDetectionResult { OriginalText = text, PrefilteredText = text };
        }

        var mappings = new List<MappingEntry>();
        var sb = new System.Text.StringBuilder(text);

        // We'll collect matches, then replace from end to start to keep indices valid.
        var replacements = new List<(int Start, int Length, string Type, string Original, string Replacement)>();

        int emailCounter = 1, ipCounter = 1, hostCounter = 1, apiCounter = 1, guidCounter = 1;

        void Collect(Regex rx, string type, Func<int> nextIndex, Func<int, string> replacementFactory)
        {
            foreach (Match m in rx.Matches(text))
            {
                if (!m.Success) continue;
                var original = m.Value;
                // Light validation for IP to reduce false positives
                if (type == "IpAddress" && !IsValidIPv4(original)) continue;
                var idx = nextIndex();
                var replacement = replacementFactory(idx);
                replacements.Add((m.Index, m.Length, type, original, replacement));
            }
        }

        if (options.DetectEmails)
        {
            Collect(EmailRegex, "Email", () => emailCounter++, i => $"user{i}@example.com");
        }
        if (options.DetectIpAddresses)
        {
            Collect(Ipv4Regex, "IpAddress", () => ipCounter++, i => $"10.0.0.{i}");
        }
        if (options.DetectHostnames)
        {
            Collect(HostnameRegex, "Hostname", () => hostCounter++, i => $"host{i}.example.local");
        }
        if (options.DetectApiKeys)
        {
            Collect(ApiKeyRegex, "ApiKey", () => apiCounter++, i => $"APIKEY_REDACTED_{i}");
        }
        if (options.DetectGuids)
        {
            Collect(GuidRegex, "Guid", () => guidCounter++, i => $"00000000-0000-0000-0000-{i.ToString().PadLeft(12,'0')}");
        }

        // De-duplicate overlapping (pick earliest longest). Sort by start, then length desc.
        replacements.Sort((a,b) => a.Start == b.Start ? b.Length.CompareTo(a.Length) : a.Start.CompareTo(b.Start));
        var final = new List<(int Start, int Length, string Type, string Original, string Replacement)>();
        int currentEnd = -1;
        foreach (var r in replacements)
        {
            if (r.Start < currentEnd) continue; // overlap
            final.Add(r);
            currentEnd = r.Start + r.Length;
        }

        // Apply from end
        for (int i = final.Count - 1; i >= 0; i--)
        {
            var r = final[i];
            sb.Remove(r.Start, r.Length);
            sb.Insert(r.Start, r.Replacement);
            mappings.Add(new MappingEntry($"Local.{r.Type}", r.Original, r.Replacement));
        }

        return new LocalDetectionResult
        {
            OriginalText = text,
            PrefilteredText = sb.ToString(),
            Mappings = mappings
        };
    }

    private static bool IsValidIPv4(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4) return false;
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out int val) || val < 0 || val > 255) return false;
        }
        return true;
    }
}
