using System.Net;
using System.Net.Sockets;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using Microsoft.Extensions.Options;

namespace LeadPilot.Infrastructure.Web;

/// <summary>
/// SSRF protection. Only http/https on allowed ports; hosts must resolve exclusively to public addresses.
/// The connect callback re-checks the actual IP being dialled, defeating DNS-rebinding between validation and connect.
/// </summary>
public sealed class UrlSafetyValidator(IOptions<WebsiteAnalysisOptions> options) : IUrlSafetyValidator
{
    public Uri ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2000) throw new UnsafeUrlException("URL is empty or too long.");
        var candidate = url.Trim();
        if (!candidate.Contains("://")) candidate = "https://" + candidate;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) throw new UnsafeUrlException("URL is not valid.");
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) throw new UnsafeUrlException($"Scheme '{uri.Scheme}' is not allowed.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new UnsafeUrlException("URLs with credentials are not allowed.");
        if (!options.Value.AllowedPorts.Contains(uri.Port)) throw new UnsafeUrlException($"Port {uri.Port} is not allowed.");
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host is "localhost" || host.EndsWith(".localhost") || host.EndsWith(".local") || host.EndsWith(".internal") || host.EndsWith(".lan")
            || host.EndsWith(".home") || host.EndsWith(".corp") || !host.Contains('.'))
            throw new UnsafeUrlException("Internal host names are not allowed.");
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal) && !IsPublicAddress(literal))
            throw new UnsafeUrlException("Private or reserved IP addresses are not allowed.");
        return uri;
    }

    public bool IsPublicAddress(IPAddress address) => IsPublic(address);

    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None) || ip.Equals(IPAddress.IPv6None))
            return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127 ||
                     (b[0] == 100 && b[1] >= 64 && b[1] <= 127) ||   // CGNAT 100.64/10
                     (b[0] == 169 && b[1] == 254) ||                 // link-local / cloud metadata
                     (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                     (b[0] == 192 && b[1] == 0 && b[2] == 0) ||
                     (b[0] == 192 && b[1] == 0 && b[2] == 2) ||
                     (b[0] == 192 && b[1] == 168) ||
                     (b[0] == 198 && (b[1] == 18 || b[1] == 19)) ||
                     (b[0] == 198 && b[1] == 51 && b[2] == 100) ||
                     (b[0] == 203 && b[1] == 0 && b[2] == 113) ||
                     b[0] >= 224);                                   // multicast + reserved
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                 // fc00::/7 unique local
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // documentation
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B) return false; // NAT64
            return true;
        }
        return false;
    }

    /// <summary>Handler that resolves DNS itself and refuses to connect to non-public addresses. Redirects are handled manually.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(8),
        UseCookies = false,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var target = addresses.FirstOrDefault(IsPublic)
                         ?? throw new UnsafeUrlException($"Host '{context.DnsEndPoint.Host}' does not resolve to a public address.");
            if (addresses.Any(a => !IsPublic(a)))
                throw new UnsafeUrlException($"Host '{context.DnsEndPoint.Host}' resolves to a private address.");
            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}

public sealed record SafeFetchResult(Uri RequestedUri, Uri FinalUri, int StatusCode, string? ContentType, string? Body, long Bytes, int ElapsedMs, bool Truncated, IReadOnlyList<Uri> Redirects);

/// <summary>Fetches public pages with manual redirect validation, size limits and timeouts. Never executes scripts.</summary>
public sealed class SafeHttpFetcher(IHttpClientFactory factory, IUrlSafetyValidator validator, IOptions<WebsiteAnalysisOptions> options)
{
    public const string ClientName = "safe-web";

    public async Task<SafeFetchResult> GetAsync(Uri uri, bool readBody, CancellationToken ct)
    {
        var o = options.Value;
        var client = factory.CreateClient(ClientName);
        var redirects = new List<Uri>();
        var current = validator.ValidateUrl(uri.ToString());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));

        for (var i = 0; ; i++)
        {
            using var request = new HttpRequestMessage(readBody ? HttpMethod.Get : HttpMethod.Head, current);
            request.Headers.TryAddWithoutValidation("User-Agent", o.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.5");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-IN,en;q=0.9");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var code = (int)response.StatusCode;
            if (code is >= 300 and < 400 && response.Headers.Location is not null)
            {
                if (i >= o.MaxRedirects) throw new UnsafeUrlException("Too many redirects.");
                var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                current = validator.ValidateUrl(next.ToString());
                redirects.Add(current);
                continue;
            }
            if (!readBody && code == 405) { readBody = true; i--; continue; } // HEAD not allowed: retry GET once

            var contentType = response.Content.Headers.ContentType?.MediaType;
            string? body = null;
            long bytes = 0;
            var truncated = false;
            if (readBody && (contentType is null || contentType.Contains("html") || contentType.Contains("text") || contentType.Contains("xml")))
            {
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                var buffer = new byte[16 * 1024];
                using var ms = new MemoryStream();
                int read;
                while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (ms.Length + read > o.MaxResponseBytes) { truncated = true; ms.Write(buffer, 0, (int)(o.MaxResponseBytes - ms.Length)); break; }
                    ms.Write(buffer, 0, read);
                }
                bytes = ms.Length;
                var charset = response.Content.Headers.ContentType?.CharSet;
                var encoding = System.Text.Encoding.UTF8;
                try { if (!string.IsNullOrWhiteSpace(charset)) encoding = System.Text.Encoding.GetEncoding(charset.Trim('"')); } catch (ArgumentException) { }
                body = encoding.GetString(ms.ToArray());
            }
            return new SafeFetchResult(uri, current, code, contentType, body, bytes, (int)sw.ElapsedMilliseconds, truncated, redirects);
        }
    }
}
