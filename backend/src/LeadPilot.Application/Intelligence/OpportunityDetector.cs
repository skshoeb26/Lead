using LeadPilot.Domain;
using LeadPilot.Domain.Entities;

namespace LeadPilot.Application.Intelligence;

/// <summary>
/// Deterministic opportunity rules derived only from observed website evidence and the business category.
/// Every rule states the observation (evidence) separately from any assumption, and never claims a feature
/// is definitely missing - only that it was not found on the publicly accessible pages inspected.
/// </summary>
public static class OpportunityDetector
{
    private const string Scope = "on the publicly accessible pages inspected";

    public static List<Opportunity> Detect(Lead lead, LeadCategory? category, WebsiteAnalysis? w)
    {
        var list = new List<Opportunity>();
        var slug = category?.Slug ?? string.Empty;
        var pages = PagesSummary(w);

        void Add(string code, string title, string problem, string evidence, string service, Confidence conf, string? assumptions = null) =>
            list.Add(new Opportunity
            {
                LeadId = lead.Id,
                WebsiteAnalysisId = w?.Id,
                Code = code,
                Title = title,
                Problem = problem,
                Evidence = evidence,
                Assumptions = assumptions,
                RecommendedService = service,
                Confidence = conf,
                Source = OpportunitySource.RuleEngine
            });

        if (string.IsNullOrWhiteSpace(lead.WebsiteUrl))
        {
            Add("no_website", "No business website listed",
                "Prospective customers cannot find detailed information or enquire online.",
                $"No website URL is recorded for this business (source: {lead.SourceProvider}).",
                "Business website development", Confidence.Medium,
                "The business may have a website not listed with the data provider.");
            return list;
        }

        if (w is null || w.Status != ProcessStatus.Completed) return list;

        if (w.IsReachable == false)
        {
            Add("site_unreachable", "Website appears to be down",
                "Visitors may be unable to reach the website.",
                $"Request to {w.RequestedUrl} failed during analysis ({w.Error ?? "no response"}) at {w.AnalysedAt:u}.",
                "Website maintenance and technical consulting", Confidence.Low,
                "The outage may be temporary; re-check before mentioning it.");
            return list;
        }

        if (w.HasViewportMeta == false)
            Add("not_mobile_friendly", "Website may not be mobile-friendly",
                "Most Indian users browse on mobile; a non-responsive site can lose enquiries.",
                $"No responsive viewport meta tag was found on the homepage ({w.FinalUrl}).",
                "Website redesign (mobile-first)", Confidence.Medium);

        if (w.IsHttps == false)
            Add("no_https", "Website does not use HTTPS",
                "Browsers mark non-HTTPS sites as 'Not secure', which reduces visitor trust.",
                $"The final URL after redirects was {w.FinalUrl} (no HTTPS).",
                "Website maintenance and technical consulting", Confidence.High);

        if (w.HasEnquiryForm == false)
        {
            var (title, service) = slug switch
            {
                "coaching-institutes" or "schools" => ("No online enquiry / admission form", "Custom enquiry management application"),
                "real-estate" or "builders" => ("No property enquiry capture", "Lead management CRM for real estate"),
                "manufacturers" or "distributors" => ("No product enquiry workflow", "Product catalogue with enquiry workflow"),
                _ => ("No online enquiry form", "Enquiry capture form with CRM integration")
            };
            Add("no_enquiry_form", title,
                "Prospects must call or email directly, so enquiries are harder to capture and track.",
                $"No form with contact fields was detected {Scope} ({pages}).",
                service, Confidence.Medium,
                "An enquiry form may exist on pages that were not inspected or behind a login.");
        }

        if (w.HasBookingFeature == false && slug is "clinics" or "gyms" or "salons" or "hotels" or "restaurants")
        {
            var service = slug == "restaurants" ? "Online table reservation system" : "Online appointment booking system";
            Add("no_booking", slug == "restaurants" ? "No online reservation facility detected" : "No online appointment booking detected",
                "Customers must phone to book, which limits bookings outside business hours.",
                $"No booking/appointment/reservation functionality was found {Scope} ({pages}).",
                service, Confidence.Medium,
                "Bookings may be handled through a third-party platform not linked from the site.");
        }

        if (w.HasOnlineOrdering == false && slug == "restaurants")
            Add("no_online_ordering", "No direct online ordering detected",
                "Relying only on aggregators means paying commission on every order.",
                $"No ordering or cart functionality found {Scope}.",
                "Direct online ordering website", Confidence.Low,
                "The restaurant may intentionally use aggregators only.");

        if (w.HasContactPage == false && w.HasEnquiryForm != true)
            Add("no_contact_page", "No clear contact page",
                "Visitors may struggle to find how to reach the business.",
                $"No contact page link was found in the homepage navigation ({w.FinalUrl}).",
                "Website improvement", Confidence.Medium);

        if (w.HasWhatsAppLink == false)
            Add("no_whatsapp", "No WhatsApp contact link",
                "Many customers in India prefer WhatsApp for quick enquiries.",
                $"No wa.me / WhatsApp link was found {Scope}.",
                "WhatsApp click-to-chat integration", Confidence.Low);

        if (w.PageSpeedPerformance is < 50)
            Add("slow_site", "Slow mobile performance",
                "Slow pages increase bounce rates, especially on mobile data.",
                $"Google PageSpeed Insights mobile performance score: {w.PageSpeedPerformance}/100" +
                (w.LargestContentfulPaintMs is not null ? $", LCP {w.LargestContentfulPaintMs / 1000:0.0}s." : "."),
                "Website performance optimisation", Confidence.High);

        if (string.IsNullOrWhiteSpace(w.MetaDescription) || w.HasH1 == false)
            Add("weak_seo", "Weak basic SEO",
                "Missing basic metadata can reduce visibility in Google search.",
                $"Homepage: meta description {(string.IsNullOrWhiteSpace(w.MetaDescription) ? "missing" : "present")}, H1 heading {(w.HasH1 == false ? "missing" : "present")}.",
                "SEO basics and website improvement", Confidence.Medium);

        if (w.BrokenLinksCount is > 0)
            Add("broken_links", "Broken internal links",
                "Broken links create a poor visitor experience.",
                $"{w.BrokenLinksCount} of {w.InternalLinksChecked} internal links checked returned errors.",
                "Website maintenance", Confidence.High);

        if (w.LatestCopyrightYear is not null && w.LatestCopyrightYear < DateTime.UtcNow.Year - 2)
            Add("stale_site", "Website may be outdated",
                "An outdated site can signal an inactive business to visitors.",
                $"Latest copyright year visible on the site is {w.LatestCopyrightYear}.",
                "Website redesign", Confidence.Low,
                "A stale copyright notice does not prove the content is outdated.");

        if (w.SiteTypeGuess == "static-brochure" && slug is "coaching-institutes" or "schools" or "real-estate" or "manufacturers" or "distributors" or "clinics")
            Add("internal_crm", "Could benefit from a customer/lead management system",
                "Businesses handling many enquiries often track them manually.",
                "The website appears to be an informational site without any login, portal or enquiry tracking features visible.",
                "CRM development", Confidence.Low,
                "We cannot see internal systems; the business may already use a CRM.");

        return list;
    }

    private static string PagesSummary(WebsiteAnalysis? w)
    {
        if (w?.PagesInspectedJson is null) return w?.FinalUrl ?? "homepage";
        try
        {
            var pages = System.Text.Json.JsonSerializer.Deserialize<List<string>>(w.PagesInspectedJson) ?? [];
            return pages.Count == 0 ? "homepage" : $"{pages.Count} page(s): {string.Join(", ", pages.Take(5))}";
        }
        catch
        {
            return "homepage";
        }
    }
}
