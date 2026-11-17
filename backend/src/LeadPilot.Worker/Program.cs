using Hangfire;
using LeadPilot.Application.Common;
using LeadPilot.Infrastructure;
using LeadPilot.Infrastructure.Jobs;
using Serilog;

// LeadPilot.Worker hosts the Hangfire server: discovery, website/AI analysis, scoring, email outbox,
// follow-up reminders, maintenance and budget monitoring. It runs 24/7 in the cloud, independent of any laptop or phone.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog((_, cfg) => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres")))
    throw new InvalidOperationException("ConnectionStrings:Postgres is required for the worker.");

builder.Services.AddLeadPilotCore(builder.Configuration, builder.Environment);
builder.Services.AddLeadPilotHangfireServer(builder.Configuration);

var host = builder.Build();
var jobs = builder.Configuration.GetSection(JobsOptions.Section).Get<JobsOptions>() ?? new JobsOptions();
RecurringJobs.Register(host.Services.GetRequiredService<IRecurringJobManager>(), jobs);
Log.Information("LeadPilot worker started with {Workers} workers", jobs.WorkerCount);
host.Run();
