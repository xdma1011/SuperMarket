using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SupermarketSystem.CashierApp.Services;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// طلبات مساعد الكاشير (28/9/2026): المساعد/الشريك ضرب الأغراض من تلفونه (صفحة "تجهيز طلب" بلوحة الإدارة)،
/// والكاشير بينزّل الطلب بالسلة وبيحاسب هو على المصاري الحقيقية. أونلاين بس - بلا نت، الكاشير بيضرب
/// الأصناف عادي. الطلب بيتسكّر بالسيرفر لحظة البيع (PreparedOrderId بطلب البيع)، مش لحظة التنزيل.
/// </summary>
public partial class PreparedOrdersWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid? _branchId;

    public PreparedOrderDto? SelectedOrder { get; private set; }

    public PreparedOrdersWindow(ApiClient apiClient, Guid? branchId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _branchId = branchId;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "";
        var result = await _apiClient.GetOpenPreparedOrdersAsync(_branchId, CancellationToken.None);
        RefreshButton.IsEnabled = true;

        if (!result.Success)
        {
            StatusText.Text = $"ما قدرت أجيب الطلبات: {result.ErrorMessage}";
        }

        OrdersGrid.ItemsSource = result.Orders;
        EmptyText.Visibility = result.Success && result.Orders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (result.Orders.Count > 0)
        {
            OrdersGrid.SelectedIndex = 0;
            OrdersGrid.Focus();
        }
        else
        {
            ItemsGrid.ItemsSource = null;
        }
    }

    private void OrdersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ItemsGrid.ItemsSource = (OrdersGrid.SelectedItem as PreparedOrderDto)?.Items;
    }

    private void LoadSelected()
    {
        if (OrdersGrid.SelectedItem is not PreparedOrderDto order)
        {
            return;
        }

        SelectedOrder = order;
        DialogResult = true;
    }

    private void OrdersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => LoadSelected();

    private void LoadButton_Click(object sender, RoutedEventArgs e) => LoadSelected();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void CancelOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is not PreparedOrderDto order)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"إلغاء الطلب رقم {order.TicketNumber}؟ (الزبون فلّ أو انضرب بالغلط)",
            "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var (success, errorMessage) = await _apiClient.CancelPreparedOrderAsync(order.Id, CancellationToken.None);
        await ReloadAsync();
        if (!success)
        {
            StatusText.Text = $"ما انلغى الطلب: {errorMessage}";
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            LoadSelected();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
