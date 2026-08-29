using System.IO;
using System.Windows;
using Microsoft.Win32;
using MidiEditor.Services;

namespace MidiEditor;

public partial class VocalSettingsWindow : Window
{
    private static string L(string key, params object?[] args) => LocalizationService.Get(key, args);

    public VocalToolSettings Settings { get; private set; }

    public VocalSettingsWindow(VocalToolSettings settings)
    {
        InitializeComponent();
        LocalizationService.ApplyTo(this);
        Loaded += (_, _) => LocalizationService.ApplyTo(this);
        Settings = settings.Clone();
        VoicebankRootBox.Text = Settings.VoicebankRootPath ?? string.Empty;
        OpenUtauBox.Text = Settings.OpenUtauPath ?? string.Empty;
        ResamplerBox.Text = Settings.ResamplerPath ?? string.Empty;
        WavtoolBox.Text = Settings.WavtoolPath ?? string.Empty;
    }

    private void BrowseVoicebank_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = L("VoicebankBrowseTitle"),
            Filter = $"UTAU voicebank (character.txt;oto.ini)|character.txt;oto.ini|{L("AllFiles")}"
        };
        if (dialog.ShowDialog(this) == true)
            VoicebankRootBox.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
    }

    private void BrowseOpenUtau_Click(object sender, RoutedEventArgs e) =>
        BrowseExecutable(OpenUtauBox, L("OpenUtauBrowseTitle"), $"OpenUtau (*.exe)|*.exe|{L("AllFiles")}");

    private void BrowseResampler_Click(object sender, RoutedEventArgs e) =>
        BrowseExecutable(ResamplerBox, L("ResamplerBrowseTitle"), $"{L("ExecutableFiles")}|*.exe|{L("AllFiles")}");

    private void BrowseWavtool_Click(object sender, RoutedEventArgs e) =>
        BrowseExecutable(WavtoolBox, L("WavtoolBrowseTitle"), $"{L("ExecutableFiles")}|*.exe|{L("AllFiles")}");

    private void BrowseExecutable(System.Windows.Controls.TextBox box, string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            box.Text = dialog.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings = new VocalToolSettings
        {
            VoicebankRootPath = Clean(VoicebankRootBox.Text),
            OpenUtauPath = Clean(OpenUtauBox.Text),
            ResamplerPath = Clean(ResamplerBox.Text),
            WavtoolPath = Clean(WavtoolBox.Text)
        };
        DialogResult = true;
    }

    private static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
