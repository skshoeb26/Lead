using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using Microsoft.Extensions.Options;

namespace LeadPilot.Infrastructure.Providers;

/// <summary>
/// Google Places API (New) Text Search. Uses the official API only (no Maps scraping). Requests a minimal field
/// mask; place IDs may be stored indefinitely, other content is subject to Google's caching terms and is refreshed
/// or purged by the retention job (see GooglePlaces:ContentRetentionDays).
/// </summary>
public sealed class GooglePlacesProvider : ILeadDiscoveryProvider, IBusinessEnrichmentProvider
{
    public const string HttpClientName = "google-places";
    private const string SearchFieldMask = "places.id,places.displayName,places.formattedAddress,places.addressComponents,places.location," +
                                           "places.websiteUri,places.nationalPhoneNumber,places.internationalPhoneNumber,places.rating," +
                                           "places.userRatingCount,places.businessStatus,places.googleMapsUri,places.types,nextPageToken";
    private const string DetailsFieldMask = "id,displayName,formattedAddress,addressComponents,location,websiteUri,nationalPhoneNumber," +
                                            "internationalPhoneNumber,rating,userRatingCount,businessStatus,googleMapsUri,types";

    private readonly IHttpClientFactory _factory;
    private readonly GooglePlacesOptions _o;
    private readonly RateLimiter _limiter;

    public GooglePlacesProvider(IHttpClientFactory factory, IOptions<GooglePlacesOptions> options)
    {
        _factory = factory;
        _o = options.Value;
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = Math.Max(1, _o.RequestsPerMinute), TokensPerPeriod = Math.Max(1, _o.RequestsPerMinute),
            ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 100, QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    }

    public string Name => "google_places";
    public string DisplayName => "Google Places API";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_o.ApiKey);
    public bool IsMock => false;
    public string Attribution => _o.Attribution;
    public decimal EstimatedCostPerRequestUsd => _o.CostPerRequestUsd;
    public int MaxResultsPerQuery => 60; // Text Search returns at most 3 pages of 20

    public async Task<DiscoverySearchPage> SearchAsync(DiscoverySearchRequest r, CancellationToken ct)
    {
        if (!IsConfigured) throw new IntegrationNotConfiguredException(DisplayName, "Set GooglePlaces__ApiKey.");
        using var lease = await _limiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired) throw new RateLimitedException("Google Places rate limit queue is full.");

        var location = string.Join(", ", new[] { r.Locality, r.City, r.District, r.State, r.PinCode }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var textQuery = string.IsNullOrEmpty(location) ? $"{r.Keyword} in India" : $"{r.Keyword} in {location}, India";
        var body = new Dictionary<string, object?>
        {
            ["textQuery"] = textQuery,
            ["pageSize"] = Math.Clamp(r.PageSize, 1, 20),
            ["regionCode"] = _o.RegionCode,
            ["languageCode"] = _o.LanguageCode
        };
        if (!string.IsNullOrEmpty(r.PageToken)) body["pageToken"] = r.PageToken;

        var client = _factory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_o.BaseUrl), "places:searchText")) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Goog-Api-Key", _o.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", SearchFieldMask);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Google Places returned {(int)response.StatusCode}: {(err.Length > 300 ? err[..300] : err)}", null, response.StatusCode);
        }
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>(Json, ct) ?? new SearchResponse();
        var results = (payload.Places ?? []).Where(p => p.Id is not null && p.DisplayName?.Text is not null).Select(Map).ToList();
        return new DiscoverySearchPage(results, payload.NextPageToken, _o.CostPerRequestUsd);
    }

    public async Task<DiscoveredBusiness?> GetDetailsAsync(string externalId, CancellationToken ct)
    {
        if (!IsConfigured) throw new IntegrationNotConfiguredException(DisplayName, "Set GooglePlaces__ApiKey.");
        using var lease = await _limiter.AcquireAsync(1, ct);
        var client = _factory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_o.BaseUrl), $"places/{Uri.EscapeDataString(externalId)}"));
        request.Headers.Add("X-Goog-Api-Key", _o.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", DetailsFieldMask);
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var p = await response.Content.ReadFromJsonAsync<Place>(Json, ct);
        return p?.Id is null ? null : Map(p);
    }

    private static DiscoveredBusiness Map(Place p)
    {
        string? Component(string type) => p.AddressComponents?.FirstOrDefault(c => c.Types?.Contains(type) == true)?.LongText;
        return new DiscoveredBusiness
        {
            ExternalId = p.Id!,
            Name = p.DisplayName!.Text!,
            WebsiteUrl = p.WebsiteUri,
            Phone = p.NationalPhoneNumber ?? p.InternationalPhoneNumber,
            FormattedAddress = p.FormattedAddress,
            Locality = Component("sublocality_level_1") ?? Component("sublocality"),
            City = Component("locality"),
            District = Component("administrative_area_level_3"),
            State = Component("administrative_area_level_1"),
            PinCode = Component("postal_code"),
            Latitude = p.Location?.Latitude,
            Longitude = p.Location?.Longitude,
            Rating = p.Rating,
            ReviewCount = p.UserRatingCount,
            BusinessStatus = p.BusinessStatus,
            SourceUrl = p.GoogleMapsUri,
            Types = p.Types ?? []
        };
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class SearchResponse
    {
        public List<Place>? Places { get; set; }
        public string? NextPageToken { get; set; }
    }

    private sealed class Place
    {
        public string? Id { get; set; }
        public LocalizedText? DisplayName { get; set; }
        public string? FormattedAddress { get; set; }
        public List<AddressComponent>? AddressComponents { get; set; }
        public LatLng? Location { get; set; }
        public string? WebsiteUri { get; set; }
        public string? NationalPhoneNumber { get; set; }
        public string? InternationalPhoneNumber { get; set; }
        public double? Rating { get; set; }
        public int? UserRatingCount { get; set; }
        public string? BusinessStatus { get; set; }
        public string? GoogleMapsUri { get; set; }
        public List<string>? Types { get; set; }
    }

    private sealed class LocalizedText { public string? Text { get; set; } }
    private sealed class LatLng { public double Latitude { get; set; } public double Longitude { get; set; } }
    private sealed class AddressComponent
    {
        public string? LongText { get; set; }
        public string? ShortText { get; set; }
        public List<string>? Types { get; set; }
    }
}

