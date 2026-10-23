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

    /// <summary>The window's content on the app's surface, with a row of buttons at the bottom.</summary>
    protected void Body(UIElement content, params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in buttons) { b.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(b); }
        var dock = new DockPanel { Margin = new Thickness(20, 18, 20, 16) };
        DockPanel.SetDock(row, Dock.Bottom);
        dock.Children.Add(row);
        dock.Children.Add(content);
        var ground = new Border { Child = dock };
        ground.SetResourceReference(Border.BackgroundProperty, "Surface");
        Content = ground;
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
        words.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        words.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
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
