using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadPilot.Infrastructure.Web;

/// <summary>
/// Analyses publicly accessible pages only. It never submits forms, logs in, executes scripts, or bypasses access controls.
/// Results distinguish "not found on inspected pages" from "does not exist".
/// </summary>
public sealed partial class WebsiteAnalyzer(
    SafeHttpFetcher fetcher,
    IUrlSafetyValidator validator,
    IPageSpeedClient pageSpeed,
    IUsageTracker usage,
    IOptions<WebsiteAnalysisOptions> options,
    ILogger<WebsiteAnalyzer> logger) : IWebsiteAnalyzer
{
    private static readonly HtmlParser Parser = new(new HtmlParserOptions { IsScripting = false });

    private sealed class PageFacts
    {
        public string Url = "";
        public bool HasForm;
        public bool HasBooking;
        public bool HasWhatsApp;
        public bool HasOrdering;
        public bool HasAggregatorOrdering;
        public List<string> FormNotes = [];
        public List<string> BookingNotes = [];
    }

    public async Task<WebsiteAnalysisResult> AnalyseAsync(Guid leadId, string url, bool usePageSpeed, CancellationToken ct)
    {
        var o = options.Value;
        var uri = validator.ValidateUrl(url);
        var analysis = new WebsiteAnalysis { LeadId = leadId, RequestedUrl = uri.ToString(), Status = ProcessStatus.Running };
        var evidence = new List<WebsiteAnalysisEvidence>();
        void Ev(string check, CheckResult result, string observation, string? page = null) =>
            evidence.Add(new WebsiteAnalysisEvidence { WebsiteAnalysisId = analysis.Id, Check = check, Result = result, Observation = observation, PageUrl = page });

        SafeFetchResult home;
        try
        {
            home = await fetcher.GetAsync(uri, true, ct);
            // Fallback: if https failed to connect, try plain http (common for small sites).
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && uri.Scheme == "https" && !ct.IsCancellationRequested)
        {
            try
            {
                var http = new UriBuilder(uri) { Scheme = "http", Port = 80 }.Uri;
                home = await fetcher.GetAsync(http, true, ct);
                Ev("https", CheckResult.Fail, $"HTTPS connection failed ({Short(ex.Message)}); site responded over plain HTTP.", http.ToString());
            }
            catch (Exception inner) when (inner is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                analysis.IsReachable = false;
                analysis.Error = Short(ex.Message);
                analysis.Status = ProcessStatus.Completed;
                analysis.AnalysedAt = DateTime.UtcNow;
                Ev("availability", CheckResult.Fail, $"Website did not respond: {Short(ex.Message)}", uri.ToString());
                return new WebsiteAnalysisResult(analysis, evidence);
            }
        }

        analysis.FinalUrl = home.FinalUri.ToString();
        analysis.HttpStatusCode = home.StatusCode;
        analysis.ResponseTimeMs = home.ElapsedMs;
        analysis.PageSizeBytes = home.Bytes;
        analysis.IsReachable = home.StatusCode is >= 200 and < 400;
        analysis.IsHttps = home.FinalUri.Scheme == "https";
        analysis.HttpsRedirect = uri.Scheme == "http" && analysis.IsHttps == true;

        if (analysis.IsReachable != true || string.IsNullOrEmpty(home.Body))
        {
            Ev("availability", CheckResult.Fail, $"Homepage returned HTTP {home.StatusCode}.", analysis.FinalUrl);
            analysis.Status = ProcessStatus.Completed;
            analysis.AnalysedAt = DateTime.UtcNow;
            return new WebsiteAnalysisResult(analysis, evidence);
        }
        Ev("availability", CheckResult.Pass, $"Homepage returned HTTP {home.StatusCode} in {home.ElapsedMs} ms ({home.Bytes / 1024} KB{(home.Truncated ? ", truncated at size limit" : "")}).", analysis.FinalUrl);
        if (!evidence.Any(e => e.Check == "https"))
            Ev("https", analysis.IsHttps == true ? CheckResult.Pass : CheckResult.Fail,
                analysis.IsHttps == true ? "Final URL uses HTTPS." : "Final URL uses plain HTTP (no HTTPS).", analysis.FinalUrl);

        var doc = await Parser.ParseDocumentAsync(home.Body, ct);
        var baseUri = home.FinalUri;

        // ---------- metadata / SEO
        analysis.Title = Truncate(doc.Title?.Trim(), 500);
        analysis.MetaDescription = Truncate(doc.QuerySelector("meta[name='description' i]")?.GetAttribute("content")?.Trim(), 1000);
        analysis.HasH1 = doc.QuerySelector("h1") is not null;
        var viewport = doc.QuerySelector("meta[name='viewport' i]")?.GetAttribute("content") ?? "";
        analysis.HasViewportMeta = viewport.Contains("width=device-width", StringComparison.OrdinalIgnoreCase);
        var generator = doc.QuerySelector("meta[name='generator' i]")?.GetAttribute("content");
        analysis.DetectedGenerator = Truncate(generator, 200);

        Ev("title", string.IsNullOrWhiteSpace(analysis.Title) ? CheckResult.Fail : analysis.Title!.Length is < 10 or > 70 ? CheckResult.Warning : CheckResult.Pass,
            string.IsNullOrWhiteSpace(analysis.Title) ? "No <title> tag." : $"Title: \"{Truncate(analysis.Title, 120)}\" ({analysis.Title!.Length} chars).", analysis.FinalUrl);
        Ev("meta_description", string.IsNullOrWhiteSpace(analysis.MetaDescription) ? CheckResult.Fail : CheckResult.Pass,
            string.IsNullOrWhiteSpace(analysis.MetaDescription) ? "No meta description." : $"Meta description present ({analysis.MetaDescription!.Length} chars).", analysis.FinalUrl);
        Ev("h1", analysis.HasH1 == true ? CheckResult.Pass : CheckResult.Fail, analysis.HasH1 == true ? "H1 heading present." : "No H1 heading on homepage.", analysis.FinalUrl);
        Ev("mobile_viewport", analysis.HasViewportMeta == true ? CheckResult.Pass : CheckResult.Fail,
            analysis.HasViewportMeta == true ? $"Responsive viewport meta tag present ({Truncate(viewport, 80)})." : "No responsive viewport meta tag (width=device-width) found.", analysis.FinalUrl);
        var noindex = (doc.QuerySelector("meta[name='robots' i]")?.GetAttribute("content") ?? "").Contains("noindex", StringComparison.OrdinalIgnoreCase);
        if (noindex) Ev("indexing", CheckResult.Warning, "Homepage has a robots 'noindex' directive - it may not appear in search results.", analysis.FinalUrl);
        Ev("canonical_og", doc.QuerySelector("link[rel='canonical' i]") is not null || doc.QuerySelector("meta[property='og:title' i]") is not null ? CheckResult.Pass : CheckResult.Warning,
            "Canonical/Open Graph tags " + (doc.QuerySelector("meta[property='og:title' i]") is not null ? "present." : "not found (affects link previews when shared on WhatsApp/social)."), analysis.FinalUrl);

        // ---------- links
        var anchors = doc.QuerySelectorAll("a[href]").Select(a => (Href: a.GetAttribute("href") ?? "", Text: (a.TextContent ?? "").Trim())).ToList();
        var internalLinks = new List<(Uri Uri, string Text)>();
        var social = new HashSet<string>();
        foreach (var (href, text) in anchors)
        {
            if (href.StartsWith("mailto:") || href.StartsWith("tel:") || href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(baseUri, href, out var linkUri)) continue;
            var host = linkUri.Host.ToLowerInvariant();
            foreach (var s in new[] { "facebook.com", "instagram.com", "linkedin.com", "youtube.com", "twitter.com", "x.com" })
                if (host == s || host.EndsWith("." + s)) social.Add(s.Replace(".com", ""));
            if ((linkUri.Scheme == "http" || linkUri.Scheme == "https") && SameSite(linkUri, baseUri)) internalLinks.Add((linkUri, text));
        }
        analysis.SocialLinksJson = JsonSerializer.Serialize(social.OrderBy(s => s));
        Ev("social_links", social.Count > 0 ? CheckResult.Pass : CheckResult.Warning,
            social.Count > 0 ? $"Social profiles linked: {string.Join(", ", social.OrderBy(s => s))}." : "No social media profile links found on the homepage.", analysis.FinalUrl);

        var contactLink = internalLinks.FirstOrDefault(l => ContactRegex().IsMatch(l.Uri.AbsolutePath) || ContactRegex().IsMatch(l.Text));
        analysis.HasContactPage = contactLink.Uri is not null;
        Ev("contact_page", analysis.HasContactPage == true ? CheckResult.Pass : CheckResult.Fail,
            analysis.HasContactPage == true ? $"Contact page linked: {contactLink.Uri}" : "No link to a contact page found in homepage navigation.", analysis.FinalUrl);

        var hasTel = anchors.Any(a => a.Href.StartsWith("tel:"));
        var hasMail = anchors.Any(a => a.Href.StartsWith("mailto:"));
        Ev("contact_methods", hasTel || hasMail ? CheckResult.Pass : CheckResult.Warning,
            $"Click-to-call link: {(hasTel ? "yes" : "no")}; email link: {(hasMail ? "yes" : "no")}.", analysis.FinalUrl);

        // ---------- CTA
        var ctaTexts = doc.QuerySelectorAll("a, button, input[type='submit']")
            .Select(e => (e.TextContent ?? e.GetAttribute("value") ?? "").Trim()).Where(t => t.Length is > 1 and < 60 && CtaRegex().IsMatch(t)).Distinct().Take(5).ToList();
        analysis.HasClearCallToAction = ctaTexts.Count > 0;
        Ev("call_to_action", ctaTexts.Count > 0 ? CheckResult.Pass : CheckResult.Fail,
            ctaTexts.Count > 0 ? $"Calls to action found: {string.Join(", ", ctaTexts.Select(t => $"\"{t}\""))}." : "No clear call-to-action buttons (e.g. Contact, Enquire, Book) found on the homepage.", analysis.FinalUrl);

        // ---------- page facts (homepage + selected internal pages)
        var facts = new List<PageFacts> { ExtractFacts(doc, analysis.FinalUrl) };
        var robots = o.RespectRobotsTxt ? await LoadRobotsAsync(baseUri, ct) : null;
        var candidates = internalLinks
            .Where(l => KeyPageRegex().IsMatch(l.Uri.AbsolutePath) || KeyPageRegex().IsMatch(l.Text))
            .Select(l => new Uri(l.Uri.GetLeftPart(UriPartial.Path)))
            .Where(u => u.AbsolutePath.TrimEnd('/') != baseUri.AbsolutePath.TrimEnd('/'))
            .Distinct().Take(o.MaxExtraPages).ToList();
        var inspected = new List<string> { analysis.FinalUrl };
        foreach (var page in candidates)
        {
            if (robots is not null && !robots.IsAllowed(page.AbsolutePath))
            {
                Ev("robots", CheckResult.NotApplicable, $"Skipped {page.AbsolutePath} (disallowed by robots.txt).", page.ToString());
                continue;
            }
            try
            {
                var r = await fetcher.GetAsync(page, true, ct);
                if (r.StatusCode is >= 200 and < 300 && r.Body is not null)
                {
                    facts.Add(ExtractFacts(await Parser.ParseDocumentAsync(r.Body, ct), r.FinalUri.ToString()));
                    inspected.Add(r.FinalUri.ToString());
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UnsafeUrlException && !ct.IsCancellationRequested)
            {
                logger.LogDebug("Could not fetch {Page}: {Message}", page, ex.Message);
            }
        }
        analysis.PagesInspectedJson = JsonSerializer.Serialize(inspected);
        var scope = $"{inspected.Count} public page(s) inspected";

        analysis.HasEnquiryForm = facts.Any(f => f.HasForm);
        Ev("enquiry_form", analysis.HasEnquiryForm == true ? CheckResult.Pass : CheckResult.Fail,
            analysis.HasEnquiryForm == true ? $"Enquiry/contact form found: {facts.First(f => f.HasForm).FormNotes.FirstOrDefault()} ({facts.First(f => f.HasForm).Url})."
                : $"No enquiry form with contact fields found ({scope}). It may exist on other pages or behind a login.");

        analysis.HasBookingFeature = facts.Any(f => f.HasBooking);
        Ev("booking", analysis.HasBookingFeature == true ? CheckResult.Pass : CheckResult.Fail,
            analysis.HasBookingFeature == true ? $"Booking functionality indicator: {facts.First(f => f.HasBooking).BookingNotes.FirstOrDefault()} ({facts.First(f => f.HasBooking).Url})."
                : $"No online booking/appointment/reservation functionality found ({scope}).");

        analysis.HasWhatsAppLink = facts.Any(f => f.HasWhatsApp);
        Ev("whatsapp", analysis.HasWhatsAppLink == true ? CheckResult.Pass : CheckResult.Fail,
            analysis.HasWhatsAppLink == true ? $"WhatsApp chat link found ({facts.First(f => f.HasWhatsApp).Url})." : $"No WhatsApp click-to-chat link found ({scope}).");

        analysis.HasOnlineOrdering = facts.Any(f => f.HasOrdering);
        if (facts.Any(f => f.HasAggregatorOrdering))
            Ev("online_ordering", CheckResult.Warning, "Links to food aggregators (e.g. Zomato/Swiggy) found; no direct ordering detected.");
        else if (analysis.HasOnlineOrdering == true)
            Ev("online_ordering", CheckResult.Pass, "Online ordering/cart functionality indicators found.");

        // ---------- accessibility basics
        var images = doc.QuerySelectorAll("img").ToList();
        analysis.ImagesWithoutAlt = images.Count(i => string.IsNullOrWhiteSpace(i.GetAttribute("alt")));
        var hasLang = !string.IsNullOrWhiteSpace(doc.DocumentElement.GetAttribute("lang"));
        Ev("accessibility", analysis.ImagesWithoutAlt > 3 || !hasLang ? CheckResult.Warning : CheckResult.Pass,
            $"{analysis.ImagesWithoutAlt} of {images.Count} homepage images lack alt text; page language attribute {(hasLang ? "set" : "missing")}.", analysis.FinalUrl);

        // ---------- freshness & visible errors
        var bodyText = doc.Body?.TextContent ?? "";
        var years = CopyrightRegex().Matches(bodyText).SelectMany(m => YearRegex().Matches(m.Value).Select(y => int.Parse(y.Value))).Where(y => y >= 1995 && y <= DateTime.UtcNow.Year + 1).ToList();
        analysis.LatestCopyrightYear = years.Count > 0 ? years.Max() : null;
        if (analysis.LatestCopyrightYear is not null)
            Ev("freshness", analysis.LatestCopyrightYear >= DateTime.UtcNow.Year - 1 ? CheckResult.Pass : CheckResult.Warning,
                $"Latest copyright year shown: {analysis.LatestCopyrightYear}.", analysis.FinalUrl);
        var visibleError = ErrorRegex().Match(bodyText);
        if (visibleError.Success) Ev("visible_errors", CheckResult.Fail, $"Visible technical error text on homepage: \"{Truncate(visibleError.Value, 120)}\".", analysis.FinalUrl);

        // ---------- broken links (internal, HEAD with GET fallback)
        var toCheck = internalLinks.Select(l => new Uri(l.Uri.GetLeftPart(UriPartial.Path))).Distinct()
            .Where(u => robots is null || robots.IsAllowed(u.AbsolutePath)).Take(o.MaxLinksToCheck).ToList();
        var broken = new List<string>();
        foreach (var link in toCheck)
        {
            try
            {
                var r = await fetcher.GetAsync(link, false, ct);
                if (r.StatusCode >= 400) broken.Add($"{link.AbsolutePath} ({r.StatusCode})");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                broken.Add($"{link.AbsolutePath} (no response)");
            }
            catch (UnsafeUrlException) { }
        }
        analysis.InternalLinksChecked = toCheck.Count;
        analysis.BrokenLinksCount = broken.Count;
        Ev("broken_links", broken.Count == 0 ? CheckResult.Pass : CheckResult.Fail,
            broken.Count == 0 ? $"All {toCheck.Count} internal links checked responded." : $"Broken links: {string.Join(", ", broken.Take(5))}.");

        // ---------- site type heuristic
        var hasLogin = internalLinks.Any(l => LoginRegex().IsMatch(l.Uri.AbsolutePath) || LoginRegex().IsMatch(l.Text));
        analysis.SiteTypeGuess = hasLogin ? "web-application"
            : generator is not null && CmsRegex().IsMatch(generator) ? "cms"
            : analysis.HasEnquiryForm == true || analysis.HasBookingFeature == true ? "interactive-site"
            : "static-brochure";
        Ev("site_type", CheckResult.NotApplicable,
            $"Heuristic only (uncertain): appears to be '{analysis.SiteTypeGuess}'" + (generator is null ? "." : $"; generator meta: {Truncate(generator, 60)}.") +
            (hasLogin ? " A login/portal link is present." : ""), analysis.FinalUrl);

        // ---------- PageSpeed
        if (usePageSpeed && pageSpeed.IsConfigured)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var ps = await pageSpeed.RunAsync(analysis.FinalUrl, ct);
                await usage.RecordAsync(new UsageLog { Provider = "pagespeed", Operation = "runPagespeed", LeadId = leadId, DurationMs = (int)sw.ElapsedMilliseconds }, ct);
                if (ps is not null)
                {
                    analysis.PageSpeedPerformance = ps.Performance; analysis.PageSpeedAccessibility = ps.Accessibility;
                    analysis.PageSpeedSeo = ps.Seo; analysis.PageSpeedBestPractices = ps.BestPractices; analysis.LargestContentfulPaintMs = ps.LcpMs;
                    Ev("pagespeed", ps.Performance is >= 50 ? CheckResult.Pass : CheckResult.Fail,
                        $"PageSpeed Insights (mobile): performance {ps.Performance}, accessibility {ps.Accessibility}, SEO {ps.Seo}, best practices {ps.BestPractices}" +
                        (ps.LcpMs is not null ? $", LCP {ps.LcpMs / 1000:0.0}s." : "."), analysis.FinalUrl);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                await usage.RecordAsync(new UsageLog { Provider = "pagespeed", Operation = "runPagespeed", LeadId = leadId, Success = false, Error = Short(ex.Message), DurationMs = (int)sw.ElapsedMilliseconds }, ct);
                Ev("pagespeed", CheckResult.Unknown, $"PageSpeed Insights unavailable: {Short(ex.Message)}");
            }
        }
        else
        {
            Ev("performance", home.ElapsedMs < 2500 ? CheckResult.Pass : CheckResult.Warning,
                $"Server responded in {home.ElapsedMs} ms with {home.Bytes / 1024} KB HTML (PageSpeed not run).", analysis.FinalUrl);
        }

        analysis.Status = ProcessStatus.Completed;
        analysis.AnalysedAt = DateTime.UtcNow;
        return new WebsiteAnalysisResult(analysis, evidence);
    }

    private static PageFacts ExtractFacts(IDocument doc, string url)
    {
        var f = new PageFacts { Url = url };
        foreach (var form in doc.QuerySelectorAll("form"))
        {
            var inputs = form.QuerySelectorAll("input, textarea, select").ToList();
            var names = string.Join(' ', inputs.Select(i => $"{i.GetAttribute("type")} {i.GetAttribute("name")} {i.GetAttribute("placeholder")} {i.GetAttribute("id")}")).ToLowerInvariant();
            var isSearch = inputs.Count <= 2 && (names.Contains("search") || Regex.IsMatch(names, @"\b(q|s)\b"));
            if (isSearch) continue;
            var hasContactField = inputs.Any(i => i.GetAttribute("type") is "email" or "tel") || form.QuerySelector("textarea") is not null
                                  || Regex.IsMatch(names, "email|phone|mobile|name|message|enquiry|inquiry");
            if (hasContactField && inputs.Count >= 2)
            {
                f.HasForm = true;
                f.FormNotes.Add($"form with {inputs.Count} fields");
                if (inputs.Any(i => i.GetAttribute("type") is "date" or "datetime-local" or "time") || Regex.IsMatch(names, "appointment|booking|slot|date"))
                {
                    f.HasBooking = true;
                    f.BookingNotes.Add("form with date/appointment fields");
                }
            }
        }
        foreach (var frame in doc.QuerySelectorAll("iframe[src], script[src]"))
        {
            var src = (frame.GetAttribute("src") ?? "").ToLowerInvariant();
            if (Regex.IsMatch(src, "forms\\.gle|docs\\.google\\.com/forms|typeform|jotform|zohopublic|formspree|hsforms|tally\\.so")) { f.HasForm = true; f.FormNotes.Add("embedded form service"); }
            if (Regex.IsMatch(src, "calendly|practo|setmore|simplybook|zocdoc|appointy|bookeo|acuityscheduling|opentable|dineout|eazydiner")) { f.HasBooking = true; f.BookingNotes.Add($"booking widget ({src.Split('/').ElementAtOrDefault(2)})"); }
        }
        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var href = (a.GetAttribute("href") ?? "").ToLowerInvariant();
            var text = (a.TextContent ?? "").Trim().ToLowerInvariant();
            if (href.Contains("wa.me/") || href.Contains("api.whatsapp.com") || href.StartsWith("whatsapp:") || href.Contains("web.whatsapp.com")) f.HasWhatsApp = true;
            if (Regex.IsMatch(href, "calendly|practo\\.com|setmore|simplybook|appointy|opentable|dineout|eazydiner")) { f.HasBooking = true; f.BookingNotes.Add($"link to booking service ({href.Split('/').ElementAtOrDefault(2)})"); }
            if (Regex.IsMatch(text, @"book (an )?appointment|book now|book online|schedule (a )?(visit|appointment)|reserve (a )?table|make a reservation")) { f.HasBooking = true; f.BookingNotes.Add($"\"{text}\" link"); }
            if (Regex.IsMatch(href, "zomato\\.com|swiggy\\.com")) f.HasAggregatorOrdering = true;
            if (Regex.IsMatch(text, "order online|order now|add to cart|checkout") || href.Contains("/cart")) f.HasOrdering = true;
        }
        return f;
    }

    private async Task<RobotsRules?> LoadRobotsAsync(Uri baseUri, CancellationToken ct)
    {
        try
        {
            var r = await fetcher.GetAsync(new Uri(baseUri, "/robots.txt"), true, ct);
            return r.StatusCode == 200 && r.Body is not null ? RobotsRules.Parse(r.Body, "LeadPilotBot") : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UnsafeUrlException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool SameSite(Uri a, Uri b) =>
        string.Equals(a.Host.Replace("www.", ""), b.Host.Replace("www.", ""), StringComparison.OrdinalIgnoreCase);

    private static string Short(string s) => s.Length > 200 ? s[..200] : s;
    private static string? Truncate(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;

    [GeneratedRegex(@"contact|reach[- ]?us|get[- ]?in[- ]?touch|enquir|inquir|संपर्क", RegexOptions.IgnoreCase)] private static partial Regex ContactRegex();
    [GeneratedRegex(@"contact|enquir|inquir|book|appointment|admission|reserv|order|services|about", RegexOptions.IgnoreCase)] private static partial Regex KeyPageRegex();
    [GeneratedRegex(@"\b(call|contact|enquire|enquiry|inquire|book|get (a )?quote|get started|register|enrol|enroll|admission|order|whatsapp|request|apply|schedule|talk to)\b", RegexOptions.IgnoreCase)] private static partial Regex CtaRegex();
    [GeneratedRegex(@"(©|&copy;|copyright)[^\n]{0,40}", RegexOptions.IgnoreCase)] private static partial Regex CopyrightRegex();
    [GeneratedRegex(@"\b(19|20)\d{2}\b")] private static partial Regex YearRegex();
    [GeneratedRegex(@"(Fatal error|Parse error|Warning: [a-z_]+\(|Database Error|Error establishing a database connection|Index of /|Internal Server Error|Uncaught exception)", RegexOptions.IgnoreCase)] private static partial Regex ErrorRegex();
    [GeneratedRegex(@"log[- ]?in|sign[- ]?in|portal|dashboard|my[- ]?account|register", RegexOptions.IgnoreCase)] private static partial Regex LoginRegex();
    [GeneratedRegex(@"wordpress|wix|shopify|joomla|drupal|squarespace|webflow|blogger", RegexOptions.IgnoreCase)] private static partial Regex CmsRegex();
}

/// <summary>Minimal robots.txt evaluator (longest-match Allow/Disallow for our agent or '*').</summary>
public sealed class RobotsRules
{
    private readonly List<(string Path, bool Allow)> _rules = [];

    public static RobotsRules Parse(string content, string agent)
    {
        var groups = new Dictionary<string, List<(string, bool)>>(StringComparer.OrdinalIgnoreCase);
        List<string> currentAgents = [];
        var lastWasAgent = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var idx = line.IndexOf(':');
            if (idx < 0) continue;
            var key = line[..idx].Trim().ToLowerInvariant();
            var value = line[(idx + 1)..].Trim();
            if (key == "user-agent")
            {
                if (!lastWasAgent) currentAgents = [];
                currentAgents.Add(value);
                lastWasAgent = true;
                continue;
            }
            lastWasAgent = false;
            if (key is not ("allow" or "disallow")) continue;
            foreach (var a in currentAgents)
            {
                if (!groups.TryGetValue(a, out var list)) groups[a] = list = [];
                if (value.Length > 0 || key == "allow") list.Add((value, key == "allow"));
            }
        }
        var rules = new RobotsRules();
        var chosen = groups.FirstOrDefault(g => agent.StartsWith(g.Key, StringComparison.OrdinalIgnoreCase) && g.Key != "*").Value
                     ?? (groups.TryGetValue("*", out var star) ? star : []);
        rules._rules.AddRange(chosen);
        return rules;
    }

    public bool IsAllowed(string path)
    {
        var match = _rules.Where(r => r.Path.Length > 0 && path.StartsWith(r.Path.TrimEnd('*'), StringComparison.Ordinal))
            .OrderByDescending(r => r.Path.Length).ThenByDescending(r => r.Allow).FirstOrDefault();
        return match == default || match.Allow;
    }
}