/// <summary>
/// Development/test-only provider that returns clearly-labelled fictitious businesses (names start with "[DEMO]",
/// domains use the reserved .example TLD, phone numbers are invalid +91 00000 placeholders). Never registered in Production.
/// </summary>
public sealed class MockDiscoveryProvider : ILeadDiscoveryProvider
{
    public string Name => "mock_discovery";
    public string DisplayName => "Demo data (development only)";
    public bool IsConfigured => true;
    public bool IsMock => true;
    public string Attribution => "Fictitious demo data";
    public decimal EstimatedCostPerRequestUsd => 0m;
    public int MaxResultsPerQuery => 40;

    public Task<DiscoverySearchPage> SearchAsync(DiscoverySearchRequest r, CancellationToken ct)
    {
        var page = string.IsNullOrEmpty(r.PageToken) ? 0 : int.Parse(r.PageToken);
        var city = r.City ?? r.State ?? "Mumbai";
        var seed = Math.Abs(HashCode.Combine(r.Keyword.ToLowerInvariant(), city.ToLowerInvariant()));
        var rnd = new Random(seed + page);
        var count = Math.Min(r.PageSize, 10);
        var results = Enumerable.Range(0, count).Select(i =>
        {
            var n = page * count + i + 1;
            var slug = $"{Normalizer.Slugify(r.Keyword)}-{Normalizer.Slugify(city)}-{n}";
            var hasSite = rnd.NextDouble() > 0.3;
            return new DiscoveredBusiness
            {
                ExternalId = $"demo-{seed}-{n}",
                Name = $"[DEMO] {System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(r.Keyword)} {n} {city}",
                WebsiteUrl = hasSite ? $"https://{slug}.example" : null,
                Phone = $"+91 00000 {rnd.Next(10000, 99999)}",
                Email = hasSite && rnd.NextDouble() > 0.4 ? $"info@{slug}.example" : null,
                FormattedAddress = $"{n} Demo Road, {city}",
                City = city,
                State = r.State,
                PinCode = r.PinCode,
                Rating = Math.Round(3 + rnd.NextDouble() * 2, 1),
                ReviewCount = rnd.Next(0, 300),
                BusinessStatus = "OPERATIONAL",
                SourceUrl = "https://demo.invalid/" + slug
            };
        }).ToList();
        var next = page < 3 ? (page + 1).ToString() : null;
        return Task.FromResult(new DiscoverySearchPage(results, next, 0m));
    }
}

public sealed class PageSpeedClient(IHttpClientFactory factory, IOptions<PageSpeedOptions> options) : IPageSpeedClient
{
    public const string HttpClientName = "pagespeed";
    public bool IsConfigured => options.Value.Enabled;

    public async Task<PageSpeedResult?> RunAsync(string url, CancellationToken ct)
    {
        var o = options.Value;
        var query = $"?url={Uri.EscapeDataString(url)}&strategy=mobile&category=performance&category=accessibility&category=seo&category=best-practices";
        if (!string.IsNullOrWhiteSpace(o.ApiKey)) query += $"&key={Uri.EscapeDataString(o.ApiKey)}";
        var client = factory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(o.BaseUrl + query, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"PageSpeed returned {(int)response.StatusCode}", null, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("lighthouseResult", out var lh)) return null;
        int? Score(string cat) => lh.TryGetProperty("categories", out var c) && c.TryGetProperty(cat, out var x) && x.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(s.GetDouble() * 100) : null;
        double? lcp = lh.TryGetProperty("audits", out var a) && a.TryGetProperty("largest-contentful-paint", out var l) && l.TryGetProperty("numericValue", out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : null;
        return new PageSpeedResult(Score("performance"), Score("accessibility"), Score("seo"), Score("best-practices"), lcp);
    }
}
