using FluentValidation;
using LeadPilot.Application.Common;
using LeadPilot.Application.Crm;
using LeadPilot.Application.Intelligence;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Application.Leads;

public sealed class CreateLeadValidator : AbstractValidator<CreateLeadRequest>
{
    public CreateLeadValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Email).Must(e => e is null || Normalizer.IsValidEmailSyntax(e)).WithMessage("Email format is invalid.");
        RuleFor(x => x.Phone).Must(p => p is null || Normalizer.IsValidPhoneSyntax(p)).WithMessage("Phone format is invalid.");
        RuleFor(x => x.WebsiteUrl).Must(u => u is null || Normalizer.NormalizeWebsiteUrl(u) is not null).WithMessage("Website URL is invalid.");
        RuleFor(x => x.ContactSourceUrl).NotEmpty()
            .When(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Record where the contact details were found (source URL or description).");
        RuleFor(x => x.PinCode).Matches("^[1-9][0-9]{5}$").When(x => !string.IsNullOrWhiteSpace(x.PinCode)).WithMessage("PIN code must be 6 digits.");
    }
}

public sealed class LeadService(IAppDbContext db, ICurrentUser currentUser, IAuditService audit, IClock clock)
{
    public async Task<PagedResult<LeadListItem>> ListAsync(LeadQuery q, CancellationToken ct)
    {
        var query = Filter(db.Leads.AsNoTracking(), q);
        var total = await query.CountAsync(ct);
        var pageSize = Math.Clamp(q.PageSize, 1, 200);
        var page = Math.Max(1, q.Page);
        var items = await Sort(query, q.Sort).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(l => new LeadListItem(l.Id, l.BusinessName, l.CategoryId, l.Category != null ? l.Category.Name : null, l.City, l.State,
                l.PinCode, l.WebsiteUrl, l.Phone, l.Email, l.Score, l.Priority, l.WebsiteQualityScore, l.Stage, l.WebsiteAnalysisStatus,
                l.AIAnalysisStatus, l.Rating, l.ReviewCount, l.SourceProvider, l.DiscoveredAt, l.NextFollowUpAt, l.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResult<LeadListItem>(items, page, pageSize, total);
    }

    public IQueryable<Lead> Filter(IQueryable<Lead> query, LeadQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(l => l.BusinessName.ToLower().Contains(s) || (l.City != null && l.City.ToLower().Contains(s))
                                     || (l.WebsiteDomain != null && l.WebsiteDomain.Contains(s)) || (l.Email != null && l.Email.ToLower().Contains(s))
                                     || (l.Phone != null && l.Phone.Contains(s)));
        }
        if (q.CategoryId is not null) query = query.Where(l => l.CategoryId == q.CategoryId);
        if (!string.IsNullOrWhiteSpace(q.State)) { var v = q.State.ToLower(); query = query.Where(l => l.State != null && l.State.ToLower() == v); }
        if (!string.IsNullOrWhiteSpace(q.City))
        {
            var cities = q.City.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(c => c.ToLower()).ToList();
            query = query.Where(l => l.City != null && cities.Contains(l.City.ToLower()));
        }
        if (!string.IsNullOrWhiteSpace(q.District)) { var v = q.District.ToLower(); query = query.Where(l => l.District != null && l.District.ToLower() == v); }
        if (!string.IsNullOrWhiteSpace(q.Locality)) { var v = q.Locality.ToLower(); query = query.Where(l => l.Locality != null && l.Locality.ToLower().Contains(v)); }
        if (!string.IsNullOrWhiteSpace(q.PinCode)) query = query.Where(l => l.PinCode == q.PinCode);
        if (q.Stage is not null) query = query.Where(l => l.Stage == q.Stage);
        if (q.Priority is not null) query = query.Where(l => l.Priority == q.Priority);
        if (q.MinScore is not null) query = query.Where(l => l.Score >= q.MinScore);
        if (q.MaxScore is not null) query = query.Where(l => l.Score <= q.MaxScore);
        if (q.HasWebsite is not null) query = q.HasWebsite.Value ? query.Where(l => l.WebsiteUrl != null) : query.Where(l => l.WebsiteUrl == null);
        if (q.HasPhone is not null) query = q.HasPhone.Value ? query.Where(l => l.Phone != null) : query.Where(l => l.Phone == null);
        if (q.HasEmail is not null) query = q.HasEmail.Value ? query.Where(l => l.Email != null) : query.Where(l => l.Email == null);
        if (q.MinRating is not null) query = query.Where(l => l.Rating >= q.MinRating);
        if (q.MaxWebsiteQuality is not null) query = query.Where(l => l.WebsiteQualityScore <= q.MaxWebsiteQuality);
        if (q.DiscoveredFrom is not null) query = query.Where(l => l.DiscoveredAt >= q.DiscoveredFrom);
        if (q.DiscoveredTo is not null) query = query.Where(l => l.DiscoveredAt <= q.DiscoveredTo);
        if (!string.IsNullOrWhiteSpace(q.Source)) query = query.Where(l => l.SourceProvider == q.Source);
        return query;
    }

    private static IQueryable<Lead> Sort(IQueryable<Lead> q, string? sort) => (sort ?? "-discovered") switch
    {
        "score" => q.OrderBy(l => l.Score ?? -1),
        "-score" => q.OrderByDescending(l => l.Score ?? -1).ThenByDescending(l => l.DiscoveredAt),
        "name" => q.OrderBy(l => l.BusinessName),
        "-name" => q.OrderByDescending(l => l.BusinessName),
        "discovered" => q.OrderBy(l => l.DiscoveredAt),
        "followup" => q.OrderBy(l => l.NextFollowUpAt == null).ThenBy(l => l.NextFollowUpAt),
        "-updated" => q.OrderByDescending(l => l.UpdatedAt),
        _ => q.OrderByDescending(l => l.DiscoveredAt)
    };

    public async Task<LeadDetail> GetAsync(Guid id, CancellationToken ct)
    {
        var l = await db.Leads.AsNoTracking()
                    .Include(x => x.Category).Include(x => x.Contacts).Include(x => x.Sources).Include(x => x.Opportunities)
                    .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Lead", id);

        var score = await db.LeadScores.AsNoTracking().Where(s => s.LeadId == id).OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
        var website = await db.WebsiteAnalyses.AsNoTracking().Include(w => w.Evidence).Where(w => w.LeadId == id)
            .OrderByDescending(w => w.CreatedAt).FirstOrDefaultAsync(ct);
        var ai = await db.AIAnalyses.AsNoTracking().Where(a => a.LeadId == id).OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        var values = l.Contacts.Select(c => c.NormalizedValue).ToList();
        var suppressed = await db.SuppressionList.AsNoTracking().Where(s => values.Contains(s.NormalizedValue)).Select(s => s.NormalizedValue).ToListAsync(ct);

        return new LeadDetail
        {
            Id = l.Id, BusinessName = l.BusinessName, CategoryId = l.CategoryId, CategoryName = l.Category?.Name, WebsiteUrl = l.WebsiteUrl,
            Phone = l.Phone, Email = l.Email, AddressLine = l.AddressLine, Locality = l.Locality, City = l.City, District = l.District,
            State = l.State, PinCode = l.PinCode, Latitude = l.Latitude, Longitude = l.Longitude, SourceProvider = l.SourceProvider,
            ProviderPlaceId = l.ProviderPlaceId, SourceUrl = l.SourceUrl, DiscoveredAt = l.DiscoveredAt, LastVerifiedAt = l.LastVerifiedAt,
            Rating = l.Rating, ReviewCount = l.ReviewCount, BusinessStatus = l.BusinessStatus, Score = l.Score, Priority = l.Priority,
            WebsiteQualityScore = l.WebsiteQualityScore, Stage = l.Stage, WebsiteAnalysisStatus = l.WebsiteAnalysisStatus,
            AIAnalysisStatus = l.AIAnalysisStatus, ExpectedDealValue = l.ExpectedDealValue, ActualDealValue = l.ActualDealValue,
            LostReason = l.LostReason, NextAction = l.NextAction, NextFollowUpAt = l.NextFollowUpAt, LastContactedAt = l.LastContactedAt,
            Tags = l.Tags, ConcurrencyStamp = l.ConcurrencyStamp, CreatedAt = l.CreatedAt, UpdatedAt = l.UpdatedAt,
            Contacts = l.Contacts.OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Type).Select(c => new ContactDto(c.Id, c.Type, c.Value, c.Label,
                c.IsPrimary, c.SourceProvider, c.SourceUrl, c.SourceRecordedAt, c.SourceVerified, c.SyntaxVerified, c.DeliveryVerified,
                c.ConsentVerified, suppressed.Contains(c.NormalizedValue))).ToList(),
            Sources = l.Sources.OrderBy(s => s.RetrievedAt).Select(s => new SourceDto(s.Provider, s.ExternalId, s.SourceUrl, s.Attribution, s.RetrievedAt)).ToList(),
            Opportunities = l.Opportunities.Where(o => !o.IsDismissed).OrderByDescending(o => o.Confidence).Select(o => o.ToDto()).ToList(),
            LatestScore = score?.ToDto(),
            LatestWebsiteAnalysis = website?.ToDto(),
            LatestAIAnalysis = ai?.ToDto()
        };
    }

