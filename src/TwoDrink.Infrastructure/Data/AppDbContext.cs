using Microsoft.EntityFrameworkCore;

namespace TwoDrink.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
