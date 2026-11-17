using System.Linq.Expressions;
using System.Text.Json;
using Hangfire;
using LeadPilot.Application.Common;
using LeadPilot.Application.Settings;
using LeadPilot.Domain.Entities;
using LeadPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeadPilot.Infrastructure.Services;

public sealed class SettingsService(AppDbContext db) : ISettingsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : new()
    {
        var row = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) return new T();
        try { return JsonSerializer.Deserialize<T>(row.ValueJson, Json) ?? new T(); }
        catch (JsonException) { return new T(); }
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken ct = default)
    {
        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        var json = JsonSerializer.Serialize(value, Json);
        if (row is null) db.SystemSettings.Add(new SystemSetting { Key = key, ValueJson = json });
        else row.ValueJson = json;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Writes audit rows through a separate DbContext scope so audit entries are not lost if the caller's unit of work fails.</summary>
public sealed class AuditService(IServiceScopeFactory scopes, ICurrentUser currentUser, ILogger<AuditService> logger) : IAuditService
{
    public async Task LogAsync(string action, string? entityType = null, string? entityId = null, string? details = null, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                UserId = currentUser.UserId, Action = action, EntityType = entityType, EntityId = entityId,
                Details = details is { Length: > 2000 } ? details[..2000] : details, IpAddress = currentUser.IpAddress
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write audit log for {Action}", action);
        }
    }
}

public sealed class UsageTracker(IServiceScopeFactory scopes, AppDbContext db) : IUsageTracker
{
    public async Task RecordAsync(UsageLog log, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.UsageLogs.Add(log);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<decimal> GetSpendAsync(string provider, DateTime fromUtc, CancellationToken ct = default) =>
        (await db.UsageLogs.AsNoTracking().Where(u => u.Provider == provider && u.CreatedAt >= fromUtc).Select(u => u.CostUsd).ToListAsync(ct)).Sum();
}

public sealed class HangfireJobScheduler(IBackgroundJobClient client) : IJobScheduler
{
    public string Enqueue<T>(Expression<Func<T, Task>> job) => client.Enqueue(job);
    public string Schedule<T>(Expression<Func<T, Task>> job, TimeSpan delay) => client.Schedule(job, delay);
    public bool Delete(string jobId) => client.Delete(jobId);
}

/// <summary>Used when Jobs:Enabled=false (tests). Records jobs instead of running them.</summary>
public sealed class RecordingJobScheduler : IJobScheduler
{
    public List<LambdaExpression> Enqueued { get; } = [];
    public string Enqueue<T>(Expression<Func<T, Task>> job) { lock (Enqueued) Enqueued.Add(job); return Guid.NewGuid().ToString("N"); }
    public string Schedule<T>(Expression<Func<T, Task>> job, TimeSpan delay) => Enqueue(job);
    public bool Delete(string jobId) => true;
}
