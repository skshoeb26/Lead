using LeadPilot.Application.Analysis;
using LeadPilot.Application.Common;
using LeadPilot.Application.Notifications;
using LeadPilot.Application.Outreach;
using LeadPilot.Application.Pitches;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeadPilot.Application.Crm;

/// <summary>
/// Recurring job: notifies about due follow-ups and (optionally) prepares follow-up email drafts in Pending Approval.
/// Follow-ups are never sent automatically.
/// </summary>
public sealed class FollowUpJob(
    IAppDbContext db,
    ISettingsService settings,
    PitchService pitches,
    EmailService emails,
    LeadAnalysisPipeline pipeline,
    NotificationService notifications,
    IClock clock,
    ILogger<FollowUpJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.FollowUps.Include(f => f.Lead)
            .Where(f => f.Status == FollowUpStatus.Pending && f.DueAt <= now && f.ReminderSentAt == null)
            .OrderBy(f => f.DueAt).Take(50).ToListAsync(ct);
        if (due.Count == 0) return;

        var outreach = await settings.GetAsync<OutreachSettings>(OutreachSettings.Key, ct);
        var drafted = 0;
        foreach (var f in due)
        {
            f.ReminderSentAt = now;
            if (f.Lead is null || f.Lead.Stage is PipelineStage.Replied or PipelineStage.Won or PipelineStage.Lost or PipelineStage.NotInterested)
            {
                f.Status = FollowUpStatus.Cancelled;
                continue;
            }
            if (f.Lead.Stage == PipelineStage.Contacted) f.Lead.Stage = PipelineStage.FollowUpRequired;

            if (outreach.AutoDraftFollowUps && f.Channel == CommunicationChannel.Email && f.EmailDraftId is null && f.Lead.Email is not null)
            {
                try
                {
                    await pipeline.EnsureAIAllowedAsync(ct);
                    var pitch = await pitches.GenerateAsync(new GeneratePitchRequest(f.LeadId, PitchType.FollowUp), ct);
                    var draft = await emails.CreateDraftAsync(new CreateEmailDraftRequest(f.LeadId, pitch.Id, null, null, null, f.Id), ct);
                    f.EmailDraftId = draft.Id;
                    drafted++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogInformation("Follow-up draft not generated for {FollowUpId}: {Reason}", f.Id, ex.Message);
                }
            }
        }
        await db.SaveChangesAsync(ct);

        var ownerGroups = due.Where(f => f.Lead is not null && f.Status == FollowUpStatus.Pending).GroupBy(f => f.Lead!.OwnerUserId);
        foreach (var g in ownerGroups)
            await notifications.NotifyAsync(g.Key, NotificationType.FollowUpDue, "Follow-ups due",
                $"{g.Count()} follow-up(s) are due.", $"{g.Count()} follow-up(s) due{(drafted > 0 ? $", {drafted} draft(s) awaiting approval" : "")}.", "/followups", ct);
    }
}
