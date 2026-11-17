using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using LeadPilot.API.Infrastructure;
using LeadPilot.Application.Auth;
using LeadPilot.Application.Common;
using LeadPilot.Infrastructure;
using LeadPilot.Infrastructure.Jobs;
using LeadPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

var config = builder.Configuration;
var jwt = config.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException("Jwt:SigningKey must be set to at least 32 bytes (env Jwt__SigningKey).");
    jwt.SigningKey = "development-only-signing-key-change-me-0123456789";
    builder.Configuration["Jwt:SigningKey"] = jwt.SigningKey;
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddLeadPilotCore(config, builder.Environment);
var jobsOptions = config.GetSection(JobsOptions.Section).Get<JobsOptions>() ?? new JobsOptions();
if (jobsOptions.Enabled && jobsOptions.RunServerInApi) builder.Services.AddLeadPilotHangfireServer(config);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        RoleClaimType = System.Security.Claims.ClaimTypes.Role, NameClaimType = "name"
    };
});
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy("Owner", p => p.RequireRole(LeadPilot.Domain.Roles.Owner, LeadPilot.Domain.Roles.Admin));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimiting:AuthPerMinute", 10), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("expensive", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimiting:ExpensivePerMinute", 30), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("RateLimiting:GlobalPerMinute", 300), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var cors = config.GetSection(CorsOptions.Section).Get<CorsOptions>() ?? new CorsOptions();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(cors.AllowedOrigins).AllowAnyHeader()
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE").WithExposedHeaders("Content-Disposition")));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "LeadPilot AI API", Version = "v1", Description = "AI-powered client acquisition platform API" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "JWT access token"
    });
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
    c.CustomSchemaIds(t => t.FullName?.Replace("LeadPilot.Application.", "").Replace("+", "."));
});

var app = builder.Build();

// ---------------- database migration + seed (configurable)
if (config.GetValue("Database:MigrateOnStartup", true) && !string.IsNullOrWhiteSpace(config.GetConnectionString("Postgres")))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational()) await db.Database.MigrateAsync();
    else await db.Database.EnsureCreatedAsync();
    await DbSeeder.SeedAsync(db);
    await scope.ServiceProvider.GetRequiredService<AuthService>().EnsureBootstrapOwnerAsync(CancellationToken.None);
}

if (jobsOptions.Enabled && jobsOptions.RunServerInApi && !string.IsNullOrWhiteSpace(config.GetConnectionString("Postgres")))
    RecurringJobs.Register(app.Services.GetRequiredService<IRecurringJobManager>(), jobsOptions);

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSecurityHeaders();
if (!app.Environment.IsDevelopment() && config.GetValue("Https:Redirect", false)) { app.UseHsts(); app.UseHttpsRedirection(); }
if (config.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/swagger/v1/swagger.json", "LeadPilot AI v1"));
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/", () => Results.Ok(new { name = "LeadPilot AI API", status = "ok" })).AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;
