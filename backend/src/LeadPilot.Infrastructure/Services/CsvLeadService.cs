using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using LeadPilot.Application.Common;
using LeadPilot.Application.Leads;
using LeadPilot.Domain.Entities;
using LeadPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Infrastructure.Services;

/// <summary>CSV import (with per-row validation, duplicate detection and provenance) and export.</summary>
public sealed class CsvLeadService(AppDbContext db, LeadService leads, ICurrentUser currentUser, IAuditService audit)
{
    public const long MaxBytes = 2 * 1024 * 1024;
    public const int MaxRows = 5000;

    public static readonly string[] Columns =
        ["business_name", "category", "website", "phone", "email", "address", "locality", "city", "district", "state", "pincode", "source_url", "tags", "notes"];

    public async Task<CsvImportResult> ImportAsync(Stream stream, string fileName, bool skipDuplicates, CancellationToken ct)
    {
        var owner = currentUser.RequireUserId();
        var categories = await db.LeadCategories.AsNoTracking().ToListAsync(ct);
        var errors = new List<string>();
        int total = 0, created = 0, dup = 0, skipped = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = a => a.Header.Trim().ToLowerInvariant().Replace(' ', '_'),
            MissingFieldFound = null, HeaderValidated = null, BadDataFound = null, TrimOptions = TrimOptions.Trim
        });
        await csv.ReadAsync();
        csv.ReadHeader();
        if (csv.HeaderRecord is null || !csv.HeaderRecord.Any(h => h.Trim().ToLowerInvariant().Replace(' ', '_') == "business_name"))
            throw new ValidationFailedException("CSV must have a 'business_name' header. Expected columns: " + string.Join(", ", Columns));

        var validator = new CreateLeadValidator();
        while (await csv.ReadAsync())
        {
            if (++total > MaxRows) { errors.Add($"Stopped after {MaxRows} rows."); break; }
            string? Get(string name) { var v = csv.GetField(name); return string.IsNullOrWhiteSpace(v) ? null : v.Trim(); }
            var row = csv.Parser.Row;
            var name = Get("business_name");
            if (name is null) { skipped++; errors.Add($"Row {row}: business_name is empty."); continue; }

            var catName = Get("category");
            var category = catName is null ? null : categories.FirstOrDefault(c => c.Name.Equals(catName, StringComparison.OrdinalIgnoreCase) || c.Slug == Normalizer.Slugify(catName));
            var hasContact = Get("email") is not null || Get("phone") is not null;
            var request = new CreateLeadRequest
            {
                BusinessName = name, CategoryId = category?.Id, WebsiteUrl = Get("website"), Phone = Get("phone"), Email = Get("email"),
                AddressLine = Get("address"), Locality = Get("locality"), City = Get("city"), District = Get("district"), State = Get("state"),
                PinCode = Get("pincode"), Tags = Get("tags"), Notes = Get("notes"),
                ContactSourceUrl = Get("source_url") ?? (hasContact ? $"CSV import: {Path.GetFileName(fileName)}" : null)
            };
            var validation = validator.Validate(request);
            if (!validation.IsValid)
            {
                skipped++;
                errors.Add($"Row {row} ({name}): {string.Join("; ", validation.Errors.Select(e => e.ErrorMessage))}");
                continue;
            }
            var duplicates = await leads.FindDuplicatesAsync(new Application.Intelligence.DuplicateProbe(null, null, name, request.WebsiteUrl, request.Phone, request.City, request.PinCode), null, ct);
            if (duplicates.Any(d => d.IsExact) && skipDuplicates) { dup++; continue; }

            var lead = leads.BuildLead(request, "csv_import", owner);
            lead.Sources[0].Attribution = $"Imported from {Path.GetFileName(fileName)}";
            db.Leads.Add(lead);
            if (request.Notes is not null) db.CrmActivities.Add(Application.Crm.Activity.Create(lead.Id, Domain.ActivityType.Note, "Imported note", request.Notes, owner));
            created++;
            if (created % 200 == 0) await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("lead.csv_import", nameof(Lead), null, $"file={Path.GetFileName(fileName)} created={created} dup={dup} skipped={skipped}", ct);
        return new CsvImportResult(total, created, dup, skipped, errors.Take(100).ToList());
    }

    public async Task<byte[]> ExportAsync(LeadQuery query, CancellationToken ct)
    {
        var items = await leads.Filter(db.Leads.AsNoTracking().Include(l => l.Category), query).OrderBy(l => l.BusinessName).Take(20000).ToListAsync(ct);
        await using var ms = new MemoryStream();
        await using (var writer = new StreamWriter(ms, new UTF8Encoding(true)))
        await using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var h in new[] { "business_name", "category", "website", "phone", "email", "address", "locality", "city", "district", "state", "pincode",
                         "score", "priority", "stage", "expected_value", "actual_value", "next_follow_up", "source", "source_url", "discovered_at", "last_contacted_at" })
                csv.WriteField(h);
            await csv.NextRecordAsync();
            foreach (var l in items)
            {
                csv.WriteField(Safe(l.BusinessName)); csv.WriteField(Safe(l.Category?.Name)); csv.WriteField(Safe(l.WebsiteUrl)); csv.WriteField(Safe(l.Phone));
                csv.WriteField(Safe(l.Email)); csv.WriteField(Safe(l.AddressLine)); csv.WriteField(Safe(l.Locality)); csv.WriteField(Safe(l.City));
                csv.WriteField(Safe(l.District)); csv.WriteField(Safe(l.State)); csv.WriteField(Safe(l.PinCode)); csv.WriteField(l.Score);
                csv.WriteField(l.Priority?.ToString()); csv.WriteField(l.Stage.ToString()); csv.WriteField(l.ExpectedDealValue); csv.WriteField(l.ActualDealValue);
                csv.WriteField(l.NextFollowUpAt?.ToString("u")); csv.WriteField(l.SourceProvider); csv.WriteField(Safe(l.SourceUrl));
                csv.WriteField(l.DiscoveredAt.ToString("u")); csv.WriteField(l.LastContactedAt?.ToString("u"));
                await csv.NextRecordAsync();
            }
        }
        await audit.LogAsync("lead.csv_export", nameof(Lead), null, $"rows={items.Count}", ct);
        return ms.ToArray();
    }

    /// <summary>Neutralises spreadsheet formula injection (cells starting with = + - @).</summary>
    private static string? Safe(string? v) => v is { Length: > 0 } && "=+-@\t\r".Contains(v[0]) ? "'" + v : v;
}
