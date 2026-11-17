using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LeadPilot.Application.Common;

/// <summary>Normalisation helpers used for duplicate detection, suppression matching and validation.</summary>
public static partial class Normalizer
{
    private static readonly string[] NoiseWords =
    [
        "pvt", "private", "ltd", "limited", "llp", "inc", "co", "company", "the", "and", "&",
        "india", "services", "service", "enterprises", "enterprise"
    ];

    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var lowered = RemoveDiacritics(name).ToLowerInvariant();
        lowered = NonAlphaNumeric().Replace(lowered, " ");
        var tokens = lowered.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !NoiseWords.Contains(t));
        return string.Join(' ', tokens);
    }

    /// <summary>Normalises an Indian phone number to E.164 (+91XXXXXXXXXX) where possible; otherwise digits only.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        if (digits.StartsWith("0091")) digits = digits[4..];
        else if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        else if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];
        return digits.Length == 10 ? "+91" + digits : digits;
    }

    public static bool IsValidPhoneSyntax(string? phone)
    {
        var n = NormalizePhone(phone);
        if (n is null) return false;
        if (n.StartsWith("+91")) return n.Length == 13;
        return n.Length is >= 8 and <= 15;
    }

    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        return email.Trim().ToLowerInvariant();
    }

    public static bool IsValidEmailSyntax(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        if (email.Contains(' ') || email.Contains('\r') || email.Contains('\n')) return false;
        try
        {
            var addr = new MailAddress(email.Trim());
            return addr.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase)
                   && EmailDomain().IsMatch(addr.Host);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Extracts a normalised registrable host (lowercase, without www.) from a URL.</summary>
    public static string? ExtractDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var candidate = url.Trim();
        if (!candidate.Contains("://")) candidate = "http://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];
        return host.Length == 0 ? null : host;
    }

    public static string? NormalizeWebsiteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var candidate = url.Trim();
        if (!candidate.Contains("://")) candidate = "https://" + candidate;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : null;
    }

    public static string Slugify(string value)
    {
        var s = NonAlphaNumeric().Replace(RemoveDiacritics(value).ToLowerInvariant(), "-").Trim('-');
        return MultiDash().Replace(s, "-");
    }

    public static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Jaro-Winkler similarity in [0,1].</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        var matchDistance = Math.Max(a.Length, b.Length) / 2 - 1;
        var aMatches = new bool[a.Length];
        var bMatches = new bool[b.Length];
        var matches = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, b.Length);
            for (var j = start; j < end; j++)
            {
                if (bMatches[j] || a[i] != b[j]) continue;
                aMatches[i] = bMatches[j] = true;
                matches++;
                break;
            }
        }
        if (matches == 0) return 0;
        double transpositions = 0;
        var k = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (!aMatches[i]) continue;
            while (!bMatches[k]) k++;
            if (a[i] != b[k]) transpositions++;
            k++;
        }
        var m = (double)matches;
        var jaro = (m / a.Length + m / b.Length + (m - transpositions / 2) / m) / 3;
        var prefix = 0;
        for (var i = 0; i < Math.Min(4, Math.Min(a.Length, b.Length)) && a[i] == b[i]; i++) prefix++;
        return jaro + prefix * 0.1 * (1 - jaro);
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonAlphaNumeric();
    [GeneratedRegex("-{2,}")] private static partial Regex MultiDash();
    [GeneratedRegex(@"^[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}$", RegexOptions.IgnoreCase)] private static partial Regex EmailDomain();
}
