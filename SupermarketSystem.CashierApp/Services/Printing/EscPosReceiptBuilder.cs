using System.Text;

namespace SupermarketSystem.CashierApp.Services.Printing;

public sealed record ReceiptLine(string ProductName, string UnitName, decimal Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record ReceiptData(
    string InvoiceNumber, DateTime CreatedAtLocal, string CashierName,
    IReadOnlyList<ReceiptLine> Lines, decimal Total, string PaymentMethodName);

/// <summary>
/// يبني أوامر ESC/POS خام (بايتات) بدل نص عادي — طابعات الإيصالات
/// الحرارية ما بتفهم "نص عادي"، بتفهم بس هالأوامر الثنائية القياسية.
///
/// ⚠️ نقطة صدق مهمة: دعم العربي على طابعات ESC/POS **يختلف فعليًا حسب
/// الشركة المصنّعة** — أمر اختيار صفحة الترميز (ESC t) هون مبني على
/// القيمة الشائعة عند أغلب الطابعات المتوافقة مع Epson، بس **مش مضمون
/// 100% يشتغل بلا تعديل على كل موديل**. أول تجربة حقيقية على طابعة
/// فعلية لازم تتأكد من هالتفصيلة تحديدًا.
/// </summary>
public static class EscPosReceiptBuilder
{
    private static readonly Encoding ArabicEncoding = Encoding.GetEncoding(1256);

    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;

    public static byte[] Build(ReceiptData data)
    {
        var bytes = new List<byte>();

        InitializePrinter(bytes);
        SelectArabicCodePage(bytes);

        SetAlignCenter(bytes);
        WriteLine(bytes, "فاتورة بيع");
        WriteLine(bytes, $"رقم: {data.InvoiceNumber}");
        WriteLine(bytes, data.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm"));
        WriteLine(bytes, $"الكاشير: {data.CashierName}");
        WriteLine(bytes, new string('-', 32));

        SetAlignRight(bytes);
        foreach (var line in data.Lines)
        {
            WriteLine(bytes, $"{line.ProductName} ({line.UnitName})");
            WriteLine(bytes, $"{line.Quantity} x {line.UnitPrice:0.00} = {line.LineTotal:0.00}");
        }

        WriteLine(bytes, new string('-', 32));

        SetBold(bytes, true);
        WriteLine(bytes, $"الإجمالي: {data.Total:0.00}");
        SetBold(bytes, false);

        WriteLine(bytes, $"طريقة الدفع: {data.PaymentMethodName}");
        WriteLine(bytes, new string('-', 32));

        SetAlignCenter(bytes);
        WriteLine(bytes, "شكرًا لزيارتكم");
        WriteLine(bytes);
        WriteLine(bytes);

        CutPaper(bytes);

        return bytes.ToArray();
    }

    private static void InitializePrinter(List<byte> bytes) => bytes.AddRange(new byte[] { Esc, 0x40 });

    /// <summary>ESC t 22 - قيمة شائعة لـWindows-1256 عند طابعات متوافقة مع Epson. لو النص طلع غير مقروء، أول شي نجرّب قيمًا تانية (0، 6، 21).</summary>
    private static void SelectArabicCodePage(List<byte> bytes) => bytes.AddRange(new byte[] { Esc, 0x74, 22 });

    private static void SetAlignCenter(List<byte> bytes) => bytes.AddRange(new byte[] { Esc, 0x61, 1 });
    private static void SetAlignRight(List<byte> bytes) => bytes.AddRange(new byte[] { Esc, 0x61, 2 });
    private static void SetBold(List<byte> bytes, bool on) => bytes.AddRange(new byte[] { Esc, 0x45, (byte)(on ? 1 : 0) });

    private static void WriteLine(List<byte> bytes, string text = "")
    {
        bytes.AddRange(ArabicEncoding.GetBytes(text));
        bytes.Add(0x0A);
    }

    /// <summary>GS V 1 - قطع جزئي (partial cut)، الأشيع بطابعات الإيصالات.</summary>
    private static void CutPaper(List<byte> bytes) => bytes.AddRange(new byte[] { Gs, 0x56, 1 });
}
