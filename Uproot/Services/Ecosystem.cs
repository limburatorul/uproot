using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Uproot.Services;

/// <summary>
/// The folder the Protagonist Labs apps share on this PC (%APPDATA%\ProtagonistLabs, vault Ecosistem.md): Uproot
/// writes its card, reads the others' cards for Works with and the sister links, and Shelf's games to mark them in
/// the list. Anything read from here may be missing or broken — that's just "not there".
/// </summary>
public static class Ecosystem
{
    public static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProtagonistLabs");
    public const string Page = "https://protagonistlabs.app/uproot/";

    public static void WriteCard(string exe, Version version) => WriteJson(Path.Combine(Folder, "apps", "uproot.json"), new JsonObject
    {
        ["id"] = "uproot", ["name"] = "Uproot", ["version"] = version.ToString(3), ["exe"] = exe,
        ["edition"] = "free", ["paid"] = false, ["page"] = Page, ["commands"] = new JsonObject(),
    });

    /// <summary>Another app's exe from its card, or null when the card is missing, unreadable, or its exe is gone.</summary>
    public static string? Exe(string id) =>
        Read(Path.Combine(Folder, "apps", id + ".json"))?["exe"] is JsonValue v && v.TryGetValue(out string? exe) && Paths.IsLocal(exe) && File.Exists(exe) ? exe : null;

    /// <summary>Someone bought a Protagonist Labs app: the discount line in Works with may be shown.</summary>
    public static bool BoughtSomething()
    {
        try
        {
            return Directory.GetFiles(Path.Combine(Folder, "apps"), "*.json").Any(f =>
                Read(f) is { } c && Exe(Path.GetFileNameWithoutExtension(f)) != null
                && c["paid"] is JsonValue p && p.TryGetValue(out bool paid) && paid
                && c["edition"] is JsonValue e && e.TryGetValue(out string? edition) && edition == "full");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Shelf's games as (name, install folder), local folders only; empty without Shelf.</summary>
    public static List<(string Name, string Folder)> ShelfGames()
    {
        var list = new List<(string, string)>();
        if (Exe("shelf") == null) return list;
        if (Read(Path.Combine(Folder, "data", "shelf.games.json"))?["games"] is not JsonArray games) return list;
        foreach (var g in games.OfType<JsonObject>())
            if (g["name"] is JsonValue n && n.TryGetValue(out string? name) && g["installDir"] is JsonValue d && d.TryGetValue(out string? dir) && Paths.IsLocal(dir))
                list.Add((name!, dir!.TrimEnd('\\')));
        return list;
    }

    /// <summary>A shared file as JSON, or null if it isn't a schema-1 object (or is unreasonably big).</summary>
    public static JsonObject? Read(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length < 32 << 20 && JsonNode.Parse(File.ReadAllText(path)) is JsonObject o
                   && o["schema"] is JsonValue v && v.TryGetValue(out int schema) && schema == 1 ? o : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }

    private static void WriteJson(string path, JsonObject body)
    {
        var file = new JsonObject { ["schema"] = 1, ["updated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) };
        foreach (var (key, value) in body.ToList()) { body.Remove(key); file[key] = value; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", file.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); // UTF-8, no BOM
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
