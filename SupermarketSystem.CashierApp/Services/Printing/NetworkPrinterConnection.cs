using System.Net.Sockets;

namespace SupermarketSystem.CashierApp.Services.Printing;

/// <summary>
/// طابعات الشبكة الحرارية بتقبل عادة اتصال TCP خام على المنفذ 9100
/// (بروتوكول RAW/JetDirect القياسي) — تفتح Socket، تبعت البايتات، تسكر.
/// أبسط بكثير من USB (بلا P/Invoke، بلا Windows Spooler).
/// </summary>
public sealed class NetworkPrinterConnection : IPrinterConnection
{
    private readonly string _ipAddress;
    private readonly int _port;

    public NetworkPrinterConnection(string ipAddress, int port)
    {
        _ipAddress = ipAddress;
        _port = port;
    }

    public async Task SendRawAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_ipAddress))
        {
            throw new InvalidOperationException("عنوان IP لطابعة الشبكة غير محدَّد بالإعدادات (PrinterNetworkIpAddress).");
        }

        using var client = new TcpClient();

        // مهلة قصيرة عمدًا (5 ثواني) - لو الطابعة مطفية أو الشبكة معلَّقة،
        // ما لازم الكاشير ينتظر دقايق قبل ما يعرف إنه في مشكلة.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await client.ConnectAsync(_ipAddress, _port, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException($"تعذّر الوصول لطابعة الشبكة ({_ipAddress}:{_port}) - تأكد إنها شغّالة ومتصلة بنفس الشبكة.");
        }

        await using var stream = client.GetStream();
        await stream.WriteAsync(data, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
