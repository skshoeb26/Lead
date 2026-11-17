using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Common;
using LeadPilot.Application.Settings;
using LeadPilot.Domain;
using LeadPilot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeadPilot.Application.Notifications;

public sealed record NotificationDto(Guid Id, NotificationType Type, string Title, string Body, string? DeepLink, DateTime CreatedAt, DateTime? ReadAt);
public sealed record RegisterDeviceRequest(string Token, DevicePlatform Platform = DevicePlatform.Android, string? DeviceName = null);

public sealed class NotificationService(
    IAppDbContext db,
    INotificationProvider push,
    ISettingsService settings,
    IJobScheduler jobs,
    IClock clock,
    ILogger<NotificationService> logger)
{
    /// <summary>
    /// Creates an in-app notification and queues a push. <paramref name="privateBody"/> (which may contain lead
    /// names) is only used in the push preview when the user has opted in; otherwise <paramref name="publicBody"/> is used.
    /// </summary>
    public async Task NotifyAsync(Guid userId, NotificationType type, string title, string publicBody, string? privateBody, string? deepLink, CancellationToken ct)
    {
        var n = new Notification { UserId = userId, Type = type, Title = title, Body = privateBody ?? publicBody, DeepLink = deepLink };
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);

        var s = await settings.GetAsync<NotificationSettings>(NotificationSettings.Key, ct);
        if (!s.PushEnabled || !IsTypeEnabled(s, type)) return;
        jobs.Enqueue<NotificationService>(svc => svc.SendPushAsync(n.Id, CancellationToken.None));
    }

    /// <summary>Notify the owner(s). Single-owner today; multi-user ready.</summary>
    public async Task NotifyOwnersAsync(NotificationType type, string title, string publicBody, string? privateBody, string? deepLink, CancellationToken ct)
    {
        var owners = await db.Users.Where(u => u.IsActive && (u.Role == Roles.Owner || u.Role == Roles.Admin)).Select(u => u.Id).ToListAsync(ct);
        foreach (var id in owners) await NotifyAsync(id, type, title, publicBody, privateBody, deepLink, ct);
    }

    public async Task SendPushAsync(Guid notificationId, CancellationToken ct)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == notificationId, ct);
        if (n is null || n.PushSentAt is not null) return;
        if (!push.IsConfigured)
        {
            n.PushError = "Push provider not configured";
            await db.SaveChangesAsync(ct);
            return;
        }
        var tokens = await db.DeviceTokens.Where(d => d.UserId == n.UserId && d.IsActive).ToListAsync(ct);
        if (tokens.Count == 0) return;

        var s = await settings.GetAsync<NotificationSettings>(NotificationSettings.Key, ct);
        var body = s.IncludeDetailsInPush ? n.Body : GenericBody(n.Type);
        var data = new Dictionary<string, string> { ["notificationId"] = n.Id.ToString(), ["type"] = n.Type.ToString(), ["route"] = n.DeepLink ?? "/notifications" };
        try
        {
            var invalid = await push.SendAsync(tokens.Select(t => t.Token).ToList(), new PushMessage(n.Title, body, data), ct);
            foreach (var t in tokens.Where(t => invalid.Contains(t.Token))) t.IsActive = false;
            n.PushSentAt = clock.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Push delivery failed for notification {NotificationId}", n.Id);
            n.PushError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
        }
        await db.SaveChangesAsync(ct);
    }

    public static string GenericBody(NotificationType type) => type switch
    {
        NotificationType.DiscoveryCompleted => "A lead discovery job has finished.",
        NotificationType.HighPriorityLeads => "New high-priority leads are ready to review.",
        NotificationType.AnalysisCompleted => "A lead analysis has finished.",
        NotificationType.ApprovalPending => "Email drafts are waiting for your approval.",
        NotificationType.EmailFailed => "An email could not be sent. Tap to review.",
        NotificationType.FollowUpDue => "You have follow-ups due.",
        NotificationType.ReplyRecorded => "A new reply was recorded.",
        NotificationType.JobError => "A background task needs attention.",
        NotificationType.BudgetWarning => "API usage is approaching your budget limit.",
        _ => "Open LeadPilot AI for details."
    };

    private static bool IsTypeEnabled(NotificationSettings s, NotificationType t) => t switch
    {
        NotificationType.DiscoveryCompleted => s.DiscoveryCompleted,
        NotificationType.HighPriorityLeads => s.HighPriorityLeads,
        NotificationType.AnalysisCompleted => s.AnalysisCompleted,
        NotificationType.ApprovalPending => s.ApprovalPending,
        NotificationType.EmailFailed => s.EmailFailed,
        NotificationType.FollowUpDue => s.FollowUpDue,
        NotificationType.JobError or NotificationType.BudgetWarning => s.JobErrors,
        _ => true
    };

    public async Task<PagedResult<NotificationDto>> ListAsync(Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == userId && (!unreadOnly || n.ReadAt == null));
        var total = await q.CountAsync(ct);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var items = await q.OrderByDescending(n => n.CreatedAt).Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.DeepLink, n.CreatedAt, n.ReadAt)).ToListAsync(ct);
        return new PagedResult<NotificationDto>(items, page, pageSize, total);
    }

    public async Task<int> UnreadCountAsync(Guid userId, CancellationToken ct) =>
        await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);

    public async Task MarkReadAsync(Guid userId, Guid? id, CancellationToken ct)
    {
        var q = db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null && (id == null || n.Id == id));
        foreach (var n in await q.ToListAsync(ct)) n.ReadAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> RegisterDeviceAsync(Guid userId, RegisterDeviceRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Token) || r.Token.Length > 4096) throw new ValidationFailedException("Invalid device token.");
        var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == r.Token, ct);
        if (existing is not null)
        {
            existing.UserId = userId; existing.IsActive = true; existing.LastSeenAt = clock.UtcNow;
            existing.Platform = r.Platform; existing.DeviceName = r.DeviceName;
        }
        else
        {
            existing = new DeviceToken { UserId = userId, Token = r.Token, Platform = r.Platform, DeviceName = r.DeviceName };
            db.DeviceTokens.Add(existing);
        }
        await db.SaveChangesAsync(ct);
        return existing.Id;
    }

    public async Task RemoveDeviceAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var d = await db.DeviceTokens.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct) ?? throw new NotFoundException("Device", id);
        db.DeviceTokens.Remove(d);
        await db.SaveChangesAsync(ct);
    }
}
