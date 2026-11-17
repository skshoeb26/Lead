using FluentAssertions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Intelligence;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;

namespace LeadPilot.Tests.Unit;

public class LeadScoringTests
{
    private static readonly OwnerProfile Owner = new();
    private readonly LeadScoringEngine _engine = new();

    private static Lead LeadWith(string? website = "https://abc.example", string? email = "info@abc.example", string? phone = "+919876543210")
    {
        var lead = new Lead { BusinessName = "ABC Coaching", WebsiteUrl = website, Email = email, Phone = phone };
        if (email is not null) lead.Contacts.Add(new LeadContact { Type = ContactType.Email, Value = email, NormalizedValue = email, SyntaxVerified = true, SourceVerified = true });
        if (phone is not null) lead.Contacts.Add(new LeadContact { Type = ContactType.Phone, Value = phone, NormalizedValue = phone, SyntaxVerified = true });
        return lead;
    }

    private static WebsiteAnalysis Site(bool good) => new()
    {
        Status = ProcessStatus.Completed, IsReachable = true, IsHttps = good, HasViewportMeta = good, HasEnquiryForm = good, HasContactPage = good,
        HasClearCallToAction = good, HasWhatsAppLink = good, MetaDescription = good ? "desc" : null, BrokenLinksCount = good ? 0 : 3, PageSpeedPerformance = good ? 90 : 20
    };

    [Fact]
    public void Score_is_deterministic_and_bounded()
    {
        var input = new ScoreInput { Lead = LeadWith(), Website = Site(false), Category = new LeadCategory { Name = "Coaching", RelevanceScore = 85, RelevantServices = "CRM" } };
        var a = _engine.Calculate(input, new ScoringWeights(), Owner);
        var b = _engine.Calculate(input, new ScoringWeights(), Owner);
        a.Total.Should().Be(b.Total);
        a.Total.Should().BeInRange(0, 100);
        a.Components.Should().HaveCount(6);
        a.Components.Sum(c => c.Weight).Should().BeInRange(99, 101);
    }

    [Fact]
    public void Website_with_many_gaps_scores_higher_than_polished_site()
    {
        var gaps = _engine.Calculate(new ScoreInput { Lead = LeadWith(), Website = Site(false) }, new ScoringWeights(), Owner);
        var polished = _engine.Calculate(new ScoreInput { Lead = LeadWith(), Website = Site(true) }, new ScoringWeights(), Owner);
        gaps.Components.First(c => c.Criterion == "Website gaps").Points.Should().BeGreaterThan(polished.Components.First(c => c.Criterion == "Website gaps").Points);
        gaps.Total.Should().BeGreaterThan(polished.Total);
    }

    [Fact]
    public void No_contacts_gives_zero_contact_points()
    {
        var r = _engine.Calculate(new ScoreInput { Lead = LeadWith(website: null, email: null, phone: null) }, new ScoringWeights(), Owner);
        r.Components.First(c => c.Criterion == "Contact availability").Points.Should().Be(0);
    }

    [Fact]
    public void Custom_weights_are_normalised_to_100()
    {
        var w = new ScoringWeights { WebsiteGaps = 50, ServiceFit = 50, ContactAvailability = 50, IdentifiedOpportunity = 50, BusinessRelevance = 50, BudgetSuitability = 50 };
        var r = _engine.Calculate(new ScoreInput { Lead = LeadWith(), Website = Site(false) }, w, Owner);
        r.Total.Should().BeInRange(0, 100);
        r.Components.Should().OnlyContain(c => c.Weight == 17);
    }

    [Fact]
    public void Permanently_closed_business_gets_no_relevance()
    {
        var lead = LeadWith();
        lead.BusinessStatus = "CLOSED_PERMANENTLY";
        var r = _engine.Calculate(new ScoreInput { Lead = lead, Category = new LeadCategory { Name = "X", RelevanceScore = 90 } }, new ScoringWeights(), Owner);
        r.Components.First(c => c.Criterion == "Business relevance").Points.Should().Be(0);
    }

