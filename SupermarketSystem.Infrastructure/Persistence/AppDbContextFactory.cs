using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SupermarketSystem.Infrastructure.Services;

namespace SupermarketSystem.Infrastructure.Persistence;

/// <summary>
/// Used by the `dotnet ef` tooling at design time. Without it, the CLI would have to build and start the API host
/// to obtain an AppDbContext, which fails because AppDbContext has a second constructor parameter
/// (ICurrentUserContext) that the tooling cannot resolve on its own.
///
/// إصلاح (29/9/2026): لما تكون هاي الـfactory موجودة، EF بيستخدمها بكل أوامره - **حتى `database update`** (مش بس
/// `migrations add` زي ما كان مكتوب هون). كانت بتمرّر Connection String وهمي ("YES")، فـ`dotnet ef database update`
/// بلا `--connection` كان يوقع بـ"Format of the initialization string does not conform to specification".
/// هلق بتقرأ نفس الـConnection String تبع الـAPI بالترتيب:
///   1. متغيّر البيئة ConnectionStrings__DefaultConnection
///   2. SupermarketSystem.API/appsettings.{ASPNETCORE_ENVIRONMENT}.json (افتراضي Development)
///   3. SupermarketSystem.API/appsettings.json
/// `--connection` بسطر الأوامر بيضل يغلب الكل. بلا أي حزمة جديدة (System.Text.Json).
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string ConnectionName = "DefaultConnection";

    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(
            ResolveConnectionString(),
            sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        return new AppDbContext(optionsBuilder.Options, new PlaceholderCurrentUserContext());
    }

    private static string ResolveConnectionString()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable($"ConnectionStrings__{ConnectionName}");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var apiDirectory = FindApiDirectory();
        if (apiDirectory is not null)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (string.IsNullOrWhiteSpace(environment))
            {
                environment = "Development";
            }

            foreach (var file in new[] { $"appsettings.{environment}.json", "appsettings.json" })
            {
                var value = ReadConnectionString(Path.Combine(apiDirectory, file));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        throw new InvalidOperationException(
            $"ما لقيت ConnectionStrings:{ConnectionName} - حطّه بمتغيّر البيئة ConnectionStrings__{ConnectionName}، " +
            "أو بـSupermarketSystem.API/appsettings.json، أو مرّر --connection لأمر dotnet ef.");
    }

    /// <summary>
    /// `dotnet ef --startup-project SupermarketSystem.API` بيشغّل من مجلد الـAPI عادةً، بس لو انشغّل من جذر الحل أو من
    /// مشروع Infrastructure بندوّر لفوق لحد ما نلاقي مجلد SupermarketSystem.API.
    /// </summary>
    private static string? FindApiDirectory()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (directory.Name.Equals("SupermarketSystem.API", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(directory.FullName, "appsettings.json")))
            {
                return directory.FullName;
            }

            var child = Path.Combine(directory.FullName, "SupermarketSystem.API");
            if (File.Exists(Path.Combine(child, "appsettings.json")))
            {
                return child;
            }
        }

        return null;
    }

    private static string? ReadConnectionString(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        return document.RootElement.TryGetProperty("ConnectionStrings", out var section)
               && section.ValueKind == JsonValueKind.Object
               && section.TryGetProperty(ConnectionName, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
