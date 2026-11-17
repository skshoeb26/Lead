using LeadPilot.Application.Common;
using LeadPilot.Application.Notifications;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadPilot.Application.Crm;

public static class Activity
{
    public static CrmActivity Create(Guid leadId, ActivityType type, string title, string? description = null, Guid? actor = null, string? metadataJson = null) =>
        new() { LeadId = leadId, Type = type, Title = title, Description = description, ActorUserId = actor, MetadataJson = metadataJson };
}

public sealed record ActivityDto(Guid Id, Guid LeadId, string? LeadName, ActivityType Type, string Title, string? Description, DateTime OccurredAt);
public sealed record CreateActivityRequest(Guid LeadId, ActivityType Type, string Title, string? Description, DateTime? OccurredAt);
public sealed record ChangeStageRequest(PipelineStage Stage, string? Note, string? LostReason, decimal? ActualValue);
public sealed record PipelineColumn(PipelineStage Stage, int Count, decimal ExpectedValue, IReadOnlyList<PipelineCard> Leads);
public sealed record PipelineCard(Guid Id, string BusinessName, string? City, int? Score, LeadPriority? Priority, decimal? ExpectedDealValue, DateTime? NextFollowUpAt);

public sealed record FollowUpDto(Guid Id, Guid LeadId, string LeadName, DateTime DueAt, FollowUpStatus Status, CommunicationChannel Channel, string? Note, int SequenceStep, Guid? EmailDraftId, DateTime? CompletedAt, bool IsOverdue);
public sealed record CreateFollowUpRequest(Guid LeadId, DateTime DueAt, CommunicationChannel Channel = CommunicationChannel.Email, string? Note = null);
public sealed record UpdateFollowUpRequest(FollowUpStatus? Status, DateTime? DueAt, string? Note, int? SnoozeHours);

public sealed record MeetingDto(Guid Id, Guid LeadId, string LeadName, DateTime ScheduledAt, int DurationMinutes, string? Location, string? Agenda, string? Outcome, MeetingStatus Status);
public sealed record UpsertMeetingRequest(Guid LeadId, DateTime ScheduledAt, int DurationMinutes, string? Location, string? Agenda, string? Outcome, MeetingStatus Status = MeetingStatus.Scheduled);

public sealed record ProposalDto(Guid Id, Guid LeadId, string LeadName, string Title, decimal Amount, string Currency, string? Content, ProposalStatus Status, DateTime? SentAt, DateTime? ValidUntil);
public sealed record UpsertProposalRequest(Guid LeadId, string Title, decimal Amount, string? Content, ProposalStatus Status = ProposalStatus.Draft, DateTime? ValidUntil = null);

public sealed record DealDto(Guid Id, Guid LeadId, string LeadName, string Title, decimal? ExpectedValue, decimal? ActualValue, DealStatus Status, DateTime? ClosedAt, string? LostReason);
public sealed record UpsertDealRequest(Guid LeadId, string Title, decimal? ExpectedValue, decimal? ActualValue, DealStatus Status = DealStatus.Open, string? LostReason = null);

public sealed class CrmService(IAppDbContext db, ICurrentUser currentUser, IClock clock, ISettingsService settings, IAuditService audit)
{
    public async Task<List<PipelineColumn>> GetPipelineAsync(int perStage, CancellationToken ct)
    {
        var leads = await db.Leads.AsNoTracking()
            .Select(l => new { l.Id, l.BusinessName, l.City, l.Score, l.Priority, l.ExpectedDealValue, l.NextFollowUpAt, l.Stage, l.UpdatedAt })
            .ToListAsync(ct);
        return Enum.GetValues<PipelineStage>().Select(stage =>
        {
            var inStage = leads.Where(l => l.Stage == stage).ToList();
            return new PipelineColumn(stage, inStage.Count, inStage.Sum(l => l.ExpectedDealValue ?? 0),
                inStage.OrderByDescending(l => l.Score ?? -1).ThenByDescending(l => l.UpdatedAt).Take(perStage)
                    .Select(l => new PipelineCard(l.Id, l.BusinessName, l.City, l.Score, l.Priority, l.ExpectedDealValue, l.NextFollowUpAt)).ToList());
        }).ToList();
    }

