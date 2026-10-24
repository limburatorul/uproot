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
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        top.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = intro, FontSize = 13.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var note = Dim($"Folders go to the Recycle Bin. Registry keys are saved to a .reg file in {Leftovers.BackupFolder} before they are removed; " +
                       $"double-click one to put it back. Folders over {Format.Size(Leftovers.BigFolder)} start unticked: the Recycle Bin may not hold them, and they'd be deleted for good.");
        note.LineHeight = 18;
        note.Margin = new Thickness(0, 8, 0, 0);
        top.Children.Add(note);

        // the list in its own outlined box, a hairline between rows
        var list = new ItemsControl { Style = (Style)FindResource("VirtualList"), ItemsSource = items, ItemTemplate = Row(), Margin = new Thickness(6, 4, 2, 4) };
        var box = new Border { Child = list, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BorderBrushProperty, "Border");
        box.SetResourceReference(Border.BackgroundProperty, "Field");
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_summary, Dock.Bottom);
        _summary.Margin = new Thickness(2, 10, 0, 0);
        _summary.FontSize = 12.5;
        dock.Children.Add(top);
        dock.Children.Add(_summary);
        dock.Children.Add(box);

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
        // check · Folder/Registry · path (with the app it came from) · size, a hairline under each
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        line.SetResourceReference(Border.BorderBrushProperty, "Divider");
        line.SetValue(Border.PaddingProperty, new Thickness(8, 9, 10, 9));
        var grid = new FrameworkElementFactory(typeof(Grid));
        line.AppendChild(grid);
        foreach (var w in new[] { new GridLength(32), new GridLength(84), new GridLength(1, GridUnitType.Star), new GridLength(80) })
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
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(Grid.ColumnProperty, 1);
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        badge.SetValue(Border.PaddingProperty, new Thickness(8, 1, 8, 2));
        badge.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        badge.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        badge.SetResourceReference(Border.BackgroundProperty, "PillBackground");
        var kind = Text("KindText", 0, "TextSecondary");
        kind.SetValue(TextBlock.FontSizeProperty, 11.5);
        badge.AppendChild(kind);
        grid.AppendChild(badge);
        var words = new FrameworkElementFactory(typeof(StackPanel));
        words.SetValue(Grid.ColumnProperty, 2);
        var path = new FrameworkElementFactory(typeof(TextBlock));
        path.SetBinding(TextBlock.TextProperty, new Binding("Path"));
        path.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        words.AppendChild(path);
        var app = new FrameworkElementFactory(typeof(TextBlock));
        app.SetBinding(TextBlock.TextProperty, new Binding("App") { StringFormat = "from {0}" });
        app.SetValue(TextBlock.FontSizeProperty, 11.5);
        app.SetValue(MarginProperty, new Thickness(0, 1, 0, 0));
        app.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        words.AppendChild(app);
        grid.AppendChild(words);
        var size = Text("SizeText", 3, "TextPrimary");
        size.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        size.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        size.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
        grid.AppendChild(size);
        return new DataTemplate { VisualTree = line };
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
