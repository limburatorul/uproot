using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Uproot.Services;

public sealed class Leftover : INotifyPropertyChanged
{
    public required string App { get; init; }
    public required bool IsFolder { get; init; }
    public required string Path { get; init; }
    public long Bytes { get; init; }
    public string KindText => IsFolder ? "Folder" : "Registry";
    public string SizeText => IsFolder ? Format.Size(Bytes) : "";
    private bool _checked = true;
    public bool IsChecked { get => _checked; set { _checked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// What an uninstalled app left behind. Deliberately narrow: a folder or key counts only when its name is the app's
/// name (or the publisher's folder with the app's name inside), it sits in one of the places apps keep things, and no
/// app still installed lives in it. Folders go to the Recycle Bin, keys are exported to a .reg file first.
/// </summary>
public static class Leftovers
{
    public const long BigFolder = 2L << 30;

    public static string BackupFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProtagonistLabs", "Uproot", "Backups");

    // Names that are folders' jobs, vendors shared by many apps, or Windows' own: never a match on their own.
    private static readonly HashSet<string> Generic = new(StringComparer.Ordinal)
    {
        "common", "commonfiles", "shared", "data", "bin", "app", "apps", "tools", "update", "updates", "updater", "temp", "tmp",
        "cache", "logs", "plugins", "games", "game", "program", "programs", "launcher", "setup", "installer", "microsoft", "windows",
        "intel", "amd", "nvidia", "nvidiacorporation", "google", "mozilla", "adobe", "apple", "packages", "classes", "policies",
        "clients", "wow6432node", "registeredapplications", "system", "software", "users", "default", "settings", "config",
    };

    public static List<Leftover> Find(InstalledApp app, IReadOnlyList<InstalledApp> stillInstalled)
    {
        var found = new List<Leftover>();
        if (app.Kind == AppKind.Store) return found; // Windows removes a package's data with it

        var strong = Names(app);
        var names = new HashSet<string>(strong);
        if (app.InstallLocation != null && Norm(System.IO.Path.GetFileName(app.InstallLocation)) is { Length: >= 4 } own && !Generic.Contains(own)) names.Add(own);
        string? publisher = Norm(Clean(app.Publisher ?? ""));
        if (publisher is { Length: < 3 } || (publisher != null && names.Contains(publisher))) publisher = null;
        var owned = Owned(stillInstalled);
        var network = Paths.NetworkDrives();

        // folders
        string E(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var roots = new[]
        {
            E(Environment.SpecialFolder.ProgramFiles), E(Environment.SpecialFolder.ProgramFilesX86), E(Environment.SpecialFolder.CommonApplicationData),
            E(Environment.SpecialFolder.ApplicationData), E(Environment.SpecialFolder.LocalApplicationData),
            System.IO.Path.Combine(E(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            foreach (var dir in Children(root))
            {
                var n = Norm(System.IO.Path.GetFileName(dir));
                if (names.Contains(n)) folders.Add(dir);
                else if (publisher != null && n == publisher)
                    foreach (var sub in Children(dir)) if (names.Contains(Norm(System.IO.Path.GetFileName(sub)))) folders.Add(sub);
            }
        // where the registry said it lives, and where its uninstaller was: a folder right under a drive (D:\Stuff) only
        // when it carries the app's own name, anything deeper (D:\Games\Foo, C:\Program Files\Foo) as given
        foreach (var place in new[] { app.InstallLocation, UninstallerFolder(app) })
            if (Paths.IsLocal(place) && Directory.Exists(place)
                && (strong.Contains(Norm(System.IO.Path.GetFileName(place!))) || place!.TrimEnd('\\').Count(c => c == '\\') >= 2)) folders.Add(place!.TrimEnd('\\'));

        foreach (var dir in folders)
        {
            if (!Paths.IsLocal(dir) || network.Contains(char.ToUpperInvariant(dir[0])) || Sizes.TooBroad(dir)) continue;
            if (owned.Any(p => Paths.Within(p, dir) || Paths.Within(dir, p))) continue;   // another app lives in it, or it lives in another app
            long bytes = 0;
            try { bytes = Sizes.Folder(dir, CancellationToken.None); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            // a big folder starts unticked: past the Recycle Bin's size it would be deleted for good
            found.Add(new Leftover { App = app.Name, IsFolder = true, Path = dir, Bytes = bytes, IsChecked = bytes < BigFolder });
        }

        // registry: Software\<name> and Software\<publisher>\<name>, both hives and views, and the Uninstall entry if it stayed
        var otherNames = stillInstalled.SelectMany(Names).ToHashSet();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var soft in hive == Registry.CurrentUser ? new[] { "Software" } : new[] { "SOFTWARE", @"SOFTWARE\WOW6432Node" })
            {
                using var software = hive.OpenSubKey(soft);
                if (software == null) continue;
                foreach (var keyName in software.GetSubKeyNames())
                {
                    var n = Norm(keyName);
                    if (names.Contains(n) && !otherNames.Contains(n)) found.Add(Key(app, $@"{hive.Name}\{soft}\{keyName}"));
                    else if (publisher != null && n == publisher)
                    {
                        using var vendor = software.OpenSubKey(keyName);
                        foreach (var sub in vendor?.GetSubKeyNames() ?? [])
                            if (names.Contains(Norm(sub)) && !otherNames.Contains(Norm(sub))) found.Add(Key(app, $@"{hive.Name}\{soft}\{keyName}\{sub}"));
                    }
                }
            }
        if (app.KeyPath != null && Inventory.StillThere(app)) found.Add(Key(app, app.KeyPath));
        return found;
    }

    private static Leftover Key(InstalledApp app, string path) => new() { App = app.Name, IsFolder = false, Path = path };

    /// <summary>Removes what's checked. Returns what couldn't be removed, with why.</summary>
    public static List<string> Remove(IEnumerable<Leftover> items)
    {
        var failed = new List<string>();
        string backups = System.IO.Path.Combine(BackupFolder, DateTime.Now.ToString("yyyy-MM-dd HHmmss"));
        int n = 0;
        foreach (var item in items)
        {
            try
            {
                if (item.IsFolder)
                {
                    if (!Recycle(item.Path)) { failed.Add($"{item.Path}: couldn't move it to the Recycle Bin"); continue; }
                    // the publisher's folder, now empty, goes too
                    var parent = System.IO.Path.GetDirectoryName(item.Path);
                    if (parent != null && !Sizes.TooBroad(parent) && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
                }
                else
                {
                    Directory.CreateDirectory(backups);
                    var file = System.IO.Path.Combine(backups, $"{++n:00} {Safe(item.App)}.reg");
                    using (var reg = Process.Start(new ProcessStartInfo("reg.exe", $"export \"{item.Path}\" \"{file}\" /y") { CreateNoWindow = true, UseShellExecute = false })!)
                        reg.WaitForExit();
                    if (!File.Exists(file)) { failed.Add($"{item.Path}: couldn't back it up, so it was left alone"); continue; }
                    var (hive, sub) = Reg.Parse(item.Path);
                    hive?.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            {
                failed.Add($"{item.Path}: {ex.Message}");
            }
        }
        return failed;
    }

    /// <summary>Names the app might use for its folders and keys, normalised.</summary>
    public static HashSet<string> Names(InstalledApp app)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? s) { var n = Norm(Clean(s ?? "")); if (n.Length >= 4 && !Generic.Contains(n)) names.Add(n); }
        Add(app.Name);
        if (app.KeyPath != null && System.IO.Path.GetFileName(app.KeyPath) is var key && !key.StartsWith('{')) Add(Regex.Replace(key, "_is1$", ""));
        return names;
    }

    /// <summary>"Foo Bar 2.1.0 (x64)" → "Foo Bar"; "Acme, Inc." → "Acme".</summary>
    public static string Clean(string name)
    {
        name = Regex.Replace(name, @"\s*[\(\[][^\)\]]*[\)\]]", "");                                             // (x64), [Beta]
        name = Regex.Replace(name, @"\s+(version\s+)?v?\d+(\.\d+)+.*$", "", RegexOptions.IgnoreCase);           // 2.1.0 and anything after
        name = Regex.Replace(name, @"\s+(x64|x86|64-bit|32-bit)$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @",?\s+(inc|ltd|llc|gmbh|corp|corporation|co|s\.?r\.?l|limited)\.?$", "", RegexOptions.IgnoreCase);
        return name.Trim();
    }

    public static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string? UninstallerFolder(InstalledApp app)
    {
        if (app.UninstallString == null) return null;
        var (file, _) = Commands.Split(Environment.ExpandEnvironmentVariables(app.UninstallString));
        return Paths.IsLocal(file) ? System.IO.Path.GetDirectoryName(file) : null;
    }

    /// <summary>Every folder an installed app is known to use: its location, its uninstaller's and its icon's folders.</summary>
    private static List<string> Owned(IReadOnlyList<InstalledApp> apps)
    {
        var owned = new List<string>();
        foreach (var a in apps)
        {
            if (Paths.IsLocal(a.InstallLocation)) owned.Add(a.InstallLocation!);
            if (UninstallerFolder(a) is { } u) owned.Add(u);
            if (a.IconPath?.Split(',')[0].Trim('"') is { } icon && Paths.IsLocal(icon) && System.IO.Path.GetDirectoryName(icon) is { } d) owned.Add(d);
        }
        return owned;
    }

    private static IEnumerable<string> Children(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static string Safe(string name) => string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static bool Recycle(string path)
    {
        var op = new ShFileOp { wFunc = 3 /* FO_DELETE */, pFrom = path + "\0\0", fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400 }; // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted && !Directory.Exists(path);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOp op);
}
