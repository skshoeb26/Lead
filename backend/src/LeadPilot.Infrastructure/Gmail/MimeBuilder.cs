using System.Net.Mail;
using System.Text;
using LeadPilot.Application.Abstractions;

namespace LeadPilot.Infrastructure.Gmail;

/// <summary>Builds RFC 2822 / MIME messages and the Base64URL "raw" form expected by the Gmail API.</summary>
public static class MimeBuilder
{
    public static string Build(OutgoingEmail email, DateTime? date = null, string? messageIdDomain = null)
    {
        Guard(email.To); Guard(email.From); Guard(email.Subject); if (email.ReplyTo is not null) Guard(email.ReplyTo);
        var from = ParseAddress(email.From);
        var to = string.IsNullOrWhiteSpace(email.ToName) ? new MailAddress(email.To) : new MailAddress(email.To, email.ToName);

        var sb = new StringBuilder();
        sb.Append("From: ").Append(FormatAddress(from)).Append("\r\n");
        sb.Append("To: ").Append(FormatAddress(to)).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(email.ReplyTo)) sb.Append("Reply-To: ").Append(FormatAddress(new MailAddress(email.ReplyTo))).Append("\r\n");
        sb.Append("Subject: ").Append(EncodeHeader(email.Subject)).Append("\r\n");
        sb.Append("Date: ").Append((date ?? DateTime.UtcNow).ToString("ddd, dd MMM yyyy HH:mm:ss +0000", System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("Message-ID: <").Append(Guid.NewGuid().ToString("N")).Append('@').Append(messageIdDomain ?? from.Host).Append(">\r\n");
        if (!string.IsNullOrWhiteSpace(email.ListUnsubscribe)) sb.Append("List-Unsubscribe: ").Append(email.ListUnsubscribe).Append("\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append("Content-Type: text/plain; charset=UTF-8\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append("\r\n");
        var body = email.Body.Replace("\r\n", "\n").Replace("\n", "\r\n");
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(body));
        for (var i = 0; i < b64.Length; i += 76) sb.Append(b64, i, Math.Min(76, b64.Length - i)).Append("\r\n");
        return sb.ToString();
    }

    public static string ToBase64Url(string mime) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string FromBase64Url(string raw)
    {
        var s = raw.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    /// <summary>RFC 2047 encoded-word (UTF-8, base64) when the value is not plain ASCII.</summary>
    public static string EncodeHeader(string value)
    {
        if (value.All(c => c >= 32 && c < 127)) return value;
        return "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
    }

    private static MailAddress ParseAddress(string value)
    {
        var lt = value.IndexOf('<');
        if (lt > 0 && value.EndsWith('>')) return new MailAddress(value[(lt + 1)..^1].Trim(), value[..lt].Trim().Trim('"'));
        return new MailAddress(value.Trim());
    }

    private static string FormatAddress(MailAddress a) =>
        string.IsNullOrWhiteSpace(a.DisplayName) ? a.Address : $"{EncodeDisplayName(a.DisplayName)} <{a.Address}>";

    private static string EncodeDisplayName(string name) =>
        name.All(c => c >= 32 && c < 127) ? "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : EncodeHeader(name);

    /// <summary>Prevents header injection.</summary>
    private static void Guard(string value)
    {
        if (value.Contains('\r') || value.Contains('\n')) throw new ArgumentException("Header values cannot contain line breaks.");
    }
}
