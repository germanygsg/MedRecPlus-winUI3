using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedRecPlus.Core.Services;

public sealed record SecuritySettings
{
    [JsonPropertyName("require_password")]
    public bool RequirePassword { get; init; } = true;
    [JsonPropertyName("autolock_minutes")]
    public int AutoLockMinutes { get; init; } = 15;
}

// These are deliberately NOT SettingKeys: startup needs them before DB unlock.
// No secrets belong here. A wipe preserves this file; setup resets the defaults.
public static class LocalAppSettings
{
    public static string FilePath => Path.Combine(AppData.BaseDir, "app.json");

    public static SecuritySettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var settings = JsonSerializer.Deserialize<SecuritySettings>(File.ReadAllText(FilePath))
                ?? throw new JsonException("Empty settings.");
            return settings with { AutoLockMinutes = Math.Clamp(settings.AutoLockMinutes, 0, 60) };
        }
        catch (Exception)
        {
            var defaults = new SecuritySettings();
            try { Save(defaults); } catch (Exception) { /* Read-only storage must not block startup. */ }
            return defaults;
        }
    }

    public static void Save(SecuritySettings settings)
    {
        Directory.CreateDirectory(AppData.BaseDir);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings with
        {
            AutoLockMinutes = Math.Clamp(settings.AutoLockMinutes, 0, 60),
        }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