    [Theory]
    [InlineData(85, LeadPriority.High)]
    [InlineData(80, LeadPriority.High)]
    [InlineData(65, LeadPriority.Good)]
    [InlineData(45, LeadPriority.NeedsReview)]
    [InlineData(10, LeadPriority.Low)]
    public void Priority_bands_follow_configured_thresholds(int score, LeadPriority expected) =>
        LeadScoringEngine.Classify(score, new ScoringWeights()).Should().Be(expected);
}

public class DuplicateDetectionTests
{
    private static Lead Existing() => new()
    {
        Id = Guid.NewGuid(), BusinessName = "ABC Coaching Classes Pvt Ltd", NormalizedName = Normalizer.NormalizeName("ABC Coaching Classes Pvt Ltd"),
        WebsiteDomain = "abccoaching.in", NormalizedPhone = "+919876543210", City = "Pune", PinCode = "411001", SourceProvider = "google_places", ProviderPlaceId = "place-1"
    };

    [Fact]
    public void Same_provider_id_is_exact_duplicate() =>
        DuplicateDetector.Compare(new DuplicateProbe("google_places", "place-1", "Totally different", null, null, null, null), Existing())!.IsExact.Should().BeTrue();

    [Fact]
    public void Same_domain_with_www_and_path_is_exact_duplicate() =>
        DuplicateDetector.Compare(new DuplicateProbe(null, null, "Other", "https://www.abccoaching.in/contact", null, null, null), Existing())!.IsExact.Should().BeTrue();

    [Fact]
    public void Same_phone_in_different_format_is_exact_duplicate() =>
        DuplicateDetector.Compare(new DuplicateProbe(null, null, "Other", null, "098765 43210", null, null), Existing())!.IsExact.Should().BeTrue();

    [Fact]
    public void Similar_name_in_same_city_is_fuzzy_duplicate()
    {
        var c = DuplicateDetector.Compare(new DuplicateProbe(null, null, "ABC Coaching Classes", null, null, "Pune", null), Existing());
        c.Should().NotBeNull();
        c!.IsExact.Should().BeFalse();
        c.Score.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public void Similar_name_in_different_city_is_not_duplicate() =>
        DuplicateDetector.Compare(new DuplicateProbe(null, null, "ABC Coaching Classes", null, null, "Surat", "395003"), Existing()).Should().BeNull();

    [Fact]
    public void Shared_social_domains_are_not_identity() =>
        DuplicateDetector.Compare(new DuplicateProbe(null, null, "X", "https://facebook.com/abc", null, null, null), ExistingOnFacebook()).Should().BeNull();

    private static Lead ExistingOnFacebook() { var l = Existing(); l.WebsiteDomain = "facebook.com"; return l; }
}

public class NormalizerTests
{
    [Theory]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("098765-43210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    [InlineData("0091 9876543210", "+919876543210")]
    public void Indian_phone_numbers_normalise_to_e164(string input, string expected) => Normalizer.NormalizePhone(input).Should().Be(expected);

    [Theory]
    [InlineData("info@abc.in", true)]
    [InlineData("bad@", false)]
    [InlineData("a b@c.com", false)]
    [InlineData("x@localhost", false)]
    [InlineData("user@mail.example.co.in", true)]
    public void Email_syntax(string email, bool valid) => Normalizer.IsValidEmailSyntax(email).Should().Be(valid);

    [Fact]
    public void Name_normalisation_removes_noise_words() => Normalizer.NormalizeName("The ABC Enterprises Pvt. Ltd.").Should().Be("abc");

    [Fact]
    public void Domain_extraction_strips_www() => Normalizer.ExtractDomain("http://WWW.Example.co.in/about").Should().Be("example.co.in");
}

public class OpportunityDetectorTests
{
    [Fact]
    public void Missing_enquiry_form_produces_evidence_and_caveat()
    {
        var lead = new Lead { BusinessName = "ABC", WebsiteUrl = "https://abc.example" };
        var category = new LeadCategory { Slug = "coaching-institutes", Name = "Coaching" };
        var site = new WebsiteAnalysis { Status = ProcessStatus.Completed, IsReachable = true, FinalUrl = "https://abc.example", HasEnquiryForm = false, HasViewportMeta = true, IsHttps = true, PagesInspectedJson = "[\"https://abc.example\"]" };
        var opps = OpportunityDetector.Detect(lead, category, site);
        var form = opps.Single(o => o.Code == "no_enquiry_form");
        form.Evidence.Should().Contain("inspected");
        form.Assumptions.Should().NotBeNullOrWhiteSpace();
        form.RecommendedService.Should().Contain("enquiry");
    }

