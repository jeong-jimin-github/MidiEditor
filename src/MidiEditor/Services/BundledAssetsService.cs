using System.IO;

namespace MidiEditor.Services;

public static class BundledAssetsService
{
    public static string DefaultSoundFontPath => Path.Combine(
        AppContext.BaseDirectory, "Assets", "SoundFonts", "ChaosBank.sf2");

    public static string DefaultVoicebankPath => Path.Combine(
        AppContext.BaseDirectory, "Assets", "Voicebanks", "PulseGridDefault");
}
