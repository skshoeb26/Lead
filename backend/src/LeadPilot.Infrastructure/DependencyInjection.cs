using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Analysis;
using LeadPilot.Application.Auth;
using LeadPilot.Application.Common;
using LeadPilot.Application.Compliance;
using LeadPilot.Application.Crm;
using LeadPilot.Application.Discovery;
using LeadPilot.Application.Intelligence;
using LeadPilot.Application.Leads;
using LeadPilot.Application.Notifications;
using LeadPilot.Application.Outreach;
using LeadPilot.Application.Pitches;
using LeadPilot.Application.Reports;
using LeadPilot.Application.Settings;
using LeadPilot.Infrastructure.Gmail;
using LeadPilot.Infrastructure.Jobs;
using LeadPilot.Infrastructure.Persistence;
using LeadPilot.Infrastructure.Providers;
using LeadPilot.Infrastructure.Security;
using LeadPilot.Infrastructure.Services;
using LeadPilot.Infrastructure.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LeadPilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLeadPilotCore(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // ---------------- options
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.Section));
        services.Configure<ProvidersOptions>(config.GetSection(ProvidersOptions.Section));
        services.Configure<GooglePlacesOptions>(config.GetSection(GooglePlacesOptions.Section));
        services.Configure<PageSpeedOptions>(config.GetSection(PageSpeedOptions.Section));
        services.Configure<WebsiteAnalysisOptions>(config.GetSection(WebsiteAnalysisOptions.Section));
        services.Configure<AIOptions>(config.GetSection(AIOptions.Section));
        services.Configure<GoogleOAuthOptions>(config.GetSection(GoogleOAuthOptions.Section));
        services.Configure<EmailOptions>(config.GetSection(EmailOptions.Section));
        services.Configure<FirebaseOptions>(config.GetSection(FirebaseOptions.Section));
        services.Configure<JobsOptions>(config.GetSection(JobsOptions.Section));
        services.Configure<CorsOptions>(config.GetSection(CorsOptions.Section));

        var useMocks = config.GetValue<bool>("Providers:UseMocksWhenUnconfigured") && !env.IsProduction();
        var jobs = config.GetSection(JobsOptions.Section).Get<JobsOptions>() ?? new JobsOptions();
        var email = config.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions();

        // ---------------- persistence
        var connectionString = config.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(connectionString))
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, npg => npg.EnableRetryOnFailure(3).UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // ---------------- security
        var keysPath = config["DataProtection:KeysPath"] ?? Path.Combine(AppContext.BaseDirectory, "keys");
        Directory.CreateDirectory(keysPath);
        services.AddDataProtection().SetApplicationName("LeadPilot").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // ---------------- core services
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUsageTracker, UsageTracker>();
        services.AddSingleton<ILeadScoringService, LeadScoringEngine>();
        services.AddScoped<IConsentService, ConsentService>();
        services.AddScoped<ConsentService>();
        services.AddScoped<AuthService>();
        services.AddScoped<LeadService>();
        services.AddScoped<CsvLeadService>();
        services.AddScoped<CrmService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<DiscoveryJobRunner>();
        services.AddScoped<LeadAnalysisPipeline>();
        services.AddScoped<PitchService>();
        services.AddScoped<EmailService>();
        services.AddScoped<EmailOutboxProcessor>();
        services.AddScoped<FollowUpJob>();
        services.AddScoped<DashboardService>();
        services.AddScoped<MaintenanceJob>();
        services.AddScoped<BudgetMonitorJob>();
        services.AddValidatorsFromAssemblyContaining<RegisterValidator>();
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();

        // ---------------- HTTP clients
        services.AddHttpClient(SafeHttpFetcher.ClientName).ConfigurePrimaryHttpMessageHandler(UrlSafetyValidator.CreateHandler)
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(GooglePlacesProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(PageSpeedClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(config.GetValue("PageSpeed:TimeoutSeconds", 60)));
        services.AddHttpClient(GoogleOAuthService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient(GmailEmailProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(FcmNotificationProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient("whatsapp", c => c.Timeout = TimeSpan.FromSeconds(20));

        // ---------------- web analysis
        services.AddSingleton<IUrlSafetyValidator, UrlSafetyValidator>();
        services.AddScoped<SafeHttpFetcher>();
        services.AddScoped<IPageSpeedClient, PageSpeedClient>();
        services.AddScoped<IWebsiteAnalyzer, WebsiteAnalyzer>();

        // ---------------- discovery providers
        services.AddScoped<GooglePlacesProvider>();
        services.AddScoped<ILeadDiscoveryProvider>(sp => sp.GetRequiredService<GooglePlacesProvider>());
        services.AddScoped<IBusinessEnrichmentProvider>(sp => sp.GetRequiredService<GooglePlacesProvider>());
        if (useMocks) services.AddScoped<ILeadDiscoveryProvider, MockDiscoveryProvider>();

        // ---------------- AI provider
        var aiKey = config["AI:Claude:ApiKey"];
        var aiProvider = config["AI:Provider"] ?? "claude";
        if ((aiProvider == "mock" || string.IsNullOrWhiteSpace(aiKey)) && useMocks) services.AddScoped<IAIProvider, MockAIProvider>();
        else services.AddScoped<IAIProvider, ClaudeAIProvider>();

        // ---------------- email
        services.AddScoped<GoogleOAuthService>();
        if (email.TestMode) services.AddSingleton<IEmailProvider, TestModeEmailProvider>();
        else services.AddScoped<IEmailProvider, GmailEmailProvider>();

        // ---------------- messaging
        services.AddScoped<IWhatsAppProvider, WhatsAppCloudProvider>();
        services.AddSingleton<INotificationProvider, FcmNotificationProvider>();

        // ---------------- jobs
        if (jobs.Enabled && !string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHangfire(c => c
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
            services.AddScoped<IJobScheduler, HangfireJobScheduler>();
        }
        else
        {
            services.AddSingleton<RecordingJobScheduler>();
            services.AddSingleton<IJobScheduler>(sp => sp.GetRequiredService<RecordingJobScheduler>());
        }
        return services;
    }

    public static void AddLeadPilotHangfireServer(this IServiceCollection services, IConfiguration config)
    {
        var jobs = config.GetSection(JobsOptions.Section).Get<JobsOptions>() ?? new JobsOptions();
        services.AddHangfireServer(o =>
        {
            o.WorkerCount = Math.Max(1, jobs.WorkerCount);
            o.ServerName = $"leadpilot-{Environment.MachineName}";
            o.Queues = ["default"];
        });
    }
}

/// <summary>Current user for background jobs / worker processes (no HTTP context).</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? Role => "System";
    public string? IpAddress => null;
    public bool IsAuthenticated => false;
    public Guid RequireUserId() => throw new UnauthorizedException("No authenticated user.");
}
