using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TwoDrink.Infrastructure.Data;

// Used only by `dotnet ef` tooling; the connection string is a placeholder (no database is contacted).
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=twodrink_design")
            .UseSnakeCaseNamingConvention()
            .Options);
}
