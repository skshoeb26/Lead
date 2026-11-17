using LeadPilot.Application.Common;

namespace LeadPilot.Application.Outreach;

/// <summary>
/// Hash binding an approval to the exact recipient, subject and body. Line endings and trailing whitespace are
/// normalised so the hash is stable across platforms, but any visible change produces a new hash.
/// </summary>
public static class EmailContentHasher
{
    public static string Compute(string toAddress, string subject, string body)
    {
        var to = (toAddress ?? string.Empty).Trim().ToLowerInvariant();
        var subj = (subject ?? string.Empty).Trim();
        var normalisedBody = string.Join('\n', (body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n').Select(l => l.TrimEnd())).Trim();
        return Normalizer.Sha256Hex($"v1\u001F{to}\u001F{subj}\u001F{normalisedBody}");
    }
}
