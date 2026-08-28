using System.IO;
using System.Windows;
using Microsoft.Win32;
using MidiEditor.Services;

namespace MidiEditor;

public partial class VocalSettingsWindow : Window
{
    public VocalToolSettings Settings { get; private set; }

    public VocalSettingsWindow(VocalToolSettings settings)
    {
        InitializeComponent();
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
            Title = "보이스뱅크 폴더의 character.txt 또는 oto.ini 선택",
            Filter = "UTAU voicebank (character.txt;oto.ini)|character.txt;oto.ini|모든 파일 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            VoicebankRootBox.Text = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
    }

    private void BrowseOpenUtau_Click(object sender, RoutedEventArgs e) =>
        BrowseExecutable(OpenUtauBox, "OpenUtau 실행 파일 선택", "OpenUtau (*.exe)|*.exe|모든 파일 (*.*)|*.*");

    private void BrowseResampler_Click(object sender, RoutedEventArgs e) =>
        BrowseExecutable(ResamplerBox, "resampler 선택", "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*");

    private void BrowseWavtool_Click(object sender, RoutedEventArgs e) =>
        BrowseExecutable(WavtoolBox, "wavtool 선택", "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*");

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
