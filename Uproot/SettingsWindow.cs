using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Uproot.Services;

namespace Uproot;

/// <summary>Settings and About in one window: how to uninstall, updates, the links every studio app has, Works with.</summary>
public sealed class SettingsWindow : GlassWindow
{
    private const string Utm = "utm_source=uproot&utm_medium=app";
    private readonly StackPanel _page = new();
    private StackPanel _card = new();

    public SettingsWindow(Func<Task<string>> checkNow) : base("Settings — Uproot", 620, 720)
    {
        MinWidth = 480; MinHeight = 400;

        Section("Uninstalling");
        Option("Look for leftovers after an app is removed", "Folders and registry keys named after the app, in the places apps keep things. You see the list and choose before anything goes.",
            Settings.FindLeftovers, v => Settings.FindLeftovers = v);
        Option("Use the app's silent uninstall when it has one", "No uninstaller windows to click through. Installers built with Windows Installer show only a progress bar.",
            Settings.Quiet, v => Settings.Quiet = v);
        Option("Show Windows components and updates", "Entries Windows' own list hides: runtimes registered as parts of other apps, and updates.",
            Settings.ShowSystem, v => Settings.ShowSystem = v);

        Section("Updates");
        Option("Check for updates automatically", "A few seconds after Uproot opens, then every hour while it's open.", Settings.AutoUpdate, v => Settings.AutoUpdate = v);
        var result = Dim("");
        var check = MakeButton("Check now", false, async (s, _) =>
        {
            ((Button)s).IsEnabled = false;
            result.Text = "Checking…";
            result.Text = await checkNow();
            ((Button)s).IsEnabled = true;
        });
        check.HorizontalAlignment = HorizontalAlignment.Left;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        result.VerticalAlignment = VerticalAlignment.Center;
        result.Margin = new Thickness(12, 0, 0, 0);
        row.Children.Add(check);
        row.Children.Add(result);
        _card.Children.Add(row);

        Section("About");
        _card.Children.Add(new TextBlock { Text = $"Uproot {Updater.Current.ToString(3)}", FontWeight = FontWeights.SemiBold });
        _card.Children.Add(Dim("Free. Made by Protagonist Labs."));
        Line(Link("What's new", WhatsNewWindow.Open), " · every release, month by month");
        Line(Link("Send feedback", () => Open("mailto:feedback@protagonistlabs.app?subject=Uproot%20feedback")), " · feedback@protagonistlabs.app — bugs, ideas, anything");
        Line(Link("Website", () => Open($"{Ecosystem.Page}?{Utm}&utm_campaign=about")), " · protagonistlabs.app/uproot");
        Line(Link("More apps from Protagonist Labs", () => Open($"https://protagonistlabs.app/?{Utm}&utm_campaign=more-apps")), "");

        Section("Works with");
        WorksWith();

        var scroll = new ScrollViewer { Content = _page, Style = (Style)FindResource("GlassScrollViewer"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) };
        Body(scroll, MakeButton("Close", true, (_, _) => Close()));
    }

    /// <summary>The sisters that add something to Uproot: the ones here, then the ones that would (vault Ecosistem.md, "Works with").</summary>
    private void WorksWith()
    {
        string utm = $"{Utm}&utm_campaign=works-with";
        var sisters = new (string Id, string Name, string Does, string Would)[]
        {
            ("filelabs", "File Labs", " · opens an app's folder when you click its location", " · would open an app's folder when you click its location"),
            ("spacescan", "SpaceScan", " · shows what's inside an app's folder, biggest first", " · would show what's inside an app's folder, biggest first"),
            ("shelf", "Shelf", " · games from your Shelf library are marked in the list", " · would mark the games from your Shelf library in the list"),
        };
        foreach (var s in sisters)
            if (Ecosystem.Exe(s.Id) is { } exe) Line(Link(s.Name, () => Start(exe, "")), s.Does);
        foreach (var s in sisters.Where(s => Ecosystem.Exe(s.Id) == null).Take(3))
            Line(Link(s.Name, () => Open($"https://protagonistlabs.app/{s.Id}/?{utm}")), s.Would);

        if (!Ecosystem.BoughtSomething()) return;
        var paid = new[] { ("shelf", "Shelf"), ("stash", "Stash"), ("frostpane", "Frostpane"), ("reel", "Reel"), ("planner", "Planner"), ("reminder", "Reminder") };
        var missing = paid.Where(a => Ecosystem.Exe(a.Item1) == null).Take(4).ToList();
        if (missing.Count == 0) return;
        var line = Line(new Run("Owners of one of our apps get 30% off the others: "), "");
        line.Margin = new Thickness(0, 6, 0, 0);
        foreach (var (id, name) in missing)
        {
            if (id != missing[0].Item1) line.Inlines.Add(new Run(" · "));
            line.Inlines.Add(Link(name, () => Open($"https://protagonistlabs.app/{id}/?code={(id == "shelf" ? "SECOND30SHELF" : "SECOND30")}&{utm}")));
        }
    }

    /// <summary>A section: its name, then a card that what follows goes into (1.0.4).</summary>
    private void Section(string title)
    {
        _page.Children.Add(Themed(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, _page.Children.Count == 0 ? 0 : 20, 0, 8) }, "TextSecondary"));
        _card = new StackPanel();
        var card = new Border { Child = _card, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12) };
        card.SetResourceReference(Border.BackgroundProperty, "Field");
        card.SetResourceReference(Border.BorderBrushProperty, "Border");
        _page.Children.Add(card);
    }

    private void Option(string label, string hint, bool value, Action<bool> save)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 0) };
        box.Click += (_, _) => save(box.IsChecked == true);
        _card.Children.Add(box);
        var h = Dim(hint, 12);
        h.Margin = new Thickness(26, 2, 0, 8);
        _card.Children.Add(h);
    }

    private TextBlock Line(Inline first, string rest)
    {
        var line = Dim("", 12);
        line.Margin = new Thickness(0, 3, 0, 3);
        line.Inlines.Add(first);
        if (rest != "") line.Inlines.Add(new Run(rest));
        _card.Children.Add(line);
        return line;
    }

    /// <summary>A name in the line's own colour, underlined only under the pointer.</summary>
    private static Hyperlink Link(string text, Action open)
    {
        var link = new Hyperlink(new Run(text)) { TextDecorations = null, Cursor = System.Windows.Input.Cursors.Hand };
        link.SetResourceReference(TextElement.ForegroundProperty, "TextPrimary");
        link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.MouseLeave += (_, _) => link.TextDecorations = null;
        link.Click += (_, _) => open();
        return link;
    }

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static void Start(string exe, string args)
    {
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false }); }
        catch (System.ComponentModel.Win32Exception) { } // gone since the card was read
    }
}