    public async Task ChangeStageAsync(Guid leadId, ChangeStageRequest r, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct) ?? throw new NotFoundException("Lead", leadId);
        if (lead.Stage == r.Stage) return;
        var previous = lead.Stage;
        lead.Stage = r.Stage;
        if (r.Stage is PipelineStage.Lost or PipelineStage.NotInterested) lead.LostReason = r.LostReason ?? lead.LostReason;
        if (r.Stage == PipelineStage.Won && r.ActualValue is not null) lead.ActualDealValue = r.ActualValue;
        if (r.Stage is PipelineStage.Won or PipelineStage.Lost or PipelineStage.NotInterested)
        {
            var pending = await db.FollowUps.Where(f => f.LeadId == leadId && f.Status == FollowUpStatus.Pending).ToListAsync(ct);
            foreach (var f in pending) f.Status = FollowUpStatus.Cancelled;
            lead.NextFollowUpAt = null;
            var deal = await db.Deals.Where(d => d.LeadId == leadId && d.Status == DealStatus.Open).FirstOrDefaultAsync(ct);
            if (deal is not null)
            {
                deal.Status = r.Stage == PipelineStage.Won ? DealStatus.Won : DealStatus.Lost;
                deal.ClosedAt = clock.UtcNow;
                deal.ActualValue = r.ActualValue ?? deal.ActualValue;
                deal.LostReason = r.LostReason;
            }
        }
        db.CrmActivities.Add(Activity.Create(leadId, ActivityType.StageChange, $"Stage: {previous} → {r.Stage}", r.Note, currentUser.UserId));
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("crm.stage_change", nameof(Lead), leadId.ToString(), $"{previous}->{r.Stage}", ct);
    }

    public async Task<ActivityDto> AddActivityAsync(CreateActivityRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 200) throw new ValidationFailedException("Title is required (max 200 characters).");
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == r.LeadId, ct) ?? throw new NotFoundException("Lead", r.LeadId);
        var a = Activity.Create(r.LeadId, r.Type, r.Title.Trim(), r.Description, currentUser.UserId);
        a.OccurredAt = r.OccurredAt ?? clock.UtcNow;
        db.CrmActivities.Add(a);
        // Only reliably-known events move the pipeline: a manually recorded reply moves Contacted -> Replied.
        if (r.Type == ActivityType.EmailReceived && lead.Stage is PipelineStage.Contacted or PipelineStage.FollowUpRequired)
        {
            lead.Stage = PipelineStage.Replied;
            db.CrmActivities.Add(Activity.Create(lead.Id, ActivityType.StageChange, "Stage: → Replied", "Reply recorded", currentUser.UserId));
            var pending = await db.FollowUps.Where(f => f.LeadId == lead.Id && f.Status == FollowUpStatus.Pending).ToListAsync(ct);
            foreach (var f in pending) f.Status = FollowUpStatus.Cancelled;
            lead.NextFollowUpAt = null;
        }
        if (r.Type is ActivityType.Call or ActivityType.EmailSent or ActivityType.WhatsApp or ActivityType.LinkedIn) lead.LastContactedAt = a.OccurredAt;
        await db.SaveChangesAsync(ct);
        return new ActivityDto(a.Id, a.LeadId, lead.BusinessName, a.Type, a.Title, a.Description, a.OccurredAt);
    }

    public async Task<PagedResult<ActivityDto>> ListActivitiesAsync(Guid? leadId, int page, int pageSize, CancellationToken ct)
    {
        var q = db.CrmActivities.AsNoTracking().Where(a => leadId == null || a.LeadId == leadId);
        var total = await q.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var items = await q.OrderByDescending(a => a.OccurredAt).Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize)
            .Select(a => new ActivityDto(a.Id, a.LeadId, a.Lead != null ? a.Lead.BusinessName : null, a.Type, a.Title, a.Description, a.OccurredAt))
            .ToListAsync(ct);
        return new PagedResult<ActivityDto>(items, page, pageSize, total);
    }

    // ---------------- Follow-ups ----------------

    public async Task<List<FollowUpDto>> ListFollowUpsAsync(string? bucket, Guid? leadId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var todayEnd = now.Date.AddDays(1);
        var q = db.FollowUps.AsNoTracking().Where(f => leadId == null || f.LeadId == leadId);
        q = bucket switch
        {
            "today" => q.Where(f => f.Status == FollowUpStatus.Pending && f.DueAt >= now.Date && f.DueAt < todayEnd),
            "overdue" => q.Where(f => f.Status == FollowUpStatus.Pending && f.DueAt < now),
            "upcoming" => q.Where(f => f.Status == FollowUpStatus.Pending && f.DueAt >= todayEnd),
            "completed" => q.Where(f => f.Status == FollowUpStatus.Completed),
            "pending" => q.Where(f => f.Status == FollowUpStatus.Pending),
            _ => q
        };
        return await q.OrderBy(f => f.DueAt).Take(500)
            .Select(f => new FollowUpDto(f.Id, f.LeadId, f.Lead != null ? f.Lead.BusinessName : "", f.DueAt, f.Status, f.Channel, f.Note,
                f.SequenceStep, f.EmailDraftId, f.CompletedAt, f.Status == FollowUpStatus.Pending && f.DueAt < now))
            .ToListAsync(ct);
    }

    public async Task<Guid> CreateFollowUpAsync(CreateFollowUpRequest r, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == r.LeadId, ct) ?? throw new NotFoundException("Lead", r.LeadId);
        var f = new FollowUp { LeadId = r.LeadId, DueAt = DateTime.SpecifyKind(r.DueAt, DateTimeKind.Utc), Channel = r.Channel, Note = r.Note };
        db.FollowUps.Add(f);
        if (lead.NextFollowUpAt is null || f.DueAt < lead.NextFollowUpAt) lead.NextFollowUpAt = f.DueAt;
        db.CrmActivities.Add(Activity.Create(lead.Id, ActivityType.FollowUp, "Follow-up scheduled", $"Due {f.DueAt:u}", currentUser.UserId));
        await db.SaveChangesAsync(ct);
        return f.Id;
    }

    /// <summary>Creates the configured follow-up sequence (e.g. +3, +7, +14 days) after an email is sent.</summary>
    public async Task ScheduleSequenceAsync(Guid leadId, DateTime from, CancellationToken ct)
    {
        var s = await settings.GetAsync<OutreachSettings>(OutreachSettings.Key, ct);
        if (!s.AutoCreateFollowUps) return;
        var existing = await db.FollowUps.AnyAsync(f => f.LeadId == leadId && f.Status == FollowUpStatus.Pending, ct);
        if (existing) return;
        var lead = await db.Leads.FirstAsync(l => l.Id == leadId, ct);
        var step = 0;
        foreach (var days in s.FollowUpDays.Where(d => d > 0).OrderBy(d => d))
            db.FollowUps.Add(new FollowUp { LeadId = leadId, DueAt = from.AddDays(days), SequenceStep = ++step, Note = $"Follow-up #{step} (day {days})" });
        lead.NextFollowUpAt = s.FollowUpDays.Where(d => d > 0).Select(d => (DateTime?)from.AddDays(d)).Min();
    }

    public async Task UpdateFollowUpAsync(Guid id, UpdateFollowUpRequest r, CancellationToken ct)
    {
        var f = await db.FollowUps.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Follow-up", id);
        if (r.SnoozeHours is > 0)
        {
            f.DueAt = clock.UtcNow.AddHours(r.SnoozeHours.Value);
            f.Status = FollowUpStatus.Pending;
            f.ReminderSentAt = null;
        }
        if (r.DueAt is not null) { f.DueAt = DateTime.SpecifyKind(r.DueAt.Value, DateTimeKind.Utc); f.ReminderSentAt = null; }
        if (r.Note is not null) f.Note = r.Note;
        if (r.Status is not null && r.Status != f.Status)
        {
            f.Status = r.Status.Value;
            if (f.Status == FollowUpStatus.Completed)
            {
                f.CompletedAt = clock.UtcNow;
                db.CrmActivities.Add(Activity.Create(f.LeadId, ActivityType.FollowUp, "Follow-up completed", f.Note, currentUser.UserId));
            }
        }
        await db.SaveChangesAsync(ct);
        await RecalculateNextFollowUpAsync(f.LeadId, ct);
    }

    public async Task RecalculateNextFollowUpAsync(Guid leadId, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead is null) return;
        lead.NextFollowUpAt = await db.FollowUps.Where(f => f.LeadId == leadId && f.Status == FollowUpStatus.Pending)
            .OrderBy(f => f.DueAt).Select(f => (DateTime?)f.DueAt).FirstOrDefaultAsync(ct);
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Meetings / proposals / deals ----------------

    public async Task<List<MeetingDto>> ListMeetingsAsync(Guid? leadId, bool upcomingOnly, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return await db.Meetings.AsNoTracking().Where(m => (leadId == null || m.LeadId == leadId) && (!upcomingOnly || m.ScheduledAt >= now))
            .OrderBy(m => m.ScheduledAt)
            .Select(m => new MeetingDto(m.Id, m.LeadId, m.Lead != null ? m.Lead.BusinessName : "", m.ScheduledAt, m.DurationMinutes, m.Location, m.Agenda, m.Outcome, m.Status))
            .ToListAsync(ct);
    }

    public async Task<Guid> UpsertMeetingAsync(Guid? id, UpsertMeetingRequest r, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == r.LeadId, ct) ?? throw new NotFoundException("Lead", r.LeadId);
        Meeting m;
        if (id is null)
        {
            m = new Meeting { LeadId = r.LeadId };
            db.Meetings.Add(m);
            if (lead.Stage < PipelineStage.MeetingScheduled || lead.Stage == PipelineStage.FollowUpRequired) lead.Stage = PipelineStage.MeetingScheduled;
            db.CrmActivities.Add(Activity.Create(lead.Id, ActivityType.Meeting, "Meeting scheduled", $"{r.ScheduledAt:u} {r.Location}", currentUser.UserId));
        }
        else m = await db.Meetings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Meeting", id);
        m.ScheduledAt = DateTime.SpecifyKind(r.ScheduledAt, DateTimeKind.Utc); m.DurationMinutes = r.DurationMinutes;
        m.Location = r.Location; m.Agenda = r.Agenda; m.Outcome = r.Outcome; m.Status = r.Status;
        await db.SaveChangesAsync(ct);
        return m.Id;
    }

    public async Task<List<ProposalDto>> ListProposalsAsync(Guid? leadId, CancellationToken ct) =>
        await db.Proposals.AsNoTracking().Where(p => leadId == null || p.LeadId == leadId).OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProposalDto(p.Id, p.LeadId, p.Lead != null ? p.Lead.BusinessName : "", p.Title, p.Amount, p.Currency, p.Content, p.Status, p.SentAt, p.ValidUntil))
            .ToListAsync(ct);

    public async Task<Guid> UpsertProposalAsync(Guid? id, UpsertProposalRequest r, CancellationToken ct)
    {
        if (r.Amount < 0) throw new ValidationFailedException("Amount cannot be negative.");
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == r.LeadId, ct) ?? throw new NotFoundException("Lead", r.LeadId);
        Proposal p;
        if (id is null) { p = new Proposal { LeadId = r.LeadId }; db.Proposals.Add(p); }
        else p = await db.Proposals.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Proposal", id);
        var wasSent = p.Status == ProposalStatus.Sent;
        p.Title = r.Title; p.Amount = r.Amount; p.Content = r.Content; p.Status = r.Status; p.ValidUntil = r.ValidUntil;
        if (r.Status == ProposalStatus.Sent && !wasSent)
        {
            p.SentAt = clock.UtcNow;
            if (lead.Stage < PipelineStage.ProposalSent || lead.Stage == PipelineStage.FollowUpRequired) lead.Stage = PipelineStage.ProposalSent;
            lead.ExpectedDealValue ??= r.Amount;
            db.CrmActivities.Add(Activity.Create(lead.Id, ActivityType.Proposal, "Proposal sent", $"{r.Title} — ₹{r.Amount:N0}", currentUser.UserId));
        }
        await db.SaveChangesAsync(ct);
        return p.Id;
    }

    public async Task<List<DealDto>> ListDealsAsync(Guid? leadId, CancellationToken ct) =>
        await db.Deals.AsNoTracking().Where(d => leadId == null || d.LeadId == leadId).OrderByDescending(d => d.CreatedAt)
            .Select(d => new DealDto(d.Id, d.LeadId, d.Lead != null ? d.Lead.BusinessName : "", d.Title, d.ExpectedValue, d.ActualValue, d.Status, d.ClosedAt, d.LostReason))
            .ToListAsync(ct);

    public async Task<Guid> UpsertDealAsync(Guid? id, UpsertDealRequest r, CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == r.LeadId, ct) ?? throw new NotFoundException("Lead", r.LeadId);
        Deal d;
        if (id is null) { d = new Deal { LeadId = r.LeadId }; db.Deals.Add(d); }
        else d = await db.Deals.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Deal", id);
        d.Title = r.Title; d.ExpectedValue = r.ExpectedValue; d.ActualValue = r.ActualValue; d.LostReason = r.LostReason;
        if (d.Status != r.Status)
        {
            d.Status = r.Status;
            d.ClosedAt = r.Status == DealStatus.Open ? null : clock.UtcNow;
            if (r.Status == DealStatus.Won) { lead.Stage = PipelineStage.Won; lead.ActualDealValue = r.ActualValue ?? lead.ActualDealValue; }
            if (r.Status == DealStatus.Lost) { lead.Stage = PipelineStage.Lost; lead.LostReason = r.LostReason; }
        }
        lead.ExpectedDealValue = r.ExpectedValue ?? lead.ExpectedDealValue;
        await db.SaveChangesAsync(ct);
        return d.Id;
    }
}
