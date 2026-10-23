using System.IO;
using Microsoft.Win32;
using Uproot.Services;

namespace Uproot;

/// <summary>
/// Uproot.exe --selftest: the logic that decides what runs and what gets removed, checked on this PC with a fake app
/// (an Uninstall entry in HKCU, folders and keys named after it) that is cleaned up afterwards. Exit code = failures.
/// </summary>
public static class SelfTest
{
    private static int _failed;
    private static StreamWriter? _log;

    public static int Run()
    {
        _log = new StreamWriter(Path.Combine(Path.GetTempPath(), "uproot-selftest.txt")) { AutoFlush = true };

        // command lines
        Check(Commands.Split("\"C:\\x y\\unins000.exe\" /SILENT") == ("C:\\x y\\unins000.exe", "/SILENT"), "quoted command");
        Check(Commands.Split("MsiExec.exe /X{1}") == ("MsiExec.exe", "/X{1}"), "bare command");
        string sys = Environment.SystemDirectory;
        Check(Commands.Split($"{sys}\\cmd.exe /c echo") == ($"{sys}\\cmd.exe", "/c echo"), "unquoted path that exists");
        Check(Commands.Split("C:\\Program Files\\Gone App\\uninst.exe /S") == ("C:\\Program Files\\Gone App\\uninst.exe", "/S"), "unquoted path with spaces that is gone");

        // broken entries
        var drives = Paths.Drives();
        char missing = "QRSTUVWXYZHIJKLMNOP".First(c => !drives.Contains(c));
        Check(Inventory.WhyBroken(null, drives) == "It has no uninstaller.", "no uninstaller");
        Check(Inventory.WhyBroken($"{missing}:\\Games\\x\\unins000.exe", drives)?.Contains($"drive {missing}:") == true, "uninstaller on a missing drive");
        Check(Inventory.WhyBroken("C:\\Nope\\uninst.exe", drives) == "Its uninstaller has been deleted.", "uninstaller deleted");
        Check(Inventory.WhyBroken("MsiExec.exe /X{1}", drives) == null, "msiexec is not judged");
        Check(Inventory.WhyBroken("\\\\host\\share\\u.exe", drives) == null, "a network path is never touched");

        // names and safety
        Check(Leftovers.Clean("Foo Bar 2.1.0 (x64)") == "Foo Bar", "clean name with version and arch");
        Check(Leftovers.Clean("Acme, Inc.") == "Acme", "clean publisher");
        Check(Leftovers.Clean("Fallout 4") == "Fallout 4", "a single number stays");
        Check(Sizes.TooBroad("C:\\") && Sizes.TooBroad(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) && Sizes.TooBroad("C:\\Users"), "shared folders are too broad");
        Check(!Sizes.TooBroad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Foo")), "an app folder is not");
        Check(Paths.Within("C:\\a\\b", "C:\\a") && !Paths.Within("C:\\ab", "C:\\a"), "within");

        FakeApp();
        _log.WriteLine(_failed == 0 ? "ALL OK" : $"{_failed} FAILED");
        _log.Dispose();
        return _failed;
    }

    /// <summary>
    /// A fake app whose uninstaller hands off to a child and exits at once, as NSIS and Inno do: Run must wait for the
    /// child. Then its leftovers: its folders and keys are found, a folder another installed app lives in is not.
    /// </summary>
    private static void FakeApp()
    {
        const string name = "Uproot Selftest Fake";
        string key = $@"{Inventory.UninstallPath}\UprootSelftestFake";
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(local, "Uproot Selftest Fake");
        string shared = Path.Combine(local, "Uproot Selftest Host");
        string keyHkcu = $@"Software\Uproot Selftest Fake";
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "data.bin"), new string('x', 5000));
            Directory.CreateDirectory(Path.Combine(shared, "Uproot Selftest Fake"));   // inside another (installed) app's folder
            Registry.CurrentUser.CreateSubKey(keyHkcu)!.Dispose();
            // the uninstaller: starts a child that removes the entry 3 s later, and exits immediately
            string cmd = $"cmd.exe /c start \"\" /b cmd.exe /c \"ping -n 4 127.0.0.1 >nul & reg delete HKCU\\{key} /f >nul\"";
            using (var k = Registry.CurrentUser.CreateSubKey(key)!) { k.SetValue("DisplayName", name); k.SetValue("UninstallString", cmd); k.SetValue("InstallLocation", folder); }
            using (var k = Registry.CurrentUser.CreateSubKey($@"{Inventory.UninstallPath}\UprootSelftestHost")!) { k.SetValue("DisplayName", "Uproot Selftest Host"); k.SetValue("UninstallString", "x"); k.SetValue("InstallLocation", shared); }

            var app = Inventory.Desktop().FirstOrDefault(a => a.Name == name);
            Check(app != null && app.InstallLocation == folder && !app.IsBroken, "the fake app is listed");
            if (app == null) return;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = Task.Run(() => Uninstaller.Run(app, quiet: false, CancellationToken.None)).GetAwaiter().GetResult(); // off the UI thread: its awaits would wait for it
            Check(result.Removed && watch.Elapsed.TotalSeconds >= 2.5, $"waited for the child uninstaller ({watch.Elapsed.TotalSeconds:0.0} s, removed {result.Removed})");

            var remaining = Inventory.Desktop();
            var found = Leftovers.Find(app, remaining);
            foreach (var f in found) _log!.WriteLine($"   leftover: {f.Path} {f.SizeText}");
            Check(found.Any(f => f.IsFolder && f.Path == folder && f.Bytes == 5000), "its folder is found, with its size");
            Check(found.Any(f => !f.IsFolder && f.Path.EndsWith(keyHkcu, StringComparison.OrdinalIgnoreCase)), "its key is found");
            Check(!found.Any(f => f.Path.StartsWith(shared, StringComparison.OrdinalIgnoreCase)), "a folder inside another installed app is not");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(key, false);
            Registry.CurrentUser.DeleteSubKeyTree($@"{Inventory.UninstallPath}\UprootSelftestHost", false);
            Registry.CurrentUser.DeleteSubKeyTree(keyHkcu, false);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            if (Directory.Exists(shared)) Directory.Delete(shared, true);
        }
    }

    private static void Check(bool ok, string what)
    {
        _log!.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) _failed++;
    }
}
