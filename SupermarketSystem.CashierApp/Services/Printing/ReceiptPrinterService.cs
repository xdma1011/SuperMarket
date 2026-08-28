namespace SupermarketSystem.CashierApp.Services.Printing;

public sealed record PrintResult(bool Success, string? ErrorMessage);

/// <summary>
/// نقطة دخول واحدة للطباعة — "اطبع هالفاتورة"، بلا ما الطالب (SaleWindow)
/// يعرف تفاصيل USB أو الشبكة. القرار مبني على
/// AppConfig.PrinterConnectionType، قابل للتعديل من الإعدادات بلا أي
/// تغيير كود.
///
/// فشل الطباعة **أبدًا ما لازم يفشّل أو يلغي البيع نفسه** — البيع سُجّل
/// أصلًا قبل ما نوصل لمرحلة الطباعة. طابعة معطَّلة مشكلة منفصلة كليًا.
/// </summary>
public sealed class ReceiptPrinterService
{
    private readonly AppConfig _config;

    public ReceiptPrinterService(AppConfig config)
    {
        _config = config;
    }

    public async Task<PrintResult> PrintAsync(ReceiptData receiptData, CancellationToken cancellationToken)
    {
        IPrinterConnection connection;

        try
        {
            connection = _config.PrinterConnectionType switch
            {
                "Network" => new NetworkPrinterConnection(_config.PrinterNetworkIpAddress ?? "", _config.PrinterNetworkPort),
                _ => new UsbPrinterConnection(_config.PrinterUsbName ?? "")
            };

            var receiptBytes = EscPosReceiptBuilder.Build(receiptData);
            await connection.SendRawAsync(receiptBytes, cancellationToken);

            return new PrintResult(true, null);
        }
        catch (Exception ex)
        {
            return new PrintResult(false, ex.Message);
        }
    }
}
