using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Uproot;

/// <summary>A glass window with the app's look: the base of every dialog below.</summary>
public class GlassWindow : Window
{
    protected GlassWindow(string title, double width, double height)
    {
        Title = title;
        Icon = new BitmapImage(new Uri("pack://application:,,,/app.ico"));
        Width = width; Height = height;
        Background = Brushes.Transparent;
        FontSize = 13;
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        SourceInitialized += (_, _) => WindowGlass.EnableAcrylic(this);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        if (Application.Current.MainWindow is { IsVisible: true } owner && owner != this) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    /// <summary>
    /// The window's content on an opaque panel (its text gets ClearType, as the main window's cards do), over a footer
    /// on the glass that holds the buttons, as Windows' own dialogs have it (1.0.4).
    /// </summary>
    protected void Body(UIElement content, params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons) { b.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(b); }
        var footer = new Border { Child = row, Padding = new Thickness(24, 14, 24, 14), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BackgroundProperty, "Surface");
        footer.SetResourceReference(Border.BorderBrushProperty, "Divider");
        var page = new Border { Child = content, Padding = new Thickness(24, 22, 24, 20) };
        page.SetResourceReference(Border.BackgroundProperty, "Panel");
        RenderOptions.SetClearTypeHint(page, ClearTypeHint.Enabled);
        var dock = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(page);
        Content = dock;
    }

    public static Button MakeButton(string text, bool primary, RoutedEventHandler click)
    {
        var b = new Button { Content = text, MinWidth = 90 };
        if (primary) b.Style = (Style)Application.Current.FindResource("PrimaryButton");
        b.Click += click;
        return b;
    }

    public static TextBlock Dim(string text, double size = 12) =>
        Themed(new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }, "TextSecondary");

    public static T Themed<T>(T t, string brush) where T : FrameworkElement
    {
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}

/// <summary>A question or a message: a title, some text, and its buttons.</summary>
public sealed class Ask : GlassWindow
{
    private bool _yes;

    private Ask(string title, string text, string? yes, string no) : base(title, 520, 250)
    {
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        words.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20, FontSize = 13.5 });
        var buttons = new List<Button>();
        if (yes != null) buttons.Add(MakeButton(yes, true, (_, _) => { _yes = true; Close(); }));
        buttons.Add(MakeButton(no, yes == null, (_, _) => Close()));
        Body(words, buttons.ToArray());
        Loaded += (_, _) => buttons[0].Focus();
    }

    public static bool Confirm(string title, string text, string yes, string no = "Cancel")
    {
        var d = new Ask(title, text, yes, no);
        d.ShowDialog();
        return d._yes;
    }

    public static void Tell(string title, string text) => new Ask(title, text, null, "OK").ShowDialog();
}
