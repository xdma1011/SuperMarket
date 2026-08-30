using System.Windows;
using System.Windows.Input;

namespace SupermarketSystem.CashierApp.Views;

/// <summary>
/// Modal بسيط لإدخال باسوورد شاشة الإدارة (راجع MainWindow.AdminAccessButton_Click).
/// ما تعرف الباسوورد الصحيح ولا تقارنه - هاي مسؤولية المستدعي (MainWindow)،
/// هون بس تجمع الإدخال وترجّعه.
/// </summary>
public partial class AdminPasswordWindow : Window
{
    public string EnteredPassword { get; private set; } = string.Empty;

    public AdminPasswordWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Accept();
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        EnteredPassword = PasswordBox.Password;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
