using System.IO;
using System.Text.Json;
using SupermarketSystem.CashierApp.Views;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>فاتورة معلّقة: سلة كاملة انحطّت على جنب (زبون بدو يبدّل صنف، والطابور واقف) - بترجع بالضبط زي ما كانت.</summary>
public sealed class HeldCart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime HeldAtLocal { get; set; }
    public string? CashierName { get; set; }
    public List<CartLine> Lines { get; set; } = new();

    /// <summary>لو السلة كانت طلب جاهز من مساعد الكاشير - بيرجع معها عشان الطلب يتسكّر وقت البيع.</summary>
    public Guid? PreparedOrderId { get; set; }
    public int? PreparedTicketNumber { get; set; }

    /// <summary>رقم الزبون (اختياري) اللي كان مكتوب وقت التعليق - بيرجع معها.</summary>
    public string? CustomerPhone { get; set; }

    public int ItemCount => Lines.Count;
    public decimal Total => Lines.Sum(l => l.LineTotal);
    public string Summary => string.Join("، ", Lines.Select(l => $"{l.ProductName} × {l.Quantity:0.###}"));
}

/// <summary>
/// تعليق الفواتير (28/9/2026، طلب صاحب المشروع): ملف JSON جنب local.db - نفس نمط StoreBrandingCache/
/// PaymentSettingsCache (ملف مستقل، لا جدول SQLite ولا Migration محلية). محفوظ على الجهاز فورًا، فانقطاع
/// كهربا أو إغلاق التطبيق ما بيضيّع فاتورة معلّقة. الأسعار ورقم نسخة الكتالوج بتضل زي ما كانت لحظة
/// التعليق - السيرفر بيحسب كل سطر بسعر نسخته (OFFLINE PRICE)، فتعليق طويل ما بيغيّر السعر على الزبون.
/// </summary>
public static class HeldCartStore
{
    private const string FileName = "held-carts.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static List<HeldCart> Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return new List<HeldCart>();
            }

            return JsonSerializer.Deserialize<List<HeldCart>>(File.ReadAllText(path), JsonOptions) ?? new List<HeldCart>();
        }
        catch (Exception)
        {
            // ملف تالف (نادر جدًا) - نحتفظ بنسخة منه عشان ما تضيع البيانات، ونبلّش بقائمة فاضية.
            try
            {
                File.Copy(path, path + $".broken-{DateTime.Now:yyyyMMddHHmmss}", overwrite: false);
            }
            catch (Exception)
            {
            }

            return new List<HeldCart>();
        }
    }

    public static void Add(string directory, HeldCart cart)
    {
        var carts = Load(directory);
        carts.Add(cart);
        Save(directory, carts);
    }

    /// <summary>بيشيل فاتورة معلّقة (استرجاع أو حذف) وبيرجّعها - null لو مش موجودة (انسحبت من نافذة تانية).</summary>
    public static HeldCart? Remove(string directory, Guid heldCartId)
    {
        var carts = Load(directory);
        var cart = carts.FirstOrDefault(c => c.Id == heldCartId);
        if (cart is null)
        {
            return null;
        }

        carts.Remove(cart);
        Save(directory, carts);
        return cart;
    }

    public static int Count(string directory) => Load(directory).Count;

    /// <summary>كتابة لملف مؤقت ثم استبدال - انقطاع بالنص ما بيخلّي ملف نصه مكتوب.</summary>
    private static void Save(string directory, List<HeldCart> carts)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(carts, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}
