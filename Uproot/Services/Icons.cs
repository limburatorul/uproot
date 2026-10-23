using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Uproot.Services;

/// <summary>Each app's icon, loaded after the list is up: DisplayIcon ("file,index") for desktop apps, the package logo for Store apps.</summary>
public static class Icons
{
    public static void LoadAll(IReadOnlyList<InstalledApp> apps, Action<Action> onUi, CancellationToken token)
    {
        var drives = Paths.Drives();
        var network = Paths.NetworkDrives();
        foreach (var app in apps)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                var image = app.Kind == AppKind.Store ? Logo(app.IconPath) : FromFile(Source(app), drives, network);
                if (image != null) onUi(() => app.Icon = image);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or COMException or ArgumentException) { }
        }
    }

    /// <summary>The file to take the icon from: DisplayIcon, else the uninstaller (which usually carries the app's icon).</summary>
    private static (string File, int Index)? Source(InstalledApp app)
    {
        if (app.IconPath is { } icon)
        {
            var text = Environment.ExpandEnvironmentVariables(icon).Trim();
            int index = 0;
            int comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), out var i)) { index = i; text = text[..comma]; }
            return (text.Trim().Trim('"'), index);
        }
        if (app.UninstallString is { } u && Commands.Split(Environment.ExpandEnvironmentVariables(u)).File is var file && Paths.IsLocal(file)) return (file, 0);
        return null;
    }

    private static ImageSource? FromFile((string File, int Index)? source, HashSet<char> drives, HashSet<char> network)
    {
        if (source is not { } s || !Paths.IsLocal(s.File)) return null;
        char drive = char.ToUpperInvariant(s.File[0]);
        if (!drives.Contains(drive) || network.Contains(drive) || !File.Exists(s.File)) return null;
        if (s.File.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || s.File.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) return Bitmap(s.File);

        var large = new IntPtr[1];
        if (ExtractIconEx(s.File, s.Index, large, null, 1) == 0 || large[0] == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally { DestroyIcon(large[0]); }
    }

    /// <summary>A package logo; the manifest names "StoreLogo.png", the folder holds "StoreLogo.scale-100.png" and friends.</summary>
    private static ImageSource? Logo(string? path)
    {
        if (!Paths.IsLocal(path)) return null;
        if (File.Exists(path)) return Bitmap(path!);
        var dir = Path.GetDirectoryName(path!);
        if (dir == null || !Directory.Exists(dir)) return null;
        var stem = Path.GetFileNameWithoutExtension(path!);
        var best = Directory.EnumerateFiles(dir, stem + ".*" + Path.GetExtension(path!))
            .OrderBy(f => f.Contains("scale-100") ? 0 : f.Contains("targetsize-32") ? 1 : f.Contains("scale-200") ? 2 : 3).FirstOrDefault();
        return best == null ? null : Bitmap(best);
    }

    private static ImageSource Bitmap(string file)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 64;
        image.UriSource = new Uri(file);
        image.EndInit();
        image.Freeze();
        return image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
