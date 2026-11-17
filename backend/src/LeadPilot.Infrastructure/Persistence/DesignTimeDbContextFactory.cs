using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeadPilot.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` tooling. Reads LEADPILOT_DESIGN_CONNECTION or falls back to the local dev database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("LEADPILOT_DESIGN_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=leadpilot;Username=leadpilot;Password=devpassword";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options);
    }
}
