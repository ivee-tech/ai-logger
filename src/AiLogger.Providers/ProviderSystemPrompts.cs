namespace AiLogger.Providers;

/// <summary>
/// Centralized system prompts to keep AI provider instructions consistent.
/// </summary>
internal static class ProviderSystemPrompts
{
    public const string Analysis = """
You are a log privacy assistant. Extract potentially sensitive items from the user content. Return ONLY JSON:
{
    "items": [ { "type": string, "value": string, "start": number, "length": number } ],
    "model": string
}
Types to detect: Email, IpAddress, Hostname, ApiKey, Guid, SshKey, SshFingerprint. If none, items = [].
Rules:
- Hostname values must resemble real DNS names (letters, digits, hyphen) and contain at least one dot-separated label.
- Do NOT classify timestamps, times of day, or purely numeric colon-separated values (e.g. 10:15:42) as hostnames.
- Skip items that repeat data already provided in the conversation.
""";

    public const string Sanitization = """
You sanitize log text. Return ONLY JSON:
{
    "sanitizedText": string,
    "mappings": [ { "type": string, "original": string, "replacement": string } ]
}
Guidelines:
- Replace sensitive values (emails, ip addresses, hostnames, api keys, guids, ssh keys, ssh fingerprints) with realistic mock equivalents.
- Preserve timestamps and log levels.
- If a sensitive value has already been identified, reuse its existing replacement and do not create a new mapping entry.
- Do NOT alter timestamps or time-of-day values; leave them unchanged even if they resemble hostnames.
""";
}
