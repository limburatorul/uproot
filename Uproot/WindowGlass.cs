using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;

namespace Uproot;

/// <summary>Acrylic backdrop and a dark title bar, shared by every window (see vault Branding/Aplicatii, WPF section).</summary>
public static class WindowGlass
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_ACRYLIC = 3;

    public static void EnableAcrylic(Window window)
    {
        var hwndSource = (HwndSource)PresentationSource.FromVisual(window)!;
        IntPtr hwnd = hwndSource.Handle;

        // The compositor's own background is opaque black by default: without this, every partial-alpha
        // pixel composes over black before it ever reaches DWM.
        hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        int backdrop = DWMSBT_ACRYLIC;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
    }
}

// Styles/Controls.xaml is shared with Sysoptimizer, whose themes may upper-case labels; Uproot has the one Glass
// theme, which never does, but the templates still bind to these two.
public sealed class ThemeState : INotifyPropertyChanged
{
    public static ThemeState Instance { get; } = new();
    public bool UpperCase => false;
    public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
}

public sealed class CaseConverter : IMultiValueConverter
{
    public static CaseConverter Instance { get; } = new();
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values[0] == DependencyProperty.UnsetValue ? null : values[0];
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
