using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SupermarketSystem.Infrastructure.Services;

namespace SupermarketSystem.Infrastructure.Persistence;

/// <summary>
/// Used only by the `dotnet ef` tooling at design time. Without it, the CLI
/// would have to build and start the API host to obtain an AppDbContext,
/// which fails because AppDbContext has a second constructor parameter
/// (ICurrentUserContext) that the tooling cannot resolve on its own.
///
/// The connection string here is used ONLY to determine the provider and to
/// scaffold migrations — `dotnet ef migrations add` does not connect to a
/// database. `dotnet ef database update` uses the API project's
/// configuration instead.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=SupermarketSystem;Trusted_Connection=True;TrustServerCertificate=True",
            sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        return new AppDbContext(optionsBuilder.Options, new PlaceholderCurrentUserContext());
    }
}