    public async Task<(Guid Id, IReadOnlyList<DuplicateCandidate> Duplicates)> CreateAsync(CreateLeadRequest r, string sourceProvider, CancellationToken ct)
    {
        var probe = new DuplicateProbe(null, null, r.BusinessName, r.WebsiteUrl, r.Phone, r.City, r.PinCode);
        var duplicates = await FindDuplicatesAsync(probe, null, ct);
        if (duplicates.Any(d => d.IsExact) && !r.AllowDuplicate)
            throw new ConflictException($"A matching lead already exists: {duplicates.First(d => d.IsExact).BusinessName}.", "duplicate_lead");

        var lead = BuildLead(r, sourceProvider, currentUser.RequireUserId());
        db.Leads.Add(lead);
        if (!string.IsNullOrWhiteSpace(r.Notes))
            db.CrmActivities.Add(Activity.Create(lead.Id, ActivityType.Note, "Note", r.Notes, currentUser.UserId));
        db.CrmActivities.Add(Activity.Create(lead.Id, ActivityType.System, "Lead created", $"Source: {sourceProvider}", currentUser.UserId));
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("lead.create", nameof(Lead), lead.Id.ToString(), $"source={sourceProvider}", ct);
        return (lead.Id, duplicates);
    }

    public Lead BuildLead(CreateLeadRequest r, string sourceProvider, Guid ownerId)
    {
        var website = Normalizer.NormalizeWebsiteUrl(r.WebsiteUrl);
        var now = clock.UtcNow;
        var lead = new Lead
        {
            OwnerUserId = ownerId,
            BusinessName = r.BusinessName.Trim(),
            NormalizedName = Normalizer.NormalizeName(r.BusinessName),
            CategoryId = r.CategoryId,
            WebsiteUrl = website,
            WebsiteDomain = Normalizer.ExtractDomain(website),
            AddressLine = r.AddressLine?.Trim(),
            Locality = r.Locality?.Trim(),
            City = r.City?.Trim(),
            District = r.District?.Trim(),
            State = r.State?.Trim(),
            PinCode = r.PinCode?.Trim(),
            Tags = r.Tags,
            SourceProvider = sourceProvider,
            SourceUrl = r.ContactSourceUrl,
            DiscoveredAt = now,
            WebsiteAnalysisStatus = website is null ? ProcessStatus.Skipped : ProcessStatus.NotStarted
        };
        if (!string.IsNullOrWhiteSpace(r.Email)) AttachContact(lead, ContactType.Email, r.Email, null, sourceProvider, r.ContactSourceUrl, true);
        if (!string.IsNullOrWhiteSpace(r.Phone)) AttachContact(lead, ContactType.Phone, r.Phone, null, sourceProvider, r.ContactSourceUrl, true);
        lead.Sources.Add(new LeadSource { LeadId = lead.Id, Provider = sourceProvider, SourceUrl = r.ContactSourceUrl, RetrievedAt = now });
        return lead;
    }

