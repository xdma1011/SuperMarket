using System.Runtime.CompilerServices;
using Microsoft.Data.SqlClient;
using SupermarketSystem.IntegrationTests;

namespace SupermarketSystem.CashierHeadlessTests;

/// <summary>
/// قاعدة منفصلة لهالمشروع: اسم قاعدة الاختبار + "_cashier" (مثلًا sprmrkt_integration_tests_cashier). DatabaseFixture (من
/// IntegrationTests) بيقرأ INTEGRATION_TEST_CONNECTION_STRING أول ما ينلمس، فبنعدّله هون قبل أي كود (ModuleInitializer).
/// السبب: Respawn بيصفّر القاعدة قبل كل اختبار - لو المشروعين اشتغلوا سوا (dotnet test على الـsln) على نفس القاعدة، بيمسحوا بيانات بعض.
/// الاسم بيضل فيه "test" (حماية الـfixture)، وما بنلمس أي قاعدة تانية. القاعدة بتنعمل لحالها بأول تشغيل (Migrate).
/// </summary>
internal static class TestDatabaseRedirect
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        var connectionString = Environment.GetEnvironmentVariable("INTEGRATION_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // بيئة Docker الافتراضية: الـfixture بياخد الاسم من INTEGRATION_TEST_DB_NAME.
            var name = Environment.GetEnvironmentVariable("INTEGRATION_TEST_DB_NAME") ?? "sprmrkt_integration_tests";
            if (!name.EndsWith("_cashier", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("INTEGRATION_TEST_DB_NAME", name + "_cashier");
            }

            return;
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!builder.InitialCatalog.EndsWith("_cashier", StringComparison.OrdinalIgnoreCase))
        {
            builder.InitialCatalog += "_cashier";
            Environment.SetEnvironmentVariable("INTEGRATION_TEST_CONNECTION_STRING", builder.ConnectionString);
        }
    }
}

/// <summary>xUnit بيدوّر على تعريف المجموعة بنفس مشروع الاختبارات - نفس DatabaseFixture تبع IntegrationTests، بالتسلسل.</summary>
[CollectionDefinition(Name)]
public sealed class CashierDatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "CashierDatabase";
}
