namespace SupermarketSystem.CashierApp.Services.Printing;

/// <summary>NotConfigured: الطباعة مطفية ("None") أو طابعة USB بلا اسم - مش فشل طباعة، ما في طابعة أصلًا.</summary>
public sealed record PrintResult(bool Success, string? ErrorMessage, bool NotConfigured = false);

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

        // "None" = بلا طابعة (قبل الافتتاح/جهاز بلا طابعة)؛ USB بلا اسم طابعة نفس الشي - بدل ما
        // كل بيعة ترجّع "تعذّرت الطباعة" وتطلع رسالة للكاشير.
        if (_config.PrinterConnectionType == "None"
            || (_config.PrinterConnectionType != "Network" && string.IsNullOrWhiteSpace(_config.PrinterUsbName)))
        {
            return new PrintResult(false, "الطباعة مش مضبوطة (PrinterConnectionType بالإعدادات).", NotConfigured: true);
        }

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
