namespace SupermarketSystem.IntegrationTests;

/// <summary>
/// كل اختبارات الـintegration بمجموعة xUnit وحدة — يشغّلها xUnit بالتسلسل
/// (لا بالتوازي) لأنها كلها بتتشارك نفس قاعدة بيانات الاختبار الحقيقية
/// عبر DatabaseFixture. تشغيلهم بالتوازي كان رح يخلي اختبار يصفّر القاعدة
/// (Respawn) وسط اختبار تاني شغّال عليها بنفس اللحظة.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
