using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Infrastructure.Persistence;

/// <summary>Idempotent seed of default business categories. Users can add, edit or deactivate categories later.</summary>
public static class DbSeeder
{
    private static readonly (string Name, string Slug, string Keywords, string Services, int Relevance)[] Categories =
    [
        ("Coaching institutes", "coaching-institutes", "coaching institute,coaching classes,tuition centre", "Custom enquiry management application,CRM development,Business websites and website redesign", 85),
        ("Schools & educational institutes", "schools", "school,educational institute,college", "Business websites and website redesign,Custom web application development,Android mobile application development", 70),
        ("Clinics & hospitals", "clinics", "clinic,hospital,dental clinic,diagnostic centre", "Online appointment booking system,Business websites and website redesign,AI chatbot development", 85),
        ("Real estate agencies", "real-estate", "real estate agency,property dealer", "Lead management CRM for real estate,Business websites and website redesign,Dashboard and reporting solutions", 80),
        ("Builders & construction", "builders", "builder,construction company,developer", "CRM and ERP development,Business websites and website redesign", 75),
        ("Manufacturers", "manufacturers", "manufacturer,manufacturing company,factory", "CRM and ERP development,Product catalogue with enquiry workflow,Business process automation", 80),
        ("Restaurants & cafes", "restaurants", "restaurant,cafe", "Direct online ordering website,Online table reservation system,Business websites and website redesign", 60),
        ("Gyms & fitness centres", "gyms", "gym,fitness centre,yoga studio", "Online appointment booking system,Android mobile application development,Business websites and website redesign", 65),
        ("Travel agencies", "travel-agencies", "travel agency,tour operator", "Custom web application development,CRM development,AI chatbot development", 70),
        ("Hotels", "hotels", "hotel,resort,guest house", "Online table reservation system,Business websites and website redesign", 60),
        ("Consultants", "consultants", "consultant,consultancy,chartered accountant", "Business websites and website redesign,Business process automation,CRM development", 65),
        ("Distributors & wholesalers", "distributors", "distributor,wholesaler,trader", "CRM and ERP development,Dashboard and reporting solutions,Business process automation", 75),
        ("Salons & spas", "salons", "salon,spa,beauty parlour", "Online appointment booking system,Business websites and website redesign", 55),
        ("Small & medium businesses", "smb", "business,company", "Business websites and website redesign,Business process automation", 50),
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existing = await db.LeadCategories.Select(c => c.Slug).ToListAsync(ct);
        foreach (var c in Categories.Where(c => !existing.Contains(c.Slug)))
            db.LeadCategories.Add(new LeadCategory
            {
                Name = c.Name, Slug = c.Slug, SearchKeywords = c.Keywords, RelevantServices = c.Services, RelevanceScore = c.Relevance, IsSystem = true
            });
        await db.SaveChangesAsync(ct);
    }
}
