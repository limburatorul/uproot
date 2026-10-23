using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Uproot.Services;

namespace Uproot;

/// <summary>What the removed apps left behind, ticked where it is safe to remove, for the user to confirm.</summary>
public sealed class LeftoversWindow : GlassWindow
{
    private readonly List<Leftover> _items;
    private readonly Button _remove;
    private readonly TextBlock _summary = Dim("");
    public List<string>? Failed { get; private set; }

    public LeftoversWindow(string title, string intro, List<Leftover> items) : base(title, 860, 560)
    {
        _items = items;
        MinWidth = 600; MinHeight = 360;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        top.Children.Add(Dim($"Folders go to the Recycle Bin. Registry keys are saved to a .reg file in {Leftovers.BackupFolder} before they are removed; " +
                             $"double-click one to put it back. Folders over {Format.Size(Leftovers.BigFolder)} start unticked: the Recycle Bin may not hold them, and they'd be deleted for good."));
        ((TextBlock)top.Children[2]).Margin = new Thickness(0, 6, 0, 0);

        var list = new ItemsControl { Style = (Style)FindResource("VirtualList"), ItemsSource = items, ItemTemplate = Row() };
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_summary, Dock.Bottom);
        _summary.Margin = new Thickness(0, 8, 0, 0);
        dock.Children.Add(top);
        dock.Children.Add(_summary);
        dock.Children.Add(list);

        _remove = MakeButton("Remove ticked", true, (_, _) => Remove());
        Body(dock, _remove, MakeButton("Keep everything", false, (_, _) => Close()));
        foreach (var item in items) item.PropertyChanged += (_, _) => Count();
        Count();
    }

    private void Count()
    {
        var ticked = _items.Where(i => i.IsChecked).ToList();
        long bytes = ticked.Sum(i => i.Bytes);
        _summary.Text = $"{ticked.Count} of {_items.Count} ticked" + (bytes > 0 ? $" · {Format.Size(bytes)} of folders" : "");
        _remove.IsEnabled = ticked.Count > 0;
    }

    private async void Remove()
    {
        _remove.IsEnabled = false;
        var ticked = _items.Where(i => i.IsChecked).ToList();
        Failed = await Task.Run(() => Leftovers.Remove(ticked));
        Close();
    }

    private static DataTemplate Row()
    {
        // check · Folder/Registry · path (with the app it came from) · size
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(MarginProperty, new Thickness(0, 3, 8, 3));
        foreach (var w in new[] { new GridLength(30), new GridLength(70), new GridLength(1, GridUnitType.Star), new GridLength(80) })
        {
            var c = new FrameworkElementFactory(typeof(ColumnDefinition));
            c.SetValue(ColumnDefinition.WidthProperty, w);
            grid.AppendChild(c);
        }
        var check = new FrameworkElementFactory(typeof(CheckBox));
        check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("IsChecked") { Mode = BindingMode.TwoWay });
        check.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("Path"));
        check.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(check);
        var kind = Text("KindText", 1, "TextSecondary");
        grid.AppendChild(kind);
        var words = new FrameworkElementFactory(typeof(StackPanel));
        words.SetValue(Grid.ColumnProperty, 2);
        var path = new FrameworkElementFactory(typeof(TextBlock));
        path.SetBinding(TextBlock.TextProperty, new Binding("Path"));
        path.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        words.AppendChild(path);
        var app = new FrameworkElementFactory(typeof(TextBlock));
        app.SetBinding(TextBlock.TextProperty, new Binding("App") { StringFormat = "from {0}" });
        app.SetValue(TextBlock.FontSizeProperty, 11.0);
        app.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        words.AppendChild(app);
        grid.AppendChild(words);
        var size = Text("SizeText", 3, "TextPrimary");
        size.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        grid.AppendChild(size);
        return new DataTemplate { VisualTree = grid };
    }

    private static FrameworkElementFactory Text(string binding, int column, string brush)
    {
        var t = new FrameworkElementFactory(typeof(TextBlock));
        t.SetBinding(TextBlock.TextProperty, new Binding(binding));
        t.SetValue(Grid.ColumnProperty, column);
        t.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
