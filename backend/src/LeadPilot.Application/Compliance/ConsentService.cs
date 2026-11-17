using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Application.Compliance;

public sealed record SuppressionDto(Guid Id, CommunicationChannel Channel, string Value, string Reason, string? Source, DateTime CreatedAt);
public sealed record AddSuppressionRequest(CommunicationChannel Channel, string Value, string Reason, Guid? LeadId);
public sealed record ConsentDto(Guid Id, Guid? LeadId, CommunicationChannel Channel, string ContactValue, string Purpose, string Source, string? Evidence, DateTime GrantedAt, DateTime? RevokedAt);
public sealed record RecordConsentRequest(Guid? LeadId, CommunicationChannel Channel, string ContactValue, string Purpose, string Source, string? Evidence);

public sealed class ConsentService(IAppDbContext db, ISettingsService settings, IAuditService audit, IClock clock) : IConsentService
{
    public static string Normalize(CommunicationChannel channel, string value) => channel switch
    {
        CommunicationChannel.Email => Normalizer.NormalizeEmail(value) ?? "",
        CommunicationChannel.WhatsApp or CommunicationChannel.Phone or CommunicationChannel.Sms => Normalizer.NormalizePhone(value) ?? value.Trim(),
        _ => value.Trim().ToLowerInvariant()
    };

    public async Task<bool> IsSuppressedAsync(CommunicationChannel channel, string value, CancellationToken ct)
    {
        var n = Normalize(channel, value);
        var domain = channel == CommunicationChannel.Email && n.Contains('@') ? "@" + n.Split('@')[1] : null;
        return await db.SuppressionList.AnyAsync(s => s.Channel == channel && (s.NormalizedValue == n || (domain != null && s.NormalizedValue == domain)), ct);
    }

    public async Task<bool> HasConsentAsync(CommunicationChannel channel, string value, string? purpose, CancellationToken ct)
    {
        var n = Normalize(channel, value);
        return await db.ConsentRecords.AnyAsync(c => c.Channel == channel && c.NormalizedValue == n && c.RevokedAt == null && (purpose == null || c.Purpose == purpose), ct);
    }

    public async Task<EligibilityResult> CheckEligibilityAsync(Lead lead, CommunicationChannel channel, string recipient, CancellationToken ct)
    {
        var reasons = new List<string>();
        if (await IsSuppressedAsync(channel, recipient, ct)) reasons.Add("Recipient is on the suppression (opt-out) list.");
        if (lead.Stage == PipelineStage.NotInterested) reasons.Add("Lead is marked Not Interested.");
        if (lead.IsDeleted) reasons.Add("Lead has been deleted.");
        if (channel == CommunicationChannel.WhatsApp && !await HasConsentAsync(channel, recipient, null, ct))
            reasons.Add("WhatsApp business-initiated messages require a recorded opt-in. A public phone number is not consent.");
        if (channel == CommunicationChannel.Email)
        {
            var outreach = await settings.GetAsync<OutreachSettings>(OutreachSettings.Key, ct);
            if (outreach.RequireEmailConsent && !await HasConsentAsync(channel, recipient, null, ct))
                reasons.Add("Settings require recorded consent before emailing, and none exists for this address.");
        }
        return new EligibilityResult(reasons.Count == 0, reasons);
    }

    public async Task<List<SuppressionDto>> ListSuppressionsAsync(CancellationToken ct) =>
        await db.SuppressionList.AsNoTracking().OrderByDescending(s => s.CreatedAt)
            .Select(s => new SuppressionDto(s.Id, s.Channel, s.Value, s.Reason, s.Source, s.CreatedAt)).ToListAsync(ct);

    public async Task<SuppressionDto> AddSuppressionAsync(AddSuppressionRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Value)) throw new ValidationFailedException("Value is required.");
        var n = r.Value.StartsWith('@') ? r.Value.Trim().ToLowerInvariant() : Normalize(r.Channel, r.Value);
        var existing = await db.SuppressionList.FirstOrDefaultAsync(s => s.Channel == r.Channel && s.NormalizedValue == n, ct);
        if (existing is null)
        {
            existing = new SuppressionEntry { Channel = r.Channel, Value = r.Value.Trim(), NormalizedValue = n, Reason = r.Reason, Source = "manual", LeadId = r.LeadId };
            db.SuppressionList.Add(existing);
            // Cancel anything queued to this recipient.
            if (r.Channel == CommunicationChannel.Email)
            {
                var drafts = await db.EmailDrafts.Where(d => d.ToAddress == n && (d.Status == EmailStatus.Approved || d.Status == EmailStatus.Queued || d.Status == EmailStatus.PendingApproval)).ToListAsync(ct);
                foreach (var d in drafts) { d.Status = EmailStatus.Rejected; d.RejectionReason = "Recipient opted out"; }
            }
            await db.SaveChangesAsync(ct);
            await audit.LogAsync("suppression.add", nameof(SuppressionEntry), existing.Id.ToString(), $"channel={r.Channel}", ct);
        }
        return new SuppressionDto(existing.Id, existing.Channel, existing.Value, existing.Reason, existing.Source, existing.CreatedAt);
    }

    public async Task RemoveSuppressionAsync(Guid id, CancellationToken ct)
    {
        var s = await db.SuppressionList.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Suppression", id);
        db.SuppressionList.Remove(s);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("suppression.remove", nameof(SuppressionEntry), id.ToString(), null, ct);
    }

    public async Task<List<ConsentDto>> ListConsentsAsync(Guid? leadId, CancellationToken ct) =>
        await db.ConsentRecords.AsNoTracking().Where(c => leadId == null || c.LeadId == leadId).OrderByDescending(c => c.GrantedAt)
            .Select(c => new ConsentDto(c.Id, c.LeadId, c.Channel, c.ContactValue, c.Purpose, c.Source, c.Evidence, c.GrantedAt, c.RevokedAt)).ToListAsync(ct);

    public async Task<ConsentDto> RecordConsentAsync(RecordConsentRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Source) || string.IsNullOrWhiteSpace(r.Purpose)) throw new ValidationFailedException("Consent source and purpose are required.");
        var c = new ConsentRecord
        {
            LeadId = r.LeadId, Channel = r.Channel, ContactValue = r.ContactValue.Trim(), NormalizedValue = Normalize(r.Channel, r.ContactValue),
            Purpose = r.Purpose, Source = r.Source, Evidence = r.Evidence, GrantedAt = clock.UtcNow
        };
        db.ConsentRecords.Add(c);
        if (r.LeadId is not null)
        {
            var contacts = await db.LeadContacts.Where(x => x.LeadId == r.LeadId && x.NormalizedValue == c.NormalizedValue).ToListAsync(ct);
            foreach (var x in contacts) x.ConsentVerified = true;
        }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("consent.record", nameof(ConsentRecord), c.Id.ToString(), $"channel={r.Channel} purpose={r.Purpose}", ct);
        return new ConsentDto(c.Id, c.LeadId, c.Channel, c.ContactValue, c.Purpose, c.Source, c.Evidence, c.GrantedAt, c.RevokedAt);
    }

    public async Task RevokeConsentAsync(Guid id, CancellationToken ct)
    {
        var c = await db.ConsentRecords.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Consent", id);
        c.RevokedAt = clock.UtcNow;
        var contacts = await db.LeadContacts.Where(x => x.NormalizedValue == c.NormalizedValue).ToListAsync(ct);
        foreach (var x in contacts) x.ConsentVerified = false;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("consent.revoke", nameof(ConsentRecord), id.ToString(), null, ct);
    }
}
