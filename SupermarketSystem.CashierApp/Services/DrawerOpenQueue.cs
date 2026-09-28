using System.IO;
using System.Text.Json;

namespace SupermarketSystem.CashierApp.Services;

/// <summary>فتحة صندوق محفوظة على الجهاز بانتظار ما توصل للسيرفر.</summary>
public sealed class PendingDrawerOpen
{
    public Guid ClientRequestId { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// فتح الصندوق بلا نت (28/9/2026): الفتحة بتنحفظ بملف JSON جنب local.db **قبل** أي محاولة إرسال (نفس فلسفة
/// PendingSale - الحفظ المحلي أول)، وبتنبعت فورًا لو في نت، وإلا المزامنة الخلفية بتبعتها بكل دورة. ClientRequestId
/// بيخلي إعادة الإرسال آمنة (السيرفر بيرجّع نفس السجل)، وOccurredAtUtc هو وقت الفتح الفعلي مش وقت الوصول.
/// نفس نمط HeldCartStore (ملف مستقل، بلا جدول SQLite ولا Migration محلية).
/// </summary>
public static class DrawerOpenQueue
{
    private const string FileName = "drawer-opens-pending.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>إرسال فوري (من الزر) ومزامنة خلفية ممكن يتلاقوا - وحدة بس بتبعت بنفس الوقت.</summary>
    private static readonly SemaphoreSlim FlushLock = new(1, 1);

    private static readonly object FileLock = new();

    public static List<PendingDrawerOpen> Load(string directory)
    {
        lock (FileLock)
        {
            return LoadUnlocked(directory);
        }
    }

    public static int Count(string directory) => Load(directory).Count;

    public static void Enqueue(string directory, PendingDrawerOpen item)
    {
        lock (FileLock)
        {
            var items = LoadUnlocked(directory);
            items.Add(item);
            SaveUnlocked(directory, items);
        }
    }

    /// <summary>بيبعت كل المحفوظ بالترتيب. بيرجّع كم انبعت وكم ضل. فشل = بيضل بالملف للدورة الجاية.</summary>
    public static async Task<(int Sent, int Remaining)> FlushAsync(string directory, ApiClient apiClient, CancellationToken cancellationToken)
    {
        if (!await FlushLock.WaitAsync(0, cancellationToken))
        {
            return (0, Count(directory)); // في إرسال شغّال هلق
        }

        try
        {
            var sent = 0;
            foreach (var item in Load(directory))
            {
                var (success, error) = await apiClient.RecordDrawerOpenAsync(
                    item.BranchId, item.Reason, item.ClientRequestId, item.OccurredAtUtc, cancellationToken);
                lock (FileLock)
                {
                    var items = LoadUnlocked(directory);
                    var stored = items.FirstOrDefault(i => i.ClientRequestId == item.ClientRequestId);
                    if (stored is null)
                    {
                        continue;
                    }

                    if (success)
                    {
                        items.Remove(stored);
                        sent++;
                    }
                    else
                    {
                        stored.AttemptCount++;
                        stored.LastError = error;
                    }

                    SaveUnlocked(directory, items);
                }

                if (!success)
                {
                    break; // غالبًا ما في نت - ما في داعي نجرّب الباقي هلق
                }
            }

            return (sent, Count(directory));
        }
        finally
        {
            FlushLock.Release();
        }
    }

    private static List<PendingDrawerOpen> LoadUnlocked(string directory)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            if (!File.Exists(path))
            {
                return new List<PendingDrawerOpen>();
            }

            return JsonSerializer.Deserialize<List<PendingDrawerOpen>>(File.ReadAllText(path), JsonOptions) ?? new List<PendingDrawerOpen>();
        }
        catch (Exception)
        {
            // ملف تالف (نادر جدًا) - نسخة احتياطية منه وبنبلّش بقائمة فاضية (نفس HeldCartStore).
            try
            {
                File.Copy(path, path + $".broken-{DateTime.Now:yyyyMMddHHmmss}", overwrite: false);
            }
            catch (Exception)
            {
            }

            return new List<PendingDrawerOpen>();
        }
    }

    /// <summary>كتابة لملف مؤقت ثم استبدال - انقطاع كهربا بنص الكتابة ما بيخرّب الملف.</summary>
    private static void SaveUnlocked(string directory, List<PendingDrawerOpen> items)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