    public static LeadContact AttachContact(Lead lead, ContactType type, string value, string? label, string provider, string? sourceUrl, bool primary)
    {
        var normalized = type == ContactType.Email ? Normalizer.NormalizeEmail(value)! :
            type is ContactType.Phone or ContactType.WhatsApp ? Normalizer.NormalizePhone(value) ?? value.Trim() : value.Trim();
        var syntax = type switch
        {
            ContactType.Email => Normalizer.IsValidEmailSyntax(value),
            ContactType.Phone or ContactType.WhatsApp => Normalizer.IsValidPhoneSyntax(value),
            ContactType.Website => Normalizer.NormalizeWebsiteUrl(value) is not null,
            _ => true
        };
        var contact = new LeadContact
        {
            LeadId = lead.Id,
            Type = type,
            Value = value.Trim(),
            NormalizedValue = normalized,
            Label = label,
            IsPrimary = primary,
            SourceProvider = provider,
            SourceUrl = sourceUrl,
            SourceVerified = !string.IsNullOrWhiteSpace(sourceUrl) || provider != "manual",
            SyntaxVerified = syntax
        };
        lead.Contacts.Add(contact);
        if (primary && type == ContactType.Email) lead.Email = contact.NormalizedValue;
        if (primary && type == ContactType.Phone) { lead.Phone = contact.Value; lead.NormalizedPhone = Normalizer.NormalizePhone(contact.Value); }
        return contact;
    }

