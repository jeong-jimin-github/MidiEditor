using System.IO;
using System.Text.Json;

namespace MidiEditor.Services;

public static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PulseGrid",
        "settings.json");

    public static string? LoadLastSoundFontPath(string? settingsPath = null)
    {
        try
        {
            var path = settingsPath ?? SettingsPath;
            if (!File.Exists(path))
                return null;
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            return string.IsNullOrWhiteSpace(settings?.LastSoundFontPath) ? null : settings.LastSoundFontPath;
        }
        catch
        {
            // A damaged preference file must never prevent the editor from starting.
            return null;
        }
    }

    public static void SaveLastSoundFontPath(string? soundFontPath, string? settingsPath = null)
    {
        try
        {
            var path = settingsPath ?? SettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var settings = new AppSettings { LastSoundFontPath = soundFontPath };
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // SoundFont usage still works when preferences cannot be persisted.
        }
    }

    private sealed class AppSettings
    {
        public string? LastSoundFontPath { get; set; }
    }
}
