using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Uproot.Services;

namespace Uproot;

public partial class MainWindow : Window
{
    private List<InstalledApp> _all = [];
    private ListCollectionView? _view;
    private string _filter = "all";
    private string[] _words = [];
    private string _sort = "name";
    private bool _descending;
    private CancellationTokenSource _background = new();
    private CancellationTokenSource? _stopWaiting;
    private bool _busy;
    private Updater.Release? _update;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMinutes(60) };

    public MainWindow()
    {
        InitializeComponent();
        Title = $"Uproot {Updater.Current.ToString(3)}";
        SourceInitialized += (_, _) => WindowGlass.EnableAcrylic(this);
        Loaded += async (_, _) => { await Reload(); SearchBox.Focus(); StartUpdateChecks(); };
        Closed += (_, _) => _background.Cancel();
        PreviewKeyDown += Window_PreviewKeyDown;
        ShowSortHeaders();
    }

    // --- loading ---

    /// <summary>
    /// Reads the registry and the Store packages (both quick: nothing on disk is opened), shows the list, then finds
    /// icons and measures sizes in the background. Ticks and the chosen row survive a reload.
    /// </summary>
    private async Task Reload()
    {
        _background.Cancel();
        _background = new CancellationTokenSource();
        var token = _background.Token;
        var ticked = _all.Where(a => a.IsChecked).Select(Id).ToHashSet();
        var chosen = AppList.SelectedItem is InstalledApp c ? Id(c) : null;
        Status.Text = "Reading the installed apps…";

        var watch = Stopwatch.StartNew();
        var desktop = Task.Run(Inventory.Desktop);
        var store = Task.Run(() =>
        {
            try { return Inventory.Store(); }
            catch (Exception ex) { AppLog.Write($"Couldn't read the Store apps: {ex.Message}"); return new List<InstalledApp>(); }
        });
        var shelf = Task.Run(Ecosystem.ShelfGames);
        var apps = (await desktop).Concat(await store).ToList();
        foreach (var (name, folder) in await shelf)
            foreach (var app in apps.Where(a => Paths.IsLocal(a.InstallLocation) && Paths.Within(a.InstallLocation!, folder)))
                app.ShelfGame = name;
        AppLog.Write($"Listed {apps.Count} apps in {watch.ElapsedMilliseconds} ms");

        foreach (var app in apps)
        {
            app.IsChecked = ticked.Contains(Id(app));
            app.PropertyChanged += App_PropertyChanged;
        }
        _all = apps;
        _view = new ListCollectionView(_all) { Filter = Show, CustomSort = new Sorter(_sort, _descending) };
        AppList.ItemsSource = _view;
        if (chosen != null) AppList.SelectedItem = _all.FirstOrDefault(a => Id(a) == chosen);
        Counts();

        // the rows in view first: icons, then sizes, each on its own low-priority thread
        var order = _view.Cast<InstalledApp>().Concat(_all).Distinct().ToList();
        void OnUi(Action a) => Dispatcher.BeginInvoke(a, DispatcherPriority.Background);
        StartBackground(() => Icons.LoadAll(order, OnUi, token), "Icons");
        StartBackground(() =>
        {
            Sizes.MeasureAll(order, OnUi, token);
            if (!token.IsCancellationRequested) OnUi(() => { if (_sort == "size") _view?.Refresh(); Counts(); });
        }, "Sizes");
    }

    private static void StartBackground(Action work, string name) =>
        new Thread(() => work()) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = name }.Start();

    private static string Id(InstalledApp a) => a.KeyPath ?? a.PackageFullName ?? a.Name;

    private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstalledApp.IsChecked)) Counts();
        else if (e.PropertyName == nameof(InstalledApp.Bytes) && sender == AppList.SelectedItem) ShowDetails();
    }

    // --- filter, search, sort ---

    private bool Show(object o)
    {
        var a = (InstalledApp)o;
        if (a.IsSystem && !Settings.ShowSystem) return false;
        if (_filter == "desktop" && a.Kind != AppKind.Desktop) return false;
        if (_filter == "store" && a.Kind != AppKind.Store) return false;
        if (_filter == "broken" && !a.IsBroken) return false;
        foreach (var w in _words) if (!a.Search.Contains(w)) return false;
        return true;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _words = SearchBox.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _view?.Refresh();
        Counts();
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        foreach (ToggleButton pill in Filters.Children) pill.IsChecked = pill == sender;
        _filter = (string)((FrameworkElement)sender).Tag;
        _view?.Refresh();
        Counts();
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        var key = (string)((FrameworkElement)sender).Tag;
        _descending = key == _sort ? !_descending : key is "size" or "installed"; // biggest and newest first
        _sort = key;
        if (_view != null) _view.CustomSort = new Sorter(_sort, _descending);
        ShowSortHeaders();
    }

    private void ShowSortHeaders()
    {
        string H(string key, string text) => key == _sort ? $"{text} {(_descending ? "↓" : "↑")}" : text;
        SortName.Content = H("name", "Name");
        SortVersion.Content = H("version", "Version");
        SortInstalled.Content = H("installed", "Installed");
        SortSize.Content = H("size", "Size");
    }

    private sealed class Sorter(string key, bool descending) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = (InstalledApp)x!; var b = (InstalledApp)y!;
            int r = key switch
            {
                "size" => Nullable.Compare(a.Bytes, b.Bytes),
                "installed" => Nullable.Compare(a.Installed, b.Installed),
                "version" => string.Compare(a.Version, b.Version, StringComparison.OrdinalIgnoreCase),
                _ => 0,
            };
            if (descending) r = -r;
            return r != 0 ? r : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase) * (key == "name" && descending ? -1 : 1);
        }
    }

    /// <summary>The pills' counts, the bottom line and the Uninstall button, all from what's ticked and shown.</summary>
    private void Counts()
    {
        var listed = _all.Where(a => !a.IsSystem || Settings.ShowSystem).ToList();
        var labels = new Dictionary<string, string>
        {
            ["all"] = $"All {listed.Count}",
            ["desktop"] = $"Desktop {listed.Count(a => a.Kind == AppKind.Desktop)}",
            ["store"] = $"Store {listed.Count(a => a.Kind == AppKind.Store)}",
            ["broken"] = $"Uninstaller missing {listed.Count(a => a.IsBroken)}",
        };
        foreach (ToggleButton pill in Filters.Children) pill.Content = labels[(string)pill.Tag];
        SearchHint.Text = $"Search {listed.Count} apps";

        int shown = _view?.Count ?? 0;
        EmptyText.Text = _words.Length > 0 ? $"Nothing called “{SearchBox.Text.Trim()}”." : "Nothing here.";
        EmptyText.Visibility = shown == 0 && _all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var ticked = _all.Where(a => a.IsChecked).ToList();
        CheckAll.IsChecked = shown > 0 && _view!.Cast<InstalledApp>().All(a => a.IsChecked) ? true : ticked.Count > 0 ? null : false;
        if (_busy) return;
        long bytes = ticked.Sum(a => a.Bytes ?? 0);
        Status.Text = ticked.Count == 0
            ? $"{shown} of {listed.Count} apps shown"
            : $"{ticked.Count} ticked" + (bytes > 0 ? $" · {Format.Size(bytes)}" : "") + $" · {shown} of {listed.Count} apps shown";
        int removable = ticked.Count(a => !a.IsBroken);
        UninstallChecked.Content = removable <= 1 ? "Uninstall" : $"Uninstall {removable} apps";
        UninstallChecked.IsEnabled = removable > 0;
        ClearChecked.Visibility = ticked.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CheckAll_Click(object sender, RoutedEventArgs e)
    {
        bool tick = CheckAll.IsChecked == true;
        foreach (InstalledApp a in _view!) a.IsChecked = tick;
        if (!tick) foreach (var a in _all) a.IsChecked = false;
    }

    private void ClearChecked_Click(object sender, RoutedEventArgs e) { foreach (var a in _all) a.IsChecked = false; }

    // --- keyboard ---

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && !_busy) { e.Handled = true; await Reload(); }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; SearchBox.Focus(); SearchBox.SelectAll(); }
        else if (e.Key == Key.Escape && SearchBox.Text.Length > 0) { e.Handled = true; SearchBox.Clear(); }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocused && _view?.Count > 0)
        {
            e.Handled = true;
            AppList.SelectedIndex = Math.Max(0, AppList.SelectedIndex);
            (AppList.ItemContainerGenerator.ContainerFromIndex(AppList.SelectedIndex) as ListBoxItem)?.Focus();
        }
    }

    private void AppList_KeyDown(object sender, KeyEventArgs e)
    {
        if (AppList.SelectedItem is not InstalledApp app) return;
        if (e.Key == Key.Space) { app.IsChecked = !app.IsChecked; e.Handled = true; }
        else if (e.Key == Key.Delete) { e.Handled = true; UninstallChecked_Click(sender, e); }
    }

    private void AppList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(AppList, d) is ListBoxItem { DataContext: InstalledApp app }) app.IsChecked = !app.IsChecked;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) { if (!_busy) await Reload(); }

    // --- details ---

    private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowDetails();

    private void ShowDetails()
    {
        if (AppList.SelectedItem is not InstalledApp a) { Details.Visibility = Visibility.Collapsed; DetailsEmpty.Visibility = Visibility.Visible; return; }
        Details.Visibility = Visibility.Visible;
        DetailsEmpty.Visibility = Visibility.Collapsed;
        DIcon.Source = a.Icon;
        DName.Text = a.Name;
        DPublisher.Text = string.Join(" · ", new[] { a.Publisher, a.KindText, a.ShelfGame != null ? "in your Shelf" : null }.Where(s => !string.IsNullOrEmpty(s)));
        DBroken.Visibility = a.IsBroken ? Visibility.Visible : Visibility.Collapsed;
        DBrokenText.Text = a.Broken + " Its entry can be taken off the list, with what it left behind.";

        DSize.Text = a.MeasuredBytes is { } m
            ? Format.Size(m + (a.DataBytes ?? 0)) + (a.DataBytes is > 0 ? $" ({Format.Size(m)} app, {Format.Size(a.DataBytes.Value)} data)" : "")
            : a.EstimatedBytes is { } est ? $"{Format.Size(est)}, as its installer declared" : "Not known";
        Row(DVersionLabel, DVersion, a.Version);
        Row(DInstalledLabel, DInstalled, a.Installed?.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
        bool folder = Paths.IsLocal(a.InstallLocation);
        DLocationLabel.Visibility = DLocationLine.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
        DLocationText.Text = a.InstallLocation ?? "";
        DSpaceScanLine.Visibility = folder && Ecosystem.Exe("spacescan") != null ? Visibility.Visible : Visibility.Collapsed;
        var command = Uninstaller.CommandFor(a, Settings.Quiet);
        DCommandLabel.Visibility = DCommand.Visibility = command != null ? Visibility.Visible : Visibility.Collapsed;
        DCommand.Text = command ?? "";
        DUninstall.IsEnabled = !a.IsBroken && !_busy;
        DRemoveEntry.Visibility = a.IsBroken && a.Kind == AppKind.Desktop ? Visibility.Visible : Visibility.Collapsed;
        DRemoveEntry.IsEnabled = !_busy;
    }

    private static void Row(TextBlock label, TextBlock value, string? text)
    {
        label.Visibility = value.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        value.Text = text ?? "";
    }

    /// <summary>The app's folder: in File Labs when it's installed, else wherever Windows opens folders.</summary>
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not InstalledApp { InstallLocation: { } dir } || !Directory.Exists(dir)) return;
        if (Ecosystem.Exe("filelabs") is { } fileLabs) SettingsWindow.Start(fileLabs, $"\"{dir.TrimEnd('\\')}\"");
        else Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = false });
    }

    private void SpaceScan_Click(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is InstalledApp { InstallLocation: { } dir } && Ecosystem.Exe("spacescan") is { } exe)
            SettingsWindow.Start(exe, $"\"{dir.TrimEnd('\\')}\"");
    }

    // --- uninstalling ---

    private async void UninstallOne_Click(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is InstalledApp app) await Uninstall([app]);
    }

    private async void UninstallChecked_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var apps = _all.Where(a => a.IsChecked && !a.IsBroken).ToList();
        if (apps.Count == 0 && AppList.SelectedItem is InstalledApp { IsBroken: false } one) apps.Add(one);
        if (apps.Count > 0) await Uninstall(apps);
    }

    private void StopWaiting_Click(object sender, RoutedEventArgs e) => _stopWaiting?.Cancel();

    private async Task Uninstall(List<InstalledApp> apps)
    {
        string what = apps.Count == 1 ? apps[0].Name : $"{apps.Count} apps";
        string how = apps.Count == 1
            ? (apps[0].Kind == AppKind.Store ? "Windows removes it and its data." : "Its own uninstaller will run; follow it if it opens a window.")
            : "Each one's own uninstaller runs in turn; follow them if they open windows.";
        if (!Ask.Confirm($"Uninstall {what}?", how + (Settings.FindLeftovers ? " Afterwards you'll see what they left behind, and choose what goes." : ""), "Uninstall")) return;

        SetBusy(true);
        var removed = new List<InstalledApp>();
        var failed = new List<string>();
        for (int i = 0; i < apps.Count; i++)
        {
            var app = apps[i];
            Status.Text = (apps.Count > 1 ? $"{i + 1} of {apps.Count} · " : "") + $"Uninstalling {app.Name}… waiting for its uninstaller to finish";
            _stopWaiting = new CancellationTokenSource();
            var result = await Uninstaller.Run(app, Settings.Quiet, _stopWaiting.Token);
            AppLog.Write($"Uninstall {app.Name}: {(result.Removed ? "removed" : "still installed")} {result.Error}");
            if (result.Removed) { removed.Add(app); app.IsChecked = false; }
            else failed.Add($"{app.Name}: {result.Error ?? "still installed — its uninstaller was cancelled, failed or is still running."}");
        }
        _stopWaiting = null;

        await AfterRemoval(removed, removed.Count == 1 ? $"{removed[0].Name} is uninstalled" : $"{removed.Count} apps are uninstalled",
            "These were left behind. Each one is named after an app you removed, and nothing still installed uses it.");
        if (failed.Count > 0) Ask.Tell(failed.Count == 1 ? "One app is still installed" : $"{failed.Count} apps are still installed", string.Join("\n", failed));
        SetBusy(false);
    }

    /// <summary>An entry whose uninstaller is gone: its key and what it left, after the user has seen them.</summary>
    private async void RemoveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not InstalledApp app || _busy) return;
        SetBusy(true);
        var others = _all.Where(a => a != app).ToList();
        var found = await Task.Run(() => Leftovers.Find(app, others));
        var window = new LeftoversWindow($"Take {app.Name} off the list", "Its uninstaller is gone, so these are what's left of it: its entry in the list, and anything named after it that nothing else uses.", found);
        window.ShowDialog();
        Report(window.Failed);
        await Reload();
        SetBusy(false);
    }

    private async Task AfterRemoval(List<InstalledApp> removed, string title, string intro)
    {
        Status.Text = "Reading the installed apps again…";
        await Reload();
        if (removed.Count == 0) return;
        var found = new List<Leftover>();
        if (Settings.FindLeftovers)
        {
            Status.Text = "Looking for what they left behind…";
            var remaining = _all;
            found = await Task.Run(() => removed.SelectMany(a => Leftovers.Find(a, remaining)).ToList());
        }
        if (found.Count == 0) { Ask.Tell(title, Settings.FindLeftovers ? "Nothing was left behind." : "Done."); return; }
        var window = new LeftoversWindow(title, intro, found);
        window.ShowDialog();
        Report(window.Failed);
        if (window.Failed != null) await Reload();
    }

    private static void Report(List<string>? failed)
    {
        if (failed is { Count: > 0 }) Ask.Tell("Some things couldn't be removed", string.Join("\n", failed));
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        StopWaiting.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UninstallChecked.IsEnabled = !busy;
        ShowDetails();
        if (!busy) Counts();
    }

    // --- settings and updates ---

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow(CheckNow).ShowDialog();
        _view?.Refresh();
        Counts();
        ShowDetails();
    }

    private void StartUpdateChecks()
    {
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        first.Tick += async (_, _) => { first.Stop(); await CheckQuietly(); };
        first.Start();
        _updateTimer.Tick += async (_, _) => await CheckQuietly();
        _updateTimer.Start();
    }

    private async Task CheckQuietly()
    {
        if (!Settings.AutoUpdate || _update != null) return;
        try { ShowUpdate(await Updater.Check(manual: false)); }
        catch (Exception ex) when (Updater.Failed(ex)) { } // tried again next hour
    }

    private async Task<string> CheckNow()
    {
        try
        {
            var release = await Updater.Check(manual: true);
            ShowUpdate(release);
            return release == null ? $"Uproot {Updater.Current.ToString(3)} is the latest." : $"Version {release.Version} is ready to install.";
        }
        catch (Exception ex) when (Updater.Failed(ex)) { return Updater.CheckFailed; }
    }

    private void ShowUpdate(Updater.Release? release)
    {
        _update = release;
        UpdateBar.Visibility = release == null ? Visibility.Collapsed : Visibility.Visible;
        if (release != null) UpdateText.Text = $"Uproot {release.Version} is available.";
    }

    private void UpdateLater_Click(object sender, RoutedEventArgs e)
    {
        if (_update != null) Updater.Dismiss(_update);
        UpdateBar.Visibility = Visibility.Collapsed;
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_update == null || _busy) return;
        UpdateInstall.IsEnabled = false;
        try
        {
            var file = await Updater.Download(_update, new Progress<double>(p => UpdateText.Text = $"Downloading Uproot {_update.Version}… {p:P0}"), CancellationToken.None);
            Updater.InstallAndRestart(file);
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (Updater.Failed(ex) || ex is IOException)
        {
            UpdateText.Text = $"Couldn't download the update: {ex.Message}";
            UpdateInstall.IsEnabled = true;
        }
    }
}
