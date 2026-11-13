using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TwoDrink.Infrastructure.Data;

// Used only by `dotnet ef` tooling; the connection string is a placeholder (no database is contacted).
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        builder.ConfigureApp("Host=localhost;Database=twodrink_design");
        return new AppDbContext(builder.Options);
    }
}
