using System.Windows;
using Microsoft.EntityFrameworkCore;
using SupermarketSystem.CashierApp.Local;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// شاشة إدارية محلية بحتة - تعرض طابور PendingSale (فواتير أوفلاين لسه ما
/// وصلت السيرفر) وآخر خطأ لكل وحدة، مع زر مزامنة يدوية فورية. الدخول محمي
/// بباسوورد بسيط (راجع MainWindow.AdminAccessButton_Click) - الهدف يمنع
/// الكاشير من الدخول بالصدفة، لا حماية أمنية جدّية.
/// </summary>
public partial class PendingQueueWindow : Window
{
    private readonly string _dbPath;
    private readonly BackgroundSyncService _backgroundSync;

    public PendingQueueWindow(string dbPath, BackgroundSyncService backgroundSync)
    {
        InitializeComponent();
        _dbPath = dbPath;
        _backgroundSync = backgroundSync;

        Loaded += (_, _) => LoadQueue();
    }

    private void LoadQueue()
    {
        using var db = new LocalDbContext(_dbPath);
        var pending = db.PendingSales.OrderBy(s => s.CreatedAtLocal).ToList();

        QueueGrid.ItemsSource = pending;
        SummaryText.Text = pending.Count == 0
            ? "لا يوجد فواتير بانتظار المزامنة"
            : $"{pending.Count} فاتورة بانتظار المزامنة";
    }

    private async void SyncNowButton_Click(object sender, RoutedEventArgs e)
    {
        SyncNowButton.IsEnabled = false;
        StatusText.Text = "جاري المزامنة...";

        var outcome = await _backgroundSync.TriggerManualSyncAsync();

        StatusText.Text = outcome switch
        {
            BackgroundSyncService.ManualSyncOutcome.Completed => "تمت المزامنة بنجاح",
            BackgroundSyncService.ManualSyncOutcome.Cancelled => "أُلغيت المزامنة",
            BackgroundSyncService.ManualSyncOutcome.AlreadyRunning => "فيه مزامنة شغّالة أصلًا بالخلفية - انتظر قليلًا",
            _ => $"فشلت المزامنة: {_backgroundSync.LastErrorMessage}"
        };

        LoadQueue();
        SyncNowButton.IsEnabled = true;
    }

    /// <summary>
    /// كانت مفقودة كليًا - رفض حقيقي من السيرفر (مو انقطاع نت) بيخلّي
    /// الفاتورة تضل بالطابور للأبد، تُعاد المحاولة كل دورة مزامنة وتفشل
    /// بنفس السبب دائمًا، بلا أي طريقة تصفّيها. الحذف هون نهائي ومقصود -
    /// لازم مراجعة السبب (عمود "آخر خطأ") قبل الضغط، البيع ما رح يوصل
    /// السيرفر أبدًا بعدها.
    /// </summary>
    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        if (QueueGrid.SelectedItem is not PendingSale selected)
        {
            StatusText.Text = "اختر فاتورة من القائمة أولًا.";
            return;
        }

        var confirm = MessageBox.Show(
            $"حذف نهائي - هذه الفاتورة (أُنشئت {selected.CreatedAtLocal:yyyy-MM-dd HH:mm}) لن تصل للسيرفر أبدًا بعد الحذف.\n\nآخر خطأ: {selected.LastErrorMessage}\n\nأكيد الحذف؟",
            "تأكيد الحذف النهائي", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        using var db = new LocalDbContext(_dbPath);
        var tracked = db.PendingSales.FirstOrDefault(s => s.Id == selected.Id);
        if (tracked is not null)
        {
            db.PendingSales.Remove(tracked);
            db.SaveChanges();
        }

        StatusText.Text = "تم حذف الفاتورة نهائيًا.";
        LoadQueue();
    }
}
