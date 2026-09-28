using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// سبب فتح الصندوق (اختياري، 28/9/2026) - كبسة سبب سريع بتسجّل فورًا، أو كتابة، أو "تسجيل الفتح" فاضي = بلا سبب.
/// إلغاء (Esc) = ما في فتحة بتنسجّل. النافذة بتجمع السبب بس؛ الحفظ والإرسال عند المستدعي (SaleWindow).
/// </summary>
public partial class DrawerOpenReasonWindow : Window
{
    public string? Reason { get; private set; }

    public DrawerOpenReasonWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ReasonBox.Focus();
    }

    private void QuickReason_Click(object sender, RoutedEventArgs e)
    {
        Reason = (sender as Button)?.Content as string;
        DialogResult = true;
    }

    private void ReasonBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Accept();
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        Reason = string.IsNullOrWhiteSpace(ReasonBox.Text) ? null : ReasonBox.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
