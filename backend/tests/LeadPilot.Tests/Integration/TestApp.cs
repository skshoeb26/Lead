using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadPilot.Application.Abstractions;
using LeadPilot.Application.Auth;
using LeadPilot.Domain.Entities;
using LeadPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeadPilot.Tests.Integration;

/// <summary>Controllable email provider. Never sends anything.</summary>
public sealed class FakeEmailProvider : IEmailProvider
{
    public string Name => "fake";
    public List<OutgoingEmail> Sent { get; } = [];
    public Queue<EmailSendResult> Results { get; } = new();

    public Task<EmailSendResult> SendAsync(EmailAccount account, OutgoingEmail email, CancellationToken ct)
    {
        lock (Sent) Sent.Add(email);
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new EmailSendResult(true, $"msg-{Sent.Count}", "thread-1", 200, null, false, false));
    }
}

public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    public FakeEmailProvider Email { get; } = new();
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public TestApp()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", "");
        builder.UseSetting("Jobs:Enabled", "false");
        builder.UseSetting("RateLimiting:AuthPerMinute", "1000");
        builder.UseSetting("RateLimiting:GlobalPerMinute", "10000");
        builder.UseSetting("Providers:UseMocksWhenUnconfigured", "true");
        builder.UseSetting("Email:TestMode", "true");
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(Path.GetTempPath(), "leadpilot-test-keys"));
        builder.UseSetting("GoogleOAuth:ClientId", "test-client");
        builder.UseSetting("GoogleOAuth:ClientSecret", "test-secret");
        builder.UseSetting("GoogleOAuth:RedirectUri", "https://api.test/api/v1/integrations/google/callback");
        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
            services.AddSingleton<IEmailProvider>(Email);
        });
    }

    public async Task InitAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedAsync(db);
    }

    public T Get<T>(IServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    public async Task<HttpClient> OwnerClientAsync()
    {
        var client = CreateClient();
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("owner@test.in", "Str0ngPassword!", "Owner"));
        HttpResponseMessage response = register.IsSuccessStatusCode
            ? register
            : await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("owner@test.in", "Str0ngPassword!", null));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection.Dispose();
    }
}
