using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rebelgent.Persistence;

/// <summary>
/// Allows EF Core design-time tools (migrations) to create a DbContext
/// when running outside of the application host.
/// </summary>
internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RebelgentDbContext>
{
    public RebelgentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RebelgentDbContext>()
            .UseSqlite("Data Source=data/rebelgent.db")
            .Options;

        return new RebelgentDbContext(options);
    }
}
