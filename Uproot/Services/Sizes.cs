using System.IO;
using System.IO.Enumeration;

namespace Uproot.Services;

/// <summary>
/// What each app really takes, measured off the UI thread after the list is up. Windows' Installed apps page opens
/// every file of an app to size it (on this PC: 224,000 files in one Store app's data, ~90 s of CPU, a list that
/// took 10 s to answer a search). Reading the directory entries gives the same sizes without opening a file.
/// </summary>
public static class Sizes
{
    private static readonly EnumerationOptions Deep = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint, // junctions would count a folder twice, or loop; hidden files do count
    };

    public static long Folder(string path, CancellationToken token)
    {
        long total = 0;
        var files = new FileSystemEnumerable<long>(path, (ref FileSystemEntry e) => e.Length, Deep)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory,
        };
        foreach (var length in files)
        {
            total += length;
            if ((total & 0xFFF) == 0) token.ThrowIfCancellationRequested();
        }
        return total;
    }

    /// <summary>Measures every app with a local folder, one at a time at low priority; sets MeasuredBytes / DataBytes as it goes.</summary>
    public static void MeasureAll(IReadOnlyList<InstalledApp> apps, Action<Action> onUi, CancellationToken token)
    {
        var network = Paths.NetworkDrives();
        bool Usable(string? p) => Paths.IsLocal(p) && !network.Contains(char.ToUpperInvariant(p![0])) && !TooBroad(p) && Directory.Exists(p);
        foreach (var app in apps)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                long? install = Usable(app.InstallLocation) ? Folder(app.InstallLocation!, token) : null;
                long? data = app.Kind == AppKind.Store && Usable(app.DataFolder) ? Folder(app.DataFolder!, token) : null;
                if (install is null && data is null) continue;
                onUi(() => { app.DataBytes = data; app.MeasuredBytes = install ?? 0; });
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>An install location that is really a shared parent (a drive, Program Files, a profile) is not the app's size.</summary>
    public static bool TooBroad(string path) => Protected.Any(p => Paths.Within(p, path)) || path.TrimEnd('\\').Length <= 3;

    /// <summary>Folders no app owns alone: never measured as an app, never offered as a leftover, nor anything above them.</summary>
    public static readonly string[] Protected = BuildProtected();

    private static string[] BuildProtected()
    {
        string E(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        string local = E(Environment.SpecialFolder.LocalApplicationData), roaming = E(Environment.SpecialFolder.ApplicationData);
        string pf = E(Environment.SpecialFolder.ProgramFiles), pf86 = E(Environment.SpecialFolder.ProgramFilesX86), data = E(Environment.SpecialFolder.CommonApplicationData);
        return new[]
        {
            E(Environment.SpecialFolder.Windows), pf, pf86, data, E(Environment.SpecialFolder.UserProfile), local, roaming,
            Path.Combine(pf, "Common Files"), Path.Combine(pf86, "Common Files"), Path.Combine(pf, "WindowsApps"),
            Path.Combine(data, "Microsoft"), Path.Combine(local, "Microsoft"), Path.Combine(roaming, "Microsoft"),
            Path.Combine(local, "Packages"), Path.Combine(local, "Programs"), Path.Combine(local, "Temp"),
            E(Environment.SpecialFolder.MyDocuments), E(Environment.SpecialFolder.Desktop), E(Environment.SpecialFolder.CommonDocuments),
            Path.Combine(E(Environment.SpecialFolder.UserProfile), "Downloads"),
        }.Where(p => p.Length > 0).ToArray();
    }
}
