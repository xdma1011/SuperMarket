namespace SupermarketSystem.CashierApp.Services.Printing;

/// <summary>
/// تجريد واحد بسيط — "ابعت بايتات خام للطابعة"، بغض النظر كيف متوصلة.
/// كل التعقيد (USB عبر Windows Spooler، أو شبكة عبر TCP Socket) محصور
/// بالتنفيذين، الطبقة اللي فوق (ReceiptPrinterService) ما بتعرف ولا
/// بتحتاج تعرف الفرق.
/// </summary>
public interface IPrinterConnection
{
    /// <summary>يرمي استثناء واضح لو فشل الاتصال أو الإرسال - الطالب (ReceiptPrinterService) بيمسكه ويترجمه لرسالة عربية للكاشير.</summary>
    Task SendRawAsync(byte[] data, CancellationToken cancellationToken);
}
