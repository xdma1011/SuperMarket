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
}