    [Fact]
    public void Clinic_without_booking_gets_booking_opportunity()
    {
        var opps = OpportunityDetector.Detect(new Lead { BusinessName = "Clinic", WebsiteUrl = "https://c.example" }, new LeadCategory { Slug = "clinics", Name = "Clinics" },
            new WebsiteAnalysis { Status = ProcessStatus.Completed, IsReachable = true, HasBookingFeature = false, HasEnquiryForm = true });
        opps.Should().Contain(o => o.Code == "no_booking");
    }

    [Fact]
    public void No_website_yields_single_website_opportunity() =>
        OpportunityDetector.Detect(new Lead { BusinessName = "X", SourceProvider = "csv_import" }, null, null).Should().ContainSingle(o => o.Code == "no_website");

    [Fact]
    public void Unanalysed_website_yields_no_claims() =>
        OpportunityDetector.Detect(new Lead { BusinessName = "X", WebsiteUrl = "https://x.example" }, null, null).Should().BeEmpty();
}

public class AIValidationTests
{
    private static readonly string[] Services = ["Business websites and website redesign", "CRM and ERP development"];

    [Fact]
    public void Valid_response_parses()
    {
        const string json = """{"businessSummary":"A coaching institute.","opportunities":[{"title":"Enquiry form","problem":"p","evidence":"No form on 3 pages","assumptions":null,"recommendedService":"CRM","confidence":"medium"}],"recommendedServices":["CRM and ERP development","Blockchain"],"suggestedApproach":"x","estimatedProjectValueMin":15000,"estimatedProjectValueMax":35000,"confidenceScore":70,"evidenceQuality":"medium","nextAction":"email","unknowns":["budget"]}""";
        var (r, errors) = AIResultValidator.ParseLeadAnalysis(json, Services);
        errors.Should().BeEmpty();
        r!.RecommendedServices.Should().ContainSingle().Which.Should().Be("CRM and ERP development");
    }

    [Fact]
    public void Opportunity_without_evidence_is_rejected()
    {
        const string json = """{"businessSummary":"s","opportunities":[{"title":"t","problem":"p","evidence":"","assumptions":null,"recommendedService":"x","confidence":"high"}],"recommendedServices":[],"suggestedApproach":"","estimatedProjectValueMin":null,"estimatedProjectValueMax":null,"confidenceScore":50,"evidenceQuality":"low","nextAction":"","unknowns":[]}""";
        var (r, errors) = AIResultValidator.ParseLeadAnalysis(json, Services);
        r.Should().BeNull();
        errors.Should().Contain(e => e.Contains("evidence"));
    }

    [Theory]
    [InlineData("""{"businessSummary":"s","opportunities":[],"recommendedServices":[],"suggestedApproach":"","estimatedProjectValueMin":null,"estimatedProjectValueMax":null,"confidenceScore":150,"evidenceQuality":"low","nextAction":"","unknowns":[]}""")]
    [InlineData("""{"businessSummary":"s","opportunities":[],"recommendedServices":[],"suggestedApproach":"","estimatedProjectValueMin":50000,"estimatedProjectValueMax":1000,"confidenceScore":50,"evidenceQuality":"low","nextAction":"","unknowns":[]}""")]
    [InlineData("not json")]
    public void Invalid_responses_are_rejected(string json) => AIResultValidator.ParseLeadAnalysis(json, Services).Result.Should().BeNull();

    [Fact]
    public void Pitch_must_mention_business_name()
    {
        AIResultValidator.ParsePitch("""{"subject":"Hello","body":"Generic text","opportunitiesReferenced":[]}""", "Sunrise Clinic", true).Result.Should().BeNull();
        AIResultValidator.ParsePitch("""{"subject":"Idea for Sunrise Clinic","body":"Hi Sunrise team","opportunitiesReferenced":[]}""", "Sunrise Clinic", true).Result.Should().NotBeNull();
    }
}
