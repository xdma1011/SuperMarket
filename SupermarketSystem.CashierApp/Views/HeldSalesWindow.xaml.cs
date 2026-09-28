using System.Windows;
using System.Windows.Input;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// قائمة الفواتير المعلّقة على هالجهاز (HeldCartStore). دبل كليك/Enter/زر الاسترجاع = ResumedCart وDialogResult=true،
/// والفاتورة بتنشال من الملف فورًا (ما بتضل بمكانين). الحذف بتأكيد - زبون فلّ وما رجع.
/// </summary>
public partial class HeldSalesWindow : Window
{
    private readonly string _storeDirectory;

    public HeldCart? ResumedCart { get; private set; }

    public HeldSalesWindow(string storeDirectory)
    {
        InitializeComponent();
        _storeDirectory = storeDirectory;
        Loaded += (_, _) =>
        {
            Reload();
            HeldGrid.Focus();
        };
    }

    private void Reload()
    {
        var carts = HeldCartStore.Load(_storeDirectory).OrderBy(c => c.HeldAtLocal).ToList();
        HeldGrid.ItemsSource = carts;
        EmptyText.Visibility = carts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (carts.Count > 0)
        {
            HeldGrid.SelectedIndex = 0;
        }
    }

    private void Resume()
    {
        if (HeldGrid.SelectedItem is not HeldCart selected)
        {
            return;
        }

        var removed = HeldCartStore.Remove(_storeDirectory, selected.Id);
        if (removed is null)
        {
            MessageBox.Show("هالفاتورة انرجعت أو انحذفت من مكان تاني.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information);
            Reload();
            return;
        }

        ResumedCart = removed;
        DialogResult = true;
    }

    private void HeldGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Resume();

    private void ResumeButton_Click(object sender, RoutedEventArgs e) => Resume();

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (HeldGrid.SelectedItem is not HeldCart selected)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"حذف الفاتورة المعلّقة ({selected.ItemCount} صنف، {selected.Total:0.000} د.أ) نهائيًا؟",
            "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        HeldCartStore.Remove(_storeDirectory, selected.Id);
        Reload();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Resume();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