    public async Task UpdateAsync(Guid id, UpdateLeadRequest r, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Lead", id);
        if (r.ConcurrencyStamp is not null && r.ConcurrencyStamp != lead.ConcurrencyStamp)
            throw new ConflictException("This lead was modified elsewhere. Reload and try again.", "concurrency_conflict");
        var website = Normalizer.NormalizeWebsiteUrl(r.WebsiteUrl);
        if (website != lead.WebsiteUrl)
        {
            lead.WebsiteUrl = website;
            lead.WebsiteDomain = Normalizer.ExtractDomain(website);
            lead.WebsiteAnalysisStatus = website is null ? ProcessStatus.Skipped : ProcessStatus.NotStarted;
        }
        lead.BusinessName = r.BusinessName.Trim();
        lead.NormalizedName = Normalizer.NormalizeName(r.BusinessName);
        lead.CategoryId = r.CategoryId;
        lead.AddressLine = r.AddressLine; lead.Locality = r.Locality; lead.City = r.City; lead.District = r.District;
        lead.State = r.State; lead.PinCode = r.PinCode; lead.Tags = r.Tags; lead.NextAction = r.NextAction;
        lead.ExpectedDealValue = r.ExpectedDealValue; lead.ActualDealValue = r.ActualDealValue; lead.LostReason = r.LostReason;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("lead.update", nameof(Lead), id.ToString(), null, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException("Lead", id);
        lead.IsDeleted = true;
        lead.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("lead.delete", nameof(Lead), id.ToString(), null, ct);
    }

    /// <summary>Permanently erases a prospect and related personal data (data subject erasure request).</summary>
    public async Task EraseAsync(Guid id, bool addToSuppression, CancellationToken ct)
    {
        var lead = await db.Leads.IgnoreQueryFilters().Include(l => l.Contacts).FirstOrDefaultAsync(l => l.Id == id, ct)
                   ?? throw new NotFoundException("Lead", id);
        if (addToSuppression)
        {
            foreach (var c in lead.Contacts.Where(c => c.Type is ContactType.Email or ContactType.Phone or ContactType.WhatsApp))
            {
                var channel = c.Type == ContactType.Email ? CommunicationChannel.Email : c.Type == ContactType.WhatsApp ? CommunicationChannel.WhatsApp : CommunicationChannel.Phone;
                if (!await db.SuppressionList.AnyAsync(s => s.Channel == channel && s.NormalizedValue == c.NormalizedValue, ct))
                    db.SuppressionList.Add(new SuppressionEntry { Channel = channel, Value = c.NormalizedValue, NormalizedValue = c.NormalizedValue, Reason = "erasure_request" });
            }
        }
        db.EmailDrafts.RemoveRange(db.EmailDrafts.Where(e => e.LeadId == id));
        db.PitchDrafts.RemoveRange(db.PitchDrafts.Where(e => e.LeadId == id));
        db.CrmActivities.RemoveRange(db.CrmActivities.Where(e => e.LeadId == id));
        db.FollowUps.RemoveRange(db.FollowUps.Where(e => e.LeadId == id));
        db.Meetings.RemoveRange(db.Meetings.Where(e => e.LeadId == id));
        db.Proposals.RemoveRange(db.Proposals.Where(e => e.LeadId == id));
        db.Deals.RemoveRange(db.Deals.Where(e => e.LeadId == id));
        db.Opportunities.RemoveRange(db.Opportunities.Where(e => e.LeadId == id));
        db.AIAnalyses.RemoveRange(db.AIAnalyses.Where(e => e.LeadId == id));
        db.LeadScores.RemoveRange(db.LeadScores.Where(e => e.LeadId == id));
        db.WebsiteAnalyses.RemoveRange(db.WebsiteAnalyses.Where(e => e.LeadId == id));
        db.ConsentRecords.RemoveRange(db.ConsentRecords.Where(e => e.LeadId == id));
        db.Leads.Remove(lead);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("lead.erase", nameof(Lead), id.ToString(), $"suppressed={addToSuppression}", ct);
    }

    public async Task<ContactDto> AddContactAsync(Guid leadId, AddContactRequest r, CancellationToken ct)
    {
        var lead = await db.Leads.Include(l => l.Contacts).FirstOrDefaultAsync(l => l.Id == leadId, ct) ?? throw new NotFoundException("Lead", leadId);
        if (string.IsNullOrWhiteSpace(r.SourceUrl)) throw new ValidationFailedException("Record where this contact was found.");
        if (r.Type == ContactType.Email && !Normalizer.IsValidEmailSyntax(r.Value)) throw new ValidationFailedException("Invalid email address.");
        if (r.Type is ContactType.Phone or ContactType.WhatsApp && !Normalizer.IsValidPhoneSyntax(r.Value)) throw new ValidationFailedException("Invalid phone number.");
        if (r.IsPrimary) foreach (var c in lead.Contacts.Where(c => c.Type == r.Type)) c.IsPrimary = false;
        var contact = AttachContact(lead, r.Type, r.Value, r.Label, "manual", r.SourceUrl, r.IsPrimary || lead.Contacts.All(c => c.Type != r.Type));
        db.LeadContacts.Add(contact);
        await db.SaveChangesAsync(ct);
        return new ContactDto(contact.Id, contact.Type, contact.Value, contact.Label, contact.IsPrimary, contact.SourceProvider, contact.SourceUrl,
            contact.SourceRecordedAt, contact.SourceVerified, contact.SyntaxVerified, contact.DeliveryVerified, contact.ConsentVerified, false);
    }

    public async Task RemoveContactAsync(Guid leadId, Guid contactId, CancellationToken ct)
    {
        var contact = await db.LeadContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.LeadId == leadId, ct) ?? throw new NotFoundException("Contact", contactId);
        db.LeadContacts.Remove(contact);
        var lead = await db.Leads.FirstAsync(l => l.Id == leadId, ct);
        if (contact.Type == ContactType.Email && lead.Email == contact.NormalizedValue) lead.Email = null;
        if (contact.Type == ContactType.Phone && lead.NormalizedPhone == contact.NormalizedValue) { lead.Phone = null; lead.NormalizedPhone = null; }
        await db.SaveChangesAsync(ct);
    }

