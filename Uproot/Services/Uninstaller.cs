using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Management.Deployment;

namespace Uproot.Services;

public static class Commands
{
    /// <summary>
    /// A command line as file + arguments. Uninstall strings come quoted ("C:\x\unins000.exe" /x), bare
    /// (MsiExec.exe /X{...}) or unquoted with spaces in the path (C:\Program Files\x\uninst.exe /S), so the
    /// longest unquoted prefix that ends in .exe and exists wins over the first space.
    /// </summary>
    public static (string File, string Args) Split(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0) return (command[1..end], command[(end + 1)..].Trim());
        }
        if (Paths.IsLocal(command))
        {
            for (int at = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); at > 0; at = command.IndexOf(".exe", at + 4, StringComparison.OrdinalIgnoreCase))
            {
                var file = command[..(at + 4)];
                if (at + 4 == command.Length || command[at + 4] == ' ')
                    if (File.Exists(file) || command.IndexOf(".exe", at + 4, StringComparison.OrdinalIgnoreCase) < 0) return (file, command[(at + 4)..].Trim());
            }
        }
        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }
}

public static class Uninstaller
{
    public sealed record Result(bool Removed, string? Error);

    /// <summary>The command that will run for this app, as shown in the details and used by Run.</summary>
    public static string? CommandFor(InstalledApp app, bool quiet)
    {
        if (app.Kind == AppKind.Store) return null;
        if (app.MsiProductCode != null) return $"MsiExec.exe /X{app.MsiProductCode}" + (quiet ? " /qb /norestart" : "");
        if (quiet && app.QuietUninstallString != null) return app.QuietUninstallString;
        return app.UninstallString;
    }

    /// <summary>
    /// Runs the app's own uninstaller and waits for all of it: many hand off to a copy of themselves in %TEMP%
    /// (NSIS, Inno) and exit at once, so the first process ending means nothing. Everything it starts goes into a
    /// job object, and the wait ends when the job is empty — or when the entry is gone and a few seconds have
    /// passed (some uninstallers end by opening a web page, whose browser would otherwise keep the job alive), or
    /// when the user stops waiting.
    /// </summary>
    public static async Task<Result> Run(InstalledApp app, bool quiet, CancellationToken stopWaiting)
    {
        if (app.Kind == AppKind.Store)
        {
            try
            {
                var outcome = await new PackageManager().RemovePackageAsync(app.PackageFullName).AsTask(stopWaiting);
                return outcome.ExtendedErrorCode is { } e && e.HResult != 0 ? new(false, outcome.ErrorText) : new(true, null);
            }
            catch (OperationCanceledException) { return new(!Inventory.StillThere(app), null); }
            catch (Exception ex) { return new(false, ex.Message); }
        }

        var command = CommandFor(app, quiet);
        if (command == null) return new(false, app.Broken ?? "It has no uninstaller.");
        var (file, args) = Commands.Split(Environment.ExpandEnvironmentVariables(command));

        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        try
        {
            Process process;
            try
            {
                process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, WorkingDirectory = WorkingFolder(file) })!;
            }
            catch (Win32Exception ex) { return new(false, $"Couldn't start {Path.GetFileName(file)}: {ex.Message}"); }
            // ponytail: a child started in the first instant, before the process joins the job, isn't waited for; the
            // registry check below still catches the end of the uninstall.
            AssignProcessToJobObject(job, process.Handle);

            DateTime? goneSince = null;
            while (!stopWaiting.IsCancellationRequested)
            {
                if (ActiveProcesses(job) == 0 && process.HasExited) break;
                if (!Inventory.StillThere(app)) { goneSince ??= DateTime.UtcNow; if (DateTime.UtcNow - goneSince > TimeSpan.FromSeconds(4)) break; }
                try { await Task.Delay(400, stopWaiting); } catch (OperationCanceledException) { }
            }
            return new(!Inventory.StillThere(app), null);
        }
        finally { CloseHandle(job); } // no KILL_ON_JOB_CLOSE: whatever it left running (a browser) keeps running
    }

    private static string WorkingFolder(string file) =>
        Paths.IsLocal(file) && Path.GetDirectoryName(file) is { } dir && Directory.Exists(dir) ? dir : Environment.SystemDirectory;

    private static int ActiveProcesses(IntPtr job) =>
        QueryInformationJobObject(job, 1, out var info, Marshal.SizeOf<BasicAccounting>(), IntPtr.Zero) ? (int)info.ActiveProcesses : 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicAccounting
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, out BasicAccounting info, int length, IntPtr returned);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
