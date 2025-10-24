using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly Regex SshPublicKeyRegex = new("(?:ssh-(?:rsa|ed25519|dss)|ecdsa-sha2-nistp(?:256|384|521)) [A-Za-z0-9+/=]{20,}(?: \\S+)?", RegexOptions.Compiled);
    private static readonly Regex SshFingerprintRegex = new("SHA256:[A-Za-z0-9+/]{43}", RegexOptions.Compiled);

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
    var replacementMap = new Dictionary<string, string>(StringComparer.Ordinal);

    int emailCounter = 1, ipCounter = 1, hostCounter = 1, apiCounter = 1, guidCounter = 1, sshKeyCounter = 1, sshFingerprintCounter = 1;

        static string ComposeKey(string type, string original) => string.Concat(type, "|", original);

        void Collect(Regex rx, string type, Func<int> nextIndex, Func<int, string> replacementFactory)
        {
            foreach (Match m in rx.Matches(text))
            {
                if (!m.Success) continue;
                var original = m.Value;
                // Light validation for IP to reduce false positives
                if (type == "IpAddress" && !IsValidIPv4(original)) continue;
                if (type == "Hostname" && !IsLikelyHostname(original)) continue;
                var key = ComposeKey(type, original);
                if (!replacementMap.TryGetValue(key, out var replacement))
                {
                    var idx = nextIndex();
                    replacement = replacementFactory(idx);
                    replacementMap[key] = replacement;
                }
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
        if (options.DetectSshKeys)
        {
            Collect(SshPublicKeyRegex, "SshKey", () => sshKeyCounter++, CreateMockSshPublicKey);
            Collect(SshFingerprintRegex, "SshFingerprint", () => sshFingerprintCounter++, CreateMockSshFingerprint);
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
        var seenMappingKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = final.Count - 1; i >= 0; i--)
        {
            var r = final[i];
            sb.Remove(r.Start, r.Length);
            sb.Insert(r.Start, r.Replacement);
            var key = ComposeKey(r.Type, r.Original);
            if (seenMappingKeys.Add(key))
            {
                mappings.Add(new MappingEntry($"Local.{r.Type}", r.Original, r.Replacement));
            }
        }

        mappings.Reverse(); // restore original detection order

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

    private static bool IsLikelyHostname(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Contains(' ') || value.Contains(':')) return false;
        if (!value.Contains('.')) return false;
        if (!value.Any(char.IsLetter)) return false;

        var labels = value.Split('.');
        if (labels.Length < 2) return false;

        foreach (var label in labels)
        {
            if (string.IsNullOrEmpty(label) || label.Length > 63) return false;
            if (!char.IsLetterOrDigit(label[0]) || !char.IsLetterOrDigit(label[^1])) return false;
            if (label.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '-'))) return false;
            if (label.All(char.IsDigit)) return false; // avoid pure numeric segments
        }

        return true;
    }

    private static string CreateMockSshPublicKey(int index)
    {
        var bytes = new byte[32];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((index * 37 + i * 17) % 255);
            if (bytes[i] == 0) bytes[i] = 1;
        }
        var base64 = Convert.ToBase64String(bytes);
        return $"ssh-ed25519 {base64} user{index}@example.local";
    }

    private static string CreateMockSshFingerprint(int index)
    {
        var bytes = new byte[32];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(((index + 5) * 29 + i * 13) % 253);
            if (bytes[i] == 0) bytes[i] = 2;
        }
        var base64 = Convert.ToBase64String(bytes).TrimEnd('=');
        if (base64.Length < 43)
        {
            base64 = base64.PadRight(43, 'A');
        }
        else if (base64.Length > 43)
        {
            base64 = base64.Substring(0, 43);
        }
        return $"SHA256:{base64}";
    }
}