    public async Task DismissOpportunityAsync(Guid leadId, Guid opportunityId, CancellationToken ct)
    {
        var o = await db.Opportunities.FirstOrDefaultAsync(x => x.Id == opportunityId && x.LeadId == leadId, ct) ?? throw new NotFoundException("Opportunity", opportunityId);
        o.IsDismissed = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<DuplicateCandidate>> FindDuplicatesAsync(DuplicateProbe probe, Guid? excludeId, CancellationToken ct)
    {
        var domain = Normalizer.ExtractDomain(probe.WebsiteUrl);
        var phone = Normalizer.NormalizePhone(probe.Phone);
        var name = Normalizer.NormalizeName(probe.BusinessName);
        var firstToken = name.Split(' ').FirstOrDefault() ?? "";
        var candidates = await db.Leads.AsNoTracking()
            .Where(l => l.Id != excludeId)
            .Where(l => (probe.ProviderPlaceId != null && l.ProviderPlaceId == probe.ProviderPlaceId)
                        || (domain != null && l.WebsiteDomain == domain)
                        || (phone != null && l.NormalizedPhone == phone)
                        || (firstToken.Length > 2 && l.NormalizedName.StartsWith(firstToken)
                            && ((probe.City != null && l.City == probe.City) || (probe.PinCode != null && l.PinCode == probe.PinCode))))
            .Take(50).ToListAsync(ct);
        return candidates.Select(c => DuplicateDetector.Compare(probe, c)).OfType<DuplicateCandidate>().OrderByDescending(c => c.Score).ToList();
    }

    public async Task<List<DuplicatePairDto>> ListDuplicatePairsAsync(int max, CancellationToken ct)
    {
        var leads = await db.Leads.AsNoTracking().OrderBy(l => l.CreatedAt).Take(5000).ToListAsync(ct);
        var pairs = new List<DuplicatePairDto>();
        var byDomain = leads.Where(l => l.WebsiteDomain != null && !DuplicateDetector.IsSharedDomain(l.WebsiteDomain)).GroupBy(l => l.WebsiteDomain);
        var byPhone = leads.Where(l => l.NormalizedPhone != null).GroupBy(l => l.NormalizedPhone);
        var byCity = leads.Where(l => l.City != null).GroupBy(l => l.City!.ToLowerInvariant());
        var seen = new HashSet<(Guid, Guid)>();

        void AddPair(Lead a, Lead b)
        {
            var key = a.Id.CompareTo(b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
            if (!seen.Add(key)) return;
            var cand = DuplicateDetector.Compare(new DuplicateProbe(b.SourceProvider, b.ProviderPlaceId, b.BusinessName, b.WebsiteUrl, b.Phone, b.City, b.PinCode), a);
            if (cand is not null) pairs.Add(new DuplicatePairDto(a.Id, a.BusinessName, b.Id, b.BusinessName, cand.Score, cand.IsExact, cand.Reasons));
        }

        foreach (var g in byDomain.Concat(byPhone).Where(g => g.Count() > 1))
        {
            var list = g.ToList();
            for (var i = 1; i < list.Count; i++) AddPair(list[0], list[i]);
        }
        foreach (var g in byCity)
        {
            var list = g.ToList();
            for (var i = 0; i < list.Count && pairs.Count < max; i++)
            for (var j = i + 1; j < list.Count; j++)
                if (Normalizer.Similarity(list[i].NormalizedName, list[j].NormalizedName) >= DuplicateDetector.FuzzyThreshold) AddPair(list[i], list[j]);
        }
        return pairs.OrderByDescending(p => p.Score).Take(max).ToList();
    }

    /// <summary>Merges <paramref name="duplicateId"/> into <paramref name="targetId"/>, moving all related records.</summary>
    public async Task MergeAsync(Guid targetId, Guid duplicateId, CancellationToken ct)
    {
        if (targetId == duplicateId) throw new ValidationFailedException("Cannot merge a lead into itself.");
        var target = await db.Leads.Include(l => l.Contacts).FirstOrDefaultAsync(l => l.Id == targetId, ct) ?? throw new NotFoundException("Lead", targetId);
        var dup = await db.Leads.Include(l => l.Contacts).FirstOrDefaultAsync(l => l.Id == duplicateId, ct) ?? throw new NotFoundException("Lead", duplicateId);

        target.WebsiteUrl ??= dup.WebsiteUrl; target.WebsiteDomain ??= dup.WebsiteDomain; target.Phone ??= dup.Phone;
        target.NormalizedPhone ??= dup.NormalizedPhone; target.Email ??= dup.Email; target.AddressLine ??= dup.AddressLine;
        target.Locality ??= dup.Locality; target.City ??= dup.City; target.District ??= dup.District; target.State ??= dup.State;
        target.PinCode ??= dup.PinCode; target.CategoryId ??= dup.CategoryId; target.Rating ??= dup.Rating; target.ReviewCount ??= dup.ReviewCount;
        target.ProviderPlaceId ??= dup.ProviderPlaceId;

        foreach (var c in dup.Contacts.ToList())
        {
            if (target.Contacts.Any(t => t.Type == c.Type && t.NormalizedValue == c.NormalizedValue)) continue;
            c.LeadId = target.Id;
            c.IsPrimary = false;
        }
        await db.LeadSources.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.Opportunities.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.CrmActivities.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.FollowUps.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.Meetings.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.Proposals.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.Deals.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.PitchDrafts.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.EmailDrafts.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.WebsiteAnalyses.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.AIAnalyses.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);
        await db.LeadScores.Where(x => x.LeadId == duplicateId).ForEachAsync(x => x.LeadId = targetId, ct);

        dup.IsDeleted = true;
        dup.DeletedAt = clock.UtcNow;
        dup.DuplicateOfLeadId = targetId;
        db.CrmActivities.Add(Activity.Create(targetId, ActivityType.System, "Lead merged", $"Merged duplicate '{dup.BusinessName}'", currentUser.UserId));
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("lead.merge", nameof(Lead), targetId.ToString(), $"merged={duplicateId}", ct);
    }

    // ---------------- Categories ----------------

    public async Task<List<CategoryDto>> ListCategoriesAsync(bool includeInactive, CancellationToken ct) =>
        await db.LeadCategories.AsNoTracking().Where(c => includeInactive || c.IsActive).OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.SearchKeywords, c.RelevantServices, c.RelevanceScore, c.IsActive, c.IsSystem))
            .ToListAsync(ct);

    public async Task<CategoryDto> UpsertCategoryAsync(Guid? id, UpsertCategoryRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100) throw new ValidationFailedException("Category name is required (max 100 characters).");
        if (r.RelevanceScore is < 0 or > 100) throw new ValidationFailedException("Relevance must be 0-100.");
        LeadCategory c;
        if (id is null)
        {
            var slug = Normalizer.Slugify(r.Name);
            if (await db.LeadCategories.AnyAsync(x => x.Slug == slug, ct)) throw new ConflictException("Category already exists.");
            c = new LeadCategory { Slug = slug };
            db.LeadCategories.Add(c);
        }
        else
        {
            c = await db.LeadCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Category", id);
        }
        c.Name = r.Name.Trim(); c.Description = r.Description; c.SearchKeywords = r.SearchKeywords;
        c.RelevantServices = r.RelevantServices; c.RelevanceScore = r.RelevanceScore; c.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.Slug, c.Description, c.SearchKeywords, c.RelevantServices, c.RelevanceScore, c.IsActive, c.IsSystem);
    }
}
