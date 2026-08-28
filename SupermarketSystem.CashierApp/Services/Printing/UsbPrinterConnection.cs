using System.Runtime.InteropServices;

namespace SupermarketSystem.CashierApp.Services.Printing;

/// <summary>
/// إرسال بايتات ESC/POS خام مباشرة لطابعة مثبَّتة بويندوز — عبر
/// Windows Print Spooler مباشرة (winspool.drv)، بلا أي SDK خارجي.
/// نمط RawPrinterHelper قياسي ومعروف لإرسال بيانات خام لطابعات
/// الإيصالات الحرارية.
///
/// الطابعة لازم تكون مثبَّتة بويندوز أول (تعريف الشركة المصنّعة، أو
/// "Generic / Text Only") — هذا الكود ما بيثبّت طابعة، بس بيرسلها
/// بيانات خام بعد ما تكون مثبَّتة ومعروفة بالاسم.
/// </summary>
public sealed class UsbPrinterConnection : IPrinterConnection
{
    private readonly string _printerName;

    public UsbPrinterConnection(string printerName)
    {
        _printerName = printerName;
    }

    public Task SendRawAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_printerName))
        {
            throw new InvalidOperationException("اسم طابعة USB غير محدَّد بالإعدادات (PrinterUsbName).");
        }

        return Task.Run(() => SendRawBytesToPrinter(_printerName, data), cancellationToken);
    }

    private static void SendRawBytesToPrinter(string printerName, byte[] bytes)
    {
        if (!RawPrinterHelper.OpenPrinter(printerName, out var printerHandle, IntPtr.Zero))
        {
            throw new InvalidOperationException($"تعذّر فتح الطابعة '{printerName}' - تأكد إنها مثبَّتة ومتصلة.");
        }

        try
        {
            var docInfo = new RawPrinterHelper.DOCINFOA
            {
                pDocName = "فاتورة بيع",
                pDataType = "RAW"
            };

            if (!RawPrinterHelper.StartDocPrinter(printerHandle, 1, docInfo))
            {
                throw new InvalidOperationException("تعذّر بدء مهمة الطباعة.");
            }

            try
            {
                if (!RawPrinterHelper.StartPagePrinter(printerHandle))
                {
                    throw new InvalidOperationException("تعذّر بدء صفحة الطباعة.");
                }

                try
                {
                    var pointer = Marshal.AllocCoTaskMem(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, pointer, bytes.Length);
                        if (!RawPrinterHelper.WritePrinter(printerHandle, pointer, bytes.Length, out _))
                        {
                            throw new InvalidOperationException("فشل إرسال بيانات الطباعة للطابعة.");
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pointer);
                    }
                }
                finally
                {
                    RawPrinterHelper.EndPagePrinter(printerHandle);
                }
            }
            finally
            {
                RawPrinterHelper.EndDocPrinter(printerHandle);
            }
        }
        finally
        {
            RawPrinterHelper.ClosePrinter(printerHandle);
        }
    }
}

/// <summary>استدعاءات winspool.drv الخام - نمط RawPrinterHelper القياسي لإرسال بيانات خام لطابعة بويندوز.</summary>
internal static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
}
