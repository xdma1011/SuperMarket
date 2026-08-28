namespace SupermarketSystem.Application.Common.Interfaces;

/// <summary>
/// رقم نسخة عام واحد يزيد تلقائيًا مع أي تغيير على الكتالوج. تطبيق
/// الكاشير (Offline-first) بيسأل بس "شو آخر نسخة عندك؟" بشكل متكرر
/// ورخيص، وبيسحب التحديث الكامل فقط لو الرقم اختلف.
///
/// Increment ذري بجملة SQL خام واحدة (نفس نمط IStockOperations.
/// TryDecreaseAsync) — تعديلات كتالوج متزامنة ما لازم تفقد أي زيادة.
/// </summary>
public interface ICatalogVersionService
{
    Task<long> GetCurrentVersionAsync(CancellationToken cancellationToken);
    Task IncrementVersionAsync(CancellationToken cancellationToken);
}
