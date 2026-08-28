using System.IO;
using System.Text.Json;

namespace MidiEditor.Services;

public sealed class VocalToolSettings
{
    public string? VoicebankRootPath { get; set; }
    public string? OpenUtauPath { get; set; }
    public string? ResamplerPath { get; set; }
    public string? WavtoolPath { get; set; }

    public VocalToolSettings Clone() => new()
    {
        VoicebankRootPath = VoicebankRootPath,
        OpenUtauPath = OpenUtauPath,
        ResamplerPath = ResamplerPath,
        WavtoolPath = WavtoolPath
    };
}

public static class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PulseGrid",
        "settings.json");

    public static string? LoadLastSoundFontPath(string? settingsPath = null) =>
        Load(settingsPath).LastSoundFontPath;

    public static void SaveLastSoundFontPath(string? soundFontPath, string? settingsPath = null)
    {
        var settings = Load(settingsPath);
        settings.LastSoundFontPath = string.IsNullOrWhiteSpace(soundFontPath) ? null : soundFontPath;
        Save(settings, settingsPath);
    }

    public static VocalToolSettings LoadVocalSettings(string? settingsPath = null)
    {
        var source = Load(settingsPath).Vocal ?? new VocalToolSettings();
        return source.Clone();
    }

    public static void SaveVocalSettings(VocalToolSettings vocalSettings, string? settingsPath = null)
    {
        ArgumentNullException.ThrowIfNull(vocalSettings);
        var settings = Load(settingsPath);
        settings.Vocal = vocalSettings.Clone();
        Save(settings, settingsPath);
    }

    private static AppSettings Load(string? settingsPath)
    {
        try
        {
            var path = settingsPath ?? SettingsPath;
            if (!File.Exists(path))
                return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            // A damaged preference file must never prevent the editor from starting.
            return new AppSettings();
        }
    }

    private static void Save(AppSettings settings, string? settingsPath)
    {
        try
        {
            var path = settingsPath ?? SettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Preferences are optional; editor functionality remains available in-memory.
        }
    }

    private sealed class AppSettings
    {
        public string? LastSoundFontPath { get; set; }
        public VocalToolSettings? Vocal { get; set; }
    }
}
