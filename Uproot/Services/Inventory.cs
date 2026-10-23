using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;

namespace Uproot.Services;

public enum AppKind { Desktop, Store }

/// <summary>One row of the list: a desktop program (an Uninstall key) or a Store app (an MSIX package).</summary>
public sealed class InstalledApp : INotifyPropertyChanged
{
    public required AppKind Kind { get; init; }
    public required string Name { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public DateTime? Installed { get; init; }
    public string? InstallLocation { get; init; }

    // desktop
    public string? KeyPath { get; init; }             // HKEY_...\...\Uninstall\<key>, always the 64-bit view
    public string? UninstallString { get; init; }
    public string? QuietUninstallString { get; init; }
    public string? MsiProductCode { get; init; }
    public string? IconPath { get; init; }            // "file" or "file,index"
    public bool IsSystem { get; init; }               // SystemComponent or an update of another entry: hidden by default, like Settings
    public string? Broken { get; init; }              // why it can't be uninstalled, when it can't

    // store
    public string? PackageFullName { get; init; }
    public string? PackageFamilyName { get; init; }
    public string? DataFolder { get; init; }

    public long? EstimatedBytes { get; init; }

    private long? _measured, _data;
    private ImageSource? _icon;
    private bool _checked;
    private string? _shelfGame;

    public long? MeasuredBytes { get => _measured; set { _measured = value; Changed(); Changed(nameof(Bytes)); Changed(nameof(SizeText)); } }
    public long? DataBytes { get => _data; set { _data = value; Changed(); Changed(nameof(Bytes)); Changed(nameof(SizeText)); } }
    public ImageSource? Icon { get => _icon; set { _icon = value; Changed(); } }
    public bool IsChecked { get => _checked; set { if (_checked == value) return; _checked = value; Changed(); } }
    public string? ShelfGame { get => _shelfGame; set { _shelfGame = value; Changed(); Changed(nameof(IsShelfGame)); } }
    public bool IsShelfGame => _shelfGame != null;
    public bool IsBroken => Broken != null;

    /// <summary>What it takes on disk: measured when it could be, else what the installer declared.</summary>
    public long? Bytes => _measured is { } m ? m + (_data ?? 0) : EstimatedBytes;
    public string SizeText => Bytes is { } b && b > 0 ? Format.Size(b) : "";
    public string InstalledText => Installed?.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public string KindText => Kind == AppKind.Store ? "Store" : "Desktop";
    public string Subtitle => string.IsNullOrWhiteSpace(Publisher) ? KindText : Publisher!;
    public string Search => $"{Name}\n{Publisher}".ToLowerInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class Inventory
{
    public const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string UninstallPath32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly Regex Guid = new(@"^\{[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\}$", RegexOptions.IgnoreCase);

    /// <summary>Every Uninstall key under HKLM (both views) and HKCU. Registry only: no file is opened, so this is instant.</summary>
    public static List<InstalledApp> Desktop()
    {
        var apps = new List<InstalledApp>();
        var drives = Paths.Drives();
        foreach (var (hive, path) in new[] { (Registry.LocalMachine, UninstallPath), (Registry.LocalMachine, UninstallPath32), (Registry.CurrentUser, UninstallPath) })
        {
            using var root = hive.OpenSubKey(path);
            if (root == null) continue;
            foreach (var keyName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(keyName);
                if (key?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name)) continue;
                var uninstall = Text(key, "UninstallString");
                var msi = key.GetValue("WindowsInstaller") is 1 && Guid.IsMatch(keyName) ? keyName : null;
                apps.Add(new InstalledApp
                {
                    Kind = AppKind.Desktop,
                    Name = name.Trim(),
                    Publisher = Text(key, "Publisher"),
                    Version = Text(key, "DisplayVersion"),
                    Installed = Date(Text(key, "InstallDate")),
                    InstallLocation = Text(key, "InstallLocation")?.Trim('"', ' ').TrimEnd('\\') is { Length: > 0 } l ? l : null,
                    KeyPath = $@"{hive.Name}\{path}\{keyName}",
                    UninstallString = uninstall,
                    QuietUninstallString = Text(key, "QuietUninstallString"),
                    MsiProductCode = msi,
                    IconPath = Text(key, "DisplayIcon"),
                    IsSystem = key.GetValue("SystemComponent") is 1 || key.GetValue("ParentKeyName") != null || key.GetValue("ReleaseType") is string,
                    EstimatedBytes = key.GetValue("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : null,
                    Broken = msi != null ? null : WhyBroken(uninstall, drives),
                });
            }
        }
        // The same program registered twice with the very same uninstaller (seen with some 32/64-bit pairs) is one row.
        return apps.GroupBy(a => (a.Name, a.UninstallString ?? a.KeyPath)).Select(g => g.First()).ToList();
    }

    /// <summary>Store apps for this user: main packages only, without Windows' own (system-signed) ones and frameworks.</summary>
    public static List<InstalledApp> Store()
    {
        var apps = new List<InstalledApp>();
        var packages = new Windows.Management.Deployment.PackageManager().FindPackagesForUser("");
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        foreach (var p in packages)
        {
            try
            {
                if (p.IsFramework || p.IsResourcePackage || p.IsBundle || p.IsOptional) continue;
                if (p.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System) continue;
                string name = Safe(() => p.DisplayName) is { Length: > 0 } dn && !dn.StartsWith("ms-resource:") ? dn : p.Id.Name;
                var v = p.Id.Version;
                apps.Add(new InstalledApp
                {
                    Kind = AppKind.Store,
                    Name = name,
                    Publisher = Safe(() => p.PublisherDisplayName),
                    Version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                    Installed = Safe(() => p.InstalledDate.LocalDateTime) is var d && d.Year > 2000 ? d : null,
                    InstallLocation = Safe(() => p.InstalledLocation?.Path),
                    PackageFullName = p.Id.FullName,
                    PackageFamilyName = p.Id.FamilyName,
                    DataFolder = Path.Combine(data, p.Id.FamilyName),
                    IconPath = Safe(() => p.Logo?.LocalPath),
                });
            }
            catch (Exception ex) { AppLog.Write($"Skipped a package: {ex.Message}"); }
        }
        return apps;
    }

    /// <summary>Is this desktop entry still installed (its key still there)?</summary>
    public static bool StillThere(InstalledApp app)
    {
        if (app.Kind == AppKind.Store)
            return new Windows.Management.Deployment.PackageManager().FindPackageForUser("", app.PackageFullName) != null;
        using var key = Reg.Open(app.KeyPath!);
        return key != null;
    }

    /// <summary>Why an uninstaller can't run: none given, or its file is gone (a deleted folder, a drive that's no longer there).</summary>
    internal static string? WhyBroken(string? uninstall, HashSet<char> drives)
    {
        if (string.IsNullOrWhiteSpace(uninstall)) return "It has no uninstaller.";
        var (file, _) = Commands.Split(Environment.ExpandEnvironmentVariables(uninstall));
        if (!Paths.IsLocal(file)) return null;                                   // a bare name (msiexec, rundll32) or a network path: not ours to judge
        char drive = char.ToUpperInvariant(file[0]);
        if (!drives.Contains(drive)) return $"Its uninstaller was on drive {drive}:, which isn't connected.";
        return File.Exists(file) ? null : "Its uninstaller has been deleted.";
    }

    private static string? Text(RegistryKey key, string name) => key.GetValue(name) is string s && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static DateTime? Date(string? yyyymmdd) =>
        yyyymmdd != null && DateTime.TryParseExact(yyyymmdd, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    private static T? Safe<T>(Func<T> read) { try { return read(); } catch { return default; } } // a broken package's manifest throws on any property
}

public static class Reg
{
    /// <summary>Opens "HKEY_LOCAL_MACHINE\..." or "HKEY_CURRENT_USER\..."; null when it isn't there.</summary>
    public static RegistryKey? Open(string fullPath, bool writable = false)
    {
        var (hive, sub) = Parse(fullPath);
        return hive?.OpenSubKey(sub, writable);
    }

    public static (RegistryKey? Hive, string Sub) Parse(string fullPath)
    {
        int slash = fullPath.IndexOf('\\');
        var hive = fullPath[..slash] switch { "HKEY_LOCAL_MACHINE" => Registry.LocalMachine, "HKEY_CURRENT_USER" => Registry.CurrentUser, _ => null };
        return (hive, fullPath[(slash + 1)..]);
    }
}

public static class Paths
{
    /// <summary>A plain local path, "X:\...". UNC and device paths are refused before anything touches them:
    /// even File.Exists on \\host\... sends the user's NTLM hash to that host.</summary>
    public static bool IsLocal(string? path) =>
        path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';

    /// <summary>Drive letters present, plus the user's mapped network drives: an elevated process doesn't see those
    /// mappings, and a game on W: is not "broken" just because this window can't see W:.</summary>
    public static HashSet<char> Drives()
    {
        var set = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        using var network = Registry.CurrentUser.OpenSubKey("Network");
        foreach (var letter in network?.GetSubKeyNames() ?? []) if (letter.Length == 1) set.Add(char.ToUpperInvariant(letter[0]));
        return set;
    }

    /// <summary>Drive letters that are network drives (mapped or seen as such): never measured, never cleaned.</summary>
    public static HashSet<char> NetworkDrives()
    {
        var set = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Network).Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        using var network = Registry.CurrentUser.OpenSubKey("Network");
        foreach (var letter in network?.GetSubKeyNames() ?? []) if (letter.Length == 1) set.Add(char.ToUpperInvariant(letter[0]));
        return set;
    }

    /// <summary>a is b or inside b.</summary>
    public static bool Within(string a, string b)
    {
        a = a.TrimEnd('\\'); b = b.TrimEnd('\\');
        return a.Equals(b, StringComparison.OrdinalIgnoreCase) || a.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase);
    }
}

public static class Format
{
    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };
}
