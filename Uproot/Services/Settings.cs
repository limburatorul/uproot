using Microsoft.Win32;

namespace Uproot.Services;

/// <summary>The few choices Uproot remembers, in HKCU\Software\Uproot.</summary>
public static class Settings
{
    private const string Path = @"Software\Uproot";

    /// <summary>Use the app's silent uninstall when it offers one (MSI: a progress bar only).</summary>
    public static bool Quiet { get => Get("QuietUninstall", false); set => Set("QuietUninstall", value); }
    /// <summary>After an app is removed, look for the folders and keys it left.</summary>
    public static bool FindLeftovers { get => Get("FindLeftovers", true); set => Set("FindLeftovers", value); }
    /// <summary>List Windows components and updates too (hidden by Windows' own list).</summary>
    public static bool ShowSystem { get => Get("ShowSystem", false); set => Set("ShowSystem", value); }
    public static bool AutoUpdate { get => Get("AutoUpdate", true); set => Set("AutoUpdate", value); }

    public static string? LastSeenVersion
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(Path); return key?.GetValue("LastSeenVersion") as string; }
        set { using var key = Registry.CurrentUser.CreateSubKey(Path); key.SetValue("LastSeenVersion", value ?? ""); }
    }

    private static bool Get(string name, bool fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path);
        return key?.GetValue(name) is int v ? v != 0 : fallback;
    }

    private static void Set(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path);
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }
}
