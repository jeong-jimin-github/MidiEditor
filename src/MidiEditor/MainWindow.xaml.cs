using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MidiEditor.Controls;
using MidiEditor.Models;
using MidiEditor.Services;

namespace MidiEditor;

public partial class MainWindow : Window
{
    private static readonly string[] TrackColors =
        ["#63D5A7", "#6EA8FE", "#B790F5", "#FF9D66", "#F779B8", "#64C9E8", "#E5C76B", "#9DD36D"];

    private readonly AudioEngine _audio = new();
    private readonly HistoryService _history = new();
    private readonly VocalPreviewPlayer _vocalPreview = new();
    private VocalToolSettings _vocalSettings = AppSettingsService.LoadVocalSettings();
    private readonly DispatcherTimer _uiTimer;
    private MidiProject _project = null!;
    private MidiTrack? _selectedTrack;
    private string? _projectPath;
    private double _currentBeat;
    private bool _updatingUi;
    private bool _showingDrums;
    private bool _isDirty;
    private bool _syncingScrollbars;
    private bool _syncingTimeline;
    private bool _startupSoundFontLoaded;
    private MidiProject? _cleanProjectSnapshot;

    public MainWindow()
    {
        InitializeComponent();

        ProgramCombo.ItemsSource = GeneralMidiPrograms.All;
        ChannelCombo.ItemsSource = Enumerable.Range(1, 16).Select(channel => $"Ch {channel}").ToArray();
        SnapCombo.ItemsSource = new[]
        {
            new SnapOption("1/4", 1), new SnapOption("1/8", 0.5), new SnapOption("1/16", 0.25),
            new SnapOption("1/32", 0.125), new SnapOption("1/64", 0.0625)
        };
        SnapCombo.SelectedIndex = 2;
        Arrangement.TimelineInset = PianoRoll.TimelineInset;
        Arrangement.PixelsPerBeat = ZoomSlider.Value;

        _uiTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _uiTimer.Tick += UiTimer_Tick;
        _uiTimer.Start();

        ApplyProject(MidiProject.CreateDemo(), null, true);
        Loaded += MainWindow_Loaded;
    }

    private void ApplyProject(MidiProject project, string? path, bool resetHistory, Guid? selectedTrackId = null)
    {
        _audio.Stop();
        if (string.IsNullOrWhiteSpace(project.SoundFontPath) && _audio.IsLoaded &&
            !string.IsNullOrWhiteSpace(_audio.SoundFontPath) && File.Exists(_audio.SoundFontPath))
        {
            project.SoundFontPath = _audio.SoundFontPath;
        }
        var soundFontMissing = !string.IsNullOrWhiteSpace(project.SoundFontPath) && !File.Exists(project.SoundFontPath);
        if (string.IsNullOrWhiteSpace(project.SoundFontPath) || soundFontMissing)
        {
            _audio.Unload();
            SetSoundFontOffline();
        }
        _project = project;
        _projectPath = path;
        _currentBeat = 0;

        if (resetHistory)
        {
            _history.Clear();
            _isDirty = false;
            _cleanProjectSnapshot = project.Clone();
        }

        _updatingUi = true;
        TrackListBox.ItemsSource = _project.Tracks;
        TempoBox.Text = _project.Tempo.ToString("0.##");
        SignatureText.Text = $"{_project.BeatsPerBar} / {_project.BeatUnit}";
        LoopToggle.IsChecked = _project.LoopEnabled;
        UpdateTitle();
        TrackCountText.Text = $"{_project.Tracks.Count} tracks";
        Arrangement.Project = _project;
        PianoRoll.Project = _project;
        DrumPattern.Project = _project;
        var selectedIndex = selectedTrackId is null
            ? (_project.Tracks.Count > 0 ? 0 : -1)
            : _project.Tracks.Select((track, index) => (track, index)).FirstOrDefault(item => item.track.Id == selectedTrackId).index;
        TrackListBox.SelectedIndex = _project.Tracks.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _project.Tracks.Count - 1);
        _updatingUi = false;

        RefreshSelectedTrack(autoChooseEditor: true);
        UpdatePlayhead();
        RefreshScrollBars();
        ShowStatus(soundFontMissing
            ? $"프로젝트의 SoundFont를 찾을 수 없습니다 · {Path.GetFileName(project.SoundFontPath)}"
            : path is null ? "새 프로젝트 · 노트를 클릭하고 드래그해 편집하세요" : $"열림 · {Path.GetFileName(path)}",
            warning: soundFontMissing);
    }

    private void RefreshSelectedTrack(bool autoChooseEditor)
    {
        var nextTrack = TrackListBox.SelectedItem as MidiTrack;
        if (!ReferenceEquals(nextTrack, _selectedTrack))
        {
            // End any captured edit/preview against the old track before editor controls are rebound.
            PianoRoll.CancelInteraction();
            DrumPattern.CancelInteraction();
        }
        _selectedTrack = nextTrack;
        Arrangement.SelectedTrack = _selectedTrack;
        PianoRoll.Track = _selectedTrack;
        DrumPattern.Track = _selectedTrack;
        NoTrackOverlay.Visibility = _selectedTrack is null ? Visibility.Visible : Visibility.Collapsed;

        _updatingUi = true;
        if (_selectedTrack is null)
        {
            TrackNameBox.Text = string.Empty;
            ProgramCombo.SelectedIndex = -1;
            ChannelCombo.SelectedIndex = -1;
            ProgramCombo.IsEnabled = false;
            ChannelCombo.IsEnabled = false;
            VocalInspectorPanel.Visibility = Visibility.Collapsed;
            LyricBox.Text = string.Empty;
            LyricBox.IsEnabled = false;
            EditorTrackTitle.Text = "No track selected";
            EditorColorBar.Background = Brushes.Transparent;
            SelectionStatusText.Text = string.Empty;
        }
        else
        {
            TrackNameBox.Text = _selectedTrack.Name;
            ProgramCombo.SelectedIndex = _selectedTrack.Program;
            ChannelCombo.SelectedIndex = _selectedTrack.Channel;
            ProgramCombo.IsEnabled = _selectedTrack.Kind == TrackKind.Instrument;
            ChannelCombo.IsEnabled = _selectedTrack.Kind == TrackKind.Instrument;
            VocalInspectorPanel.Visibility = _selectedTrack.Kind == TrackKind.Vocal ? Visibility.Visible : Visibility.Collapsed;
            EditorTrackTitle.Text = _selectedTrack.Name;
            EditorColorBar.Background = new SolidColorBrush(DrawingColor(_selectedTrack.Color));
            SelectionStatusText.Text = $"{_selectedTrack.Notes.Count} notes  ·  {_selectedTrack.ProgramLabel}";
        }
        _updatingUi = false;
        RefreshVocalInspector();

        if (autoChooseEditor)
            SelectEditor(_selectedTrack?.Kind == TrackKind.Drums);
        TrackListBox.Items.Refresh();
        Arrangement.InvalidateVisual();
        RefreshScrollBars();
    }

    private void SelectEditor(bool drums)
    {
        _showingDrums = drums;
        Arrangement.TimelineInset = drums ? DrumPattern.TimelineInset : PianoRoll.TimelineInset;
        PianoRoll.Visibility = drums ? Visibility.Collapsed : Visibility.Visible;
        DrumPattern.Visibility = drums ? Visibility.Visible : Visibility.Collapsed;
        PianoTabButton.Content = _selectedTrack?.Kind == TrackKind.Vocal ? "VOCAL ROLL" : "PIANO ROLL";
        PianoTabButton.Background = drums ? new SolidColorBrush(Color.FromRgb(36, 42, 52)) : FindBrush("AccentDarkBrush");
        DrumTabButton.Background = drums ? FindBrush("AccentDarkBrush") : new SolidColorBrush(Color.FromRgb(36, 42, 52));
        EditorHintText.Text = drums
            ? "  ·  click paint  ·  right-drag erase  ·  wheel instruments"
            : _selectedTrack?.Kind == TrackKind.Vocal
                ? "  ·  draw notes  ·  select note then edit LYRIC  ·  quick render preview"
                : "  ·  left draw/move  ·  edge resize  ·  right-drag erase";
        SyncTimelineHorizontalOffset(drums ? DrumPattern.HorizontalOffset : PianoRoll.HorizontalOffset);
        RefreshScrollBars();
    }

    private Brush FindBrush(string key) => (Brush)FindResource(key);

    private static Color DrawingColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return Color.FromRgb(99, 213, 167); }
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        if (_audio.IsPlaying)
        {
            _currentBeat = _audio.CurrentBeat;
            PlayButton.Content = "❚❚";
        }
        else
        {
            PlayButton.Content = "▶";
        }
        UpdatePlayhead();
    }

    private void UpdatePlayhead()
    {
        Arrangement.PlayheadBeat = _currentBeat;
        PianoRoll.PlayheadBeat = _currentBeat;
        DrumPattern.PlayheadBeat = _currentBeat;

        if (_audio.IsPlaying && FollowToggle.IsChecked == true)
        {
            if (_showingDrums)
            {
                DrumPattern.EnsureBeatVisible(_currentBeat);
                SyncTimelineHorizontalOffset(DrumPattern.HorizontalOffset);
            }
            else
            {
                PianoRoll.EnsureBeatVisible(_currentBeat);
                SyncTimelineHorizontalOffset(PianoRoll.HorizontalOffset);
            }
        }

        var quarterBeatsPerBar = Math.Max(0.125, _project.QuarterBeatsPerBar);
        var signatureBeatLength = 4.0 / _project.BeatUnit;
        var bar = (int)(_currentBeat / quarterBeatsPerBar) + 1;
        var signatureBeatPosition = (_currentBeat % quarterBeatsPerBar) / signatureBeatLength;
        var beatInBar = (int)Math.Floor(signatureBeatPosition) + 1;
        var tick = (int)Math.Round((signatureBeatPosition - Math.Floor(signatureBeatPosition)) * 480);
        if (tick >= 480)
            tick = 0;
        PositionText.Text = $"{bar:000} : {beatInBar:00} : {tick:000}";
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_audio.IsPlaying)
        {
            _currentBeat = _audio.CurrentBeat;
            _audio.Stop();
            PlayButton.Content = "▶";
            ShowStatus("일시정지");
            return;
        }

        if (!_audio.IsLoaded && !await LoadDefaultOrChooseSoundFontAsync())
        {
            ShowStatus("재생하려면 SoundFont(.sf2)를 먼저 선택해 주세요", warning: true);
            return;
        }

        if (_currentBeat >= _project.DurationBeats)
            _currentBeat = _project.LoopEnabled ? _project.LoopStartBeat : 0;
        _audio.Start(_project, _currentBeat);
        PlayButton.Content = "❚❚";
        ShowStatus("재생 중 · Space로 일시정지");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _audio.Stop();
        _currentBeat = 0;
        UpdatePlayhead();
        ShowStatus("정지");
    }

    private void RewindButton_Click(object sender, RoutedEventArgs e)
    {
        _currentBeat = 0;
        if (_audio.IsPlaying)
            _audio.Start(_project, 0);
        UpdatePlayhead();
    }

    private async Task<bool> ChooseAndLoadSoundFontAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "SoundFont 선택",
            Filter = "SoundFont 2 (*.sf2)|*.sf2|모든 파일 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return false;
        return await LoadSoundFontAsync(dialog.FileName);
    }

    private async Task<bool> LoadDefaultOrChooseSoundFontAsync()
    {
        var bundled = BundledAssetsService.DefaultSoundFontPath;
        if (File.Exists(bundled) && await LoadSoundFontAsync(bundled, markProjectDirty: false))
            return true;
        return await ChooseAndLoadSoundFontAsync();
    }

    private async Task<bool> LoadSoundFontAsync(string path, bool markProjectDirty = true)
    {
        var soundFontChanged = !string.Equals(_project.SoundFontPath, path, StringComparison.OrdinalIgnoreCase);
        try
        {
            SoundFontButton.IsEnabled = false;
            SoundFontButton.Content = "SF2 로딩 중…";
            SoundFontDot.Fill = FindBrush("WarningBrush");
            ShowStatus($"SoundFont 로딩 중 · {Path.GetFileName(path)}");
            await _audio.LoadSoundFontAsync(path);
            _project.SoundFontPath = path;
            AppSettingsService.SaveLastSoundFontPath(path);
            if (soundFontChanged && markProjectDirty)
            {
                _history.InvalidateRedo();
                SetDirty();
            }
            SoundFontButton.Content = $"●  {_audio.SoundFontName}";
            SoundFontDot.Fill = FindBrush("AccentBrush");
            CpuStatusText.Text = "SF2 READY";
            CpuStatusText.Foreground = FindBrush("AccentBrush");
            ShowStatus($"SoundFont 준비 완료 · {_audio.SoundFontName}");
            return true;
        }
        catch (Exception exception)
        {
            if (_audio.IsLoaded)
            {
                SoundFontButton.Content = $"●  {_audio.SoundFontName}";
                SoundFontDot.Fill = FindBrush("AccentBrush");
                CpuStatusText.Text = "SF2 READY";
                ShowError("새 SoundFont를 읽지 못해 기존 SoundFont를 유지합니다.", exception);
            }
            else
            {
                SoundFontButton.Content = "SF2 불러오기";
                SoundFontDot.Fill = FindBrush("DangerBrush");
                CpuStatusText.Text = "SF2 ERROR";
                ShowError("SoundFont를 불러오지 못했습니다.", exception);
            }
            return false;
        }
        finally
        {
            SoundFontButton.IsEnabled = true;
        }
    }

    private async void SoundFontButton_Click(object sender, RoutedEventArgs e) => await ChooseAndLoadSoundFontAsync();

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_startupSoundFontLoaded)
            return;
        _startupSoundFontLoaded = true;

        var rememberedPath = AppSettingsService.LoadLastSoundFontPath();
        var startupPath = !string.IsNullOrWhiteSpace(rememberedPath) && File.Exists(rememberedPath)
            ? rememberedPath
            : BundledAssetsService.DefaultSoundFontPath;
        if (!string.IsNullOrWhiteSpace(rememberedPath) && !File.Exists(rememberedPath))
            AppSettingsService.SaveLastSoundFontPath(null);

        if (File.Exists(startupPath) && await LoadSoundFontAsync(startupPath, markProjectDirty: false))
        {
            _cleanProjectSnapshot = _project.Clone();
            SetDirty(false);
            if (string.Equals(startupPath, BundledAssetsService.DefaultSoundFontPath, StringComparison.OrdinalIgnoreCase))
                ShowStatus("기본 CC0 SoundFont 준비 완료 · ChaosBank");
        }
    }

    private void Editor_EditStarted(object? sender, EventArgs e) => _history.Begin(_project);

    private void Editor_EditFinished(object? sender, EventArgs e)
    {
        if (!_history.Commit(_project))
        {
            Arrangement.InvalidateVisual();
            RefreshSelectedTrack(autoChooseEditor: false);
            return;
        }
        SetDirty();
        Arrangement.InvalidateVisual();
        RefreshSelectedTrack(autoChooseEditor: false);
        ShowStatus("편집 완료 · Ctrl+Z로 실행 취소");

        if (_audio.IsPlaying)
            _audio.Start(_project, _audio.CurrentBeat);
    }

    private void Editor_PreviewNote(object? sender, NotePreviewEventArgs e)
    {
        if (_selectedTrack is null || !_audio.IsLoaded)
            return;
        var channel = _selectedTrack.Kind == TrackKind.Drums ? 9 : _selectedTrack.Channel;
        if (e.IsNoteOn)
            _audio.PreviewNote(channel, _selectedTrack.Program, e.Pitch, e.Velocity, _selectedTrack.Bank);
        else
            _audio.PreviewNoteOff(channel, e.Pitch);
    }

    private void Editor_SelectionChanged(object? sender, EventArgs e) => RefreshVocalInspector();

    private void Editor_SeekRequested(object? sender, SeekRequestedEventArgs e)
    {
        _currentBeat = Math.Clamp(e.Beat, 0, _project.DurationBeats);
        if (_audio.IsPlaying)
            _audio.Start(_project, _currentBeat);
        else
            _audio.Seek(_project, _currentBeat);
        UpdatePlayhead();
    }

    private void Arrangement_TrackSelected(object? sender, TrackSelectedEventArgs e)
    {
        if (e.TrackIndex >= 0 && e.TrackIndex < TrackListBox.Items.Count)
            TrackListBox.SelectedIndex = e.TrackIndex;
    }

    private void TrackListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi)
            return;
        RefreshSelectedTrack(autoChooseEditor: true);
    }

    private void AddInstrumentButton_Click(object sender, RoutedEventArgs e) => AddTrack(TrackKind.Instrument);
    private void AddDrumButton_Click(object sender, RoutedEventArgs e) => AddTrack(TrackKind.Drums);
    private void AddVocalButton_Click(object sender, RoutedEventArgs e) => AddTrack(TrackKind.Vocal);

    private void AddTrack(TrackKind kind)
    {
        var usedChannels = _project.Tracks.Where(track => track.Kind != TrackKind.Drums)
            .Select(track => track.Channel).ToHashSet();
        var availableChannel = Enumerable.Range(0, 16)
            .Where(value => value != 9 && !usedChannels.Contains(value))
            .Select(value => (int?)value)
            .FirstOrDefault();
        if (kind != TrackKind.Drums && availableChannel is null)
        {
            ShowStatus("사용 가능한 MIDI 채널이 없습니다 (Ch 10 제외 최대 15개)", warning: true);
            return;
        }

        _history.Begin(_project);
        var channel = availableChannel ?? 9;
        var number = _project.Tracks.Count(track => track.Kind == kind) + 1;
        var track = new MidiTrack
        {
            Name = kind switch
            {
                TrackKind.Drums => $"Drums {number}",
                TrackKind.Vocal => $"Vocal {number}",
                _ => $"Instrument {number}"
            },
            Kind = kind,
            Channel = kind == TrackKind.Drums ? 9 : channel,
            Program = kind switch { TrackKind.Drums => 0, TrackKind.Vocal => 53, _ => 4 },
            VoicebankPath = kind == TrackKind.Vocal ? VocalIntegrationService.DiscoverVoicebanks(_vocalSettings.VoicebankRootPath).FirstOrDefault()?.Path : null,
            Color = TrackColors[_project.Tracks.Count % TrackColors.Length]
        };
        _project.Tracks.Add(track);
        _history.Commit(_project);
        SetDirty();
        TrackCountText.Text = $"{_project.Tracks.Count} tracks";
        TrackListBox.SelectedItem = track;
        Arrangement.InvalidateVisual();
        ShowStatus($"{track.Name} 트랙을 추가했습니다");
    }

    private void DeleteTrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTrack is null)
            return;
        var index = TrackListBox.SelectedIndex;
        _history.Begin(_project);
        _project.Tracks.Remove(_selectedTrack);
        _history.Commit(_project);
        SetDirty();
        TrackCountText.Text = $"{_project.Tracks.Count} tracks";
        TrackListBox.SelectedIndex = _project.Tracks.Count == 0 ? -1 : Math.Min(index, _project.Tracks.Count - 1);
        RefreshSelectedTrack(autoChooseEditor: true);
        Arrangement.InvalidateVisual();
        ShowStatus("트랙을 삭제했습니다");
    }

    private void TrackNameBox_Commit(object sender, KeyboardFocusChangedEventArgs e) => CommitTrackName();

    private void CommitTrackName()
    {
        if (_updatingUi || _selectedTrack is null || string.IsNullOrWhiteSpace(TrackNameBox.Text) || TrackNameBox.Text.Trim() == _selectedTrack.Name)
            return;
        _history.Begin(_project);
        _selectedTrack.Name = TrackNameBox.Text;
        _history.Commit(_project);
        SetDirty();
        EditorTrackTitle.Text = _selectedTrack.Name;
        TrackListBox.Items.Refresh();
    }

    private void ProgramCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _selectedTrack is null || ProgramCombo.SelectedIndex < 0 || _selectedTrack.Kind != TrackKind.Instrument)
            return;
        _history.Begin(_project);
        _selectedTrack.Program = ProgramCombo.SelectedIndex;
        _history.Commit(_project);
        SetDirty();
        TrackListBox.Items.Refresh();
        SelectionStatusText.Text = $"{_selectedTrack.Notes.Count} notes  ·  {_selectedTrack.ProgramLabel}";
        if (_audio.IsPlaying)
            _audio.Start(_project, _audio.CurrentBeat);
    }

    private void ChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _selectedTrack is null || ChannelCombo.SelectedIndex < 0 || _selectedTrack.Kind != TrackKind.Instrument)
            return;
        var requestedChannel = ChannelCombo.SelectedIndex;
        if (requestedChannel == 9)
        {
            _updatingUi = true;
            ChannelCombo.SelectedIndex = _selectedTrack.Channel;
            _updatingUi = false;
            ShowStatus("Ch 10은 GM 드럼 전용 채널입니다", warning: true);
            return;
        }
        if (_project.Tracks.Any(track => track != _selectedTrack && track.Kind == TrackKind.Instrument && track.Channel == requestedChannel))
        {
            _updatingUi = true;
            ChannelCombo.SelectedIndex = _selectedTrack.Channel;
            _updatingUi = false;
            ShowStatus($"Ch {requestedChannel + 1}은 다른 악기 트랙에서 사용 중입니다", warning: true);
            return;
        }
        _history.Begin(_project);
        _selectedTrack.Channel = requestedChannel;
        _history.Commit(_project);
        SetDirty();
        TrackListBox.Items.Refresh();
    }

    private void RefreshVocalInspector()
    {
        if (VocalInspectorPanel is null || VoicebankCombo is null || LyricBox is null)
            return;

        var isVocal = _selectedTrack?.Kind == TrackKind.Vocal;
        VocalInspectorPanel.Visibility = isVocal ? Visibility.Visible : Visibility.Collapsed;
        if (!isVocal || _selectedTrack is null)
        {
            LyricBox.IsEnabled = false;
            return;
        }

        var previousUpdating = _updatingUi;
        _updatingUi = true;
        try
        {
            var voicebanks = VocalIntegrationService.DiscoverVoicebanks(_vocalSettings.VoicebankRootPath).ToList();
            if (!string.IsNullOrWhiteSpace(_selectedTrack.VoicebankPath) && Directory.Exists(_selectedTrack.VoicebankPath) &&
                voicebanks.All(item => !string.Equals(item.Path, _selectedTrack.VoicebankPath, StringComparison.OrdinalIgnoreCase)))
            {
                voicebanks.Add(new VoicebankInfo(Path.GetFileName(_selectedTrack.VoicebankPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), _selectedTrack.VoicebankPath));
            }
            VoicebankCombo.ItemsSource = voicebanks;
            VoicebankCombo.SelectedItem = voicebanks.FirstOrDefault(item =>
                string.Equals(item.Path, _selectedTrack.VoicebankPath, StringComparison.OrdinalIgnoreCase)) ?? voicebanks.FirstOrDefault();

            var selectedNotes = _selectedTrack.Notes.Where(note => note.IsSelected).ToArray();
            LyricBox.IsEnabled = selectedNotes.Length > 0;
            if (selectedNotes.Length == 0)
                LyricBox.Text = string.Empty;
            else
            {
                var first = selectedNotes[0].Lyric;
                LyricBox.Text = selectedNotes.All(note => note.Lyric == first) ? first : string.Empty;
            }
        }
        finally
        {
            _updatingUi = previousUpdating;
        }
    }

    private void VoicebankCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _selectedTrack?.Kind != TrackKind.Vocal || VoicebankCombo.SelectedItem is not VoicebankInfo voicebank ||
            string.Equals(_selectedTrack.VoicebankPath, voicebank.Path, StringComparison.OrdinalIgnoreCase))
            return;

        _history.Begin(_project);
        _selectedTrack.VoicebankPath = voicebank.Path;
        if (_history.Commit(_project))
            SetDirty();
        TrackListBox.Items.Refresh();
        SelectionStatusText.Text = $"{_selectedTrack.Notes.Count} notes  ·  {_selectedTrack.ProgramLabel}";
        ShowStatus($"보이스뱅크 변경 · {voicebank.Name}");
    }

    private void LyricBox_Commit(object sender, KeyboardFocusChangedEventArgs e) => CommitLyric();

    private void LyricBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CommitLyric();
        PianoRoll.Focus();
        e.Handled = true;
    }

    private void CommitLyric()
    {
        if (_updatingUi || _selectedTrack?.Kind != TrackKind.Vocal)
            return;
        var selected = _selectedTrack.Notes.Where(note => note.IsSelected).ToArray();
        if (selected.Length == 0)
            return;
        var lyric = LyricBox.Text.Trim();
        if (selected.All(note => note.Lyric == lyric))
            return;

        _history.Begin(_project);
        foreach (var note in selected)
            note.Lyric = lyric;
        if (_history.Commit(_project))
            SetDirty();
        PianoRoll.InvalidateVisual();
        ShowStatus(selected.Length == 1 ? $"가사 변경 · {lyric}" : $"선택 노트 {selected.Length}개의 가사를 변경했습니다");
    }

    private void VocalSettingsButton_Click(object sender, RoutedEventArgs e) => EditVocalSettings();

    private bool EditVocalSettings()
    {
        var dialog = new VocalSettingsWindow(_vocalSettings) { Owner = this };
        if (dialog.ShowDialog() != true)
            return false;
        _vocalSettings = dialog.Settings;
        AppSettingsService.SaveVocalSettings(_vocalSettings);
        RefreshVocalInspector();
        ShowStatus("보컬/OpenUtau 설정을 저장했습니다");
        return true;
    }

    private async void VocalPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTrack?.Kind != TrackKind.Vocal)
            return;
        CommitLyric();
        var trackId = _selectedTrack.Id;
        var project = _project.Clone();
        var track = project.Tracks.First(item => item.Id == trackId);
        VocalPreviewButton.IsEnabled = false;
        try
        {
            _audio.Stop();
            _vocalPreview.Stop();
            ShowStatus("보컬 빠른 미리듣기 렌더 중…");
            var path = await VocalIntegrationService.RenderQuickPreviewAsync(project, track, _vocalSettings.Clone());
            _vocalPreview.Play(path);
            ShowStatus($"보컬 미리듣기 재생 · {track.ProgramLabel}");
        }
        catch (Exception exception)
        {
            ShowError("보컬 미리듣기를 만들지 못했습니다.", exception);
        }
        finally
        {
            VocalPreviewButton.IsEnabled = true;
        }
    }

    private void OpenUtauButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTrack?.Kind != TrackKind.Vocal)
            return;
        CommitLyric();
        if (string.IsNullOrWhiteSpace(_vocalSettings.OpenUtauPath) || !File.Exists(_vocalSettings.OpenUtauPath))
        {
            if (!EditVocalSettings() || string.IsNullOrWhiteSpace(_vocalSettings.OpenUtauPath) || !File.Exists(_vocalSettings.OpenUtauPath))
                return;
        }

        try
        {
            var path = VocalIntegrationService.OpenInOpenUtau(_project, _selectedTrack, _vocalSettings);
            ShowStatus($"OpenUtau로 열었습니다 · {Path.GetFileName(path)}");
        }
        catch (Exception exception)
        {
            ShowError("OpenUtau를 실행하지 못했습니다.", exception);
        }
    }

    private void TrackToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Commit(_project))
            SetDirty();
        if (_audio.IsPlaying)
            _audio.Start(_project, _audio.CurrentBeat);
        Arrangement.InvalidateVisual();
    }

    private void TempoBox_Commit(object sender, KeyboardFocusChangedEventArgs e) => CommitTempo();

    private void TempoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CommitTempo();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void CommitTempo()
    {
        if (!double.TryParse(TempoBox.Text, out var tempo))
        {
            TempoBox.Text = _project.Tempo.ToString("0.##");
            return;
        }
        tempo = Math.Clamp(tempo, 20, 300);
        if (Math.Abs(tempo - _project.Tempo) < 0.001)
            return;
        _history.Begin(_project);
        _project.Tempo = tempo;
        _history.Commit(_project);
        SetDirty();
        TempoBox.Text = tempo.ToString("0.##");
        if (_audio.IsPlaying)
            _audio.Start(_project, _audio.CurrentBeat);
    }

    private void SnapCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SnapCombo.SelectedItem is not SnapOption option)
            return;
        PianoRoll.SnapBeats = option.Beats;
        DrumPattern.StepBeats = Math.Clamp(option.Beats, 0.125, 1);
    }

    private void LoopToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingUi || _project is null)
            return;
        _project.LoopEnabled = LoopToggle.IsChecked == true;
        if (_history.Commit(_project))
            SetDirty();
        Arrangement.InvalidateVisual();
        if (_audio.IsPlaying)
            _audio.Start(_project, _audio.CurrentBeat);
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Arrangement is not null)
            Arrangement.PixelsPerBeat = e.NewValue;
        if (PianoRoll is not null)
            PianoRoll.PixelsPerBeat = e.NewValue;
        if (DrumPattern is not null)
            DrumPattern.PixelsPerBeat = e.NewValue;
        if (Arrangement is not null && PianoRoll is not null && DrumPattern is not null)
            SyncTimelineHorizontalOffset(_showingDrums ? DrumPattern.HorizontalOffset : PianoRoll.HorizontalOffset);
    }

    private void EditorVerticalZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PianoRoll is not null)
            PianoRoll.VerticalZoom = e.NewValue;
        if (DrumPattern is not null)
            DrumPattern.VerticalZoom = e.NewValue;
    }

    private void ArrangementVerticalZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Arrangement is not null)
            Arrangement.VerticalZoom = e.NewValue;
    }

    private void Viewport_Changed(object? sender, EventArgs e)
    {
        if (!_syncingTimeline && (ReferenceEquals(sender, Arrangement) ||
            _showingDrums && ReferenceEquals(sender, DrumPattern) ||
            !_showingDrums && ReferenceEquals(sender, PianoRoll)))
        {
            var offset = ReferenceEquals(sender, Arrangement)
                ? Arrangement.HorizontalOffset
                : _showingDrums ? DrumPattern.HorizontalOffset : PianoRoll.HorizontalOffset;
            SyncTimelineHorizontalOffset(offset);
            return;
        }
        RefreshScrollBars();
    }

    private void ViewportScroll_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingScrollbars || Arrangement is null || PianoRoll is null || DrumPattern is null)
            return;

        if (ReferenceEquals(sender, ArrangementHorizontalScroll))
            SyncTimelineHorizontalOffset(e.NewValue);
        else if (ReferenceEquals(sender, ArrangementVerticalScroll))
            Arrangement.VerticalOffset = e.NewValue;
        else if (ReferenceEquals(sender, EditorHorizontalScroll))
            SyncTimelineHorizontalOffset(e.NewValue);
        else if (ReferenceEquals(sender, EditorVerticalScroll))
        {
            if (_showingDrums)
                DrumPattern.VerticalOffset = e.NewValue;
            else
                PianoRoll.VerticalOffset = e.NewValue;
        }
    }

    private void SyncTimelineHorizontalOffset(double requestedOffset)
    {
        if (_syncingTimeline || Arrangement is null || PianoRoll is null || DrumPattern is null)
            return;
        _syncingTimeline = true;
        try
        {
            double canonicalOffset;
            if (_showingDrums)
            {
                DrumPattern.HorizontalOffset = requestedOffset;
                canonicalOffset = DrumPattern.HorizontalOffset;
            }
            else
            {
                PianoRoll.HorizontalOffset = requestedOffset;
                canonicalOffset = PianoRoll.HorizontalOffset;
            }
            Arrangement.HorizontalOffset = canonicalOffset;
            PianoRoll.HorizontalOffset = canonicalOffset;
            DrumPattern.HorizontalOffset = canonicalOffset;
        }
        finally
        {
            _syncingTimeline = false;
        }
        RefreshScrollBars();
    }

    private void RefreshScrollBars()
    {
        if (ArrangementHorizontalScroll is null || EditorHorizontalScroll is null || Arrangement is null || PianoRoll is null || DrumPattern is null)
            return;
        _syncingScrollbars = true;
        try
        {
            ConfigureScrollBar(ArrangementHorizontalScroll, Arrangement.HorizontalMaximum, Arrangement.VisibleBeats,
                Arrangement.HorizontalOffset, 1);
            ConfigureScrollBar(ArrangementVerticalScroll, Arrangement.VerticalMaximum, Arrangement.VisibleTracks,
                Arrangement.VerticalOffset, 1);

            if (_showingDrums)
            {
                ConfigureScrollBar(EditorHorizontalScroll, DrumPattern.HorizontalMaximum, DrumPattern.VisibleBeats,
                    DrumPattern.HorizontalOffset, DrumPattern.StepBeats);
                ConfigureScrollBar(EditorVerticalScroll, DrumPattern.VerticalMaximum, DrumPattern.VisibleDrums,
                    DrumPattern.VerticalOffset, 1);
            }
            else
            {
                ConfigureScrollBar(EditorHorizontalScroll, PianoRoll.HorizontalMaximum, PianoRoll.VisibleBeats,
                    PianoRoll.HorizontalOffset, PianoRoll.SnapBeats);
                ConfigureScrollBar(EditorVerticalScroll, PianoRoll.VerticalMaximum, PianoRoll.VisiblePitchCount,
                    PianoRoll.VerticalOffset, 1);
            }
        }
        finally
        {
            _syncingScrollbars = false;
        }
    }

    private static void ConfigureScrollBar(System.Windows.Controls.Primitives.ScrollBar scrollBar,
        double maximum, double viewport, double value, double smallChange)
    {
        scrollBar.Minimum = 0;
        scrollBar.Maximum = Math.Max(0, maximum);
        scrollBar.ViewportSize = Math.Max(0, viewport);
        scrollBar.SmallChange = Math.Max(0.01, smallChange);
        scrollBar.LargeChange = Math.Max(scrollBar.SmallChange, viewport * 0.8);
        scrollBar.Value = Math.Clamp(value, 0, scrollBar.Maximum);
    }

    private void PianoTabButton_Click(object sender, RoutedEventArgs e) => SelectEditor(false);
    private void DrumTabButton_Click(object sender, RoutedEventArgs e) => SelectEditor(true);

    private void Undo()
    {
        var selectedTrackId = _selectedTrack?.Id;
        var previous = _history.Undo(_project);
        if (previous is null)
            return;
        previous.SoundFontPath = _project.SoundFontPath;
        ApplyProject(previous, _projectPath, false, selectedTrackId);
        SetDirty();
        ShowStatus("실행 취소");
    }

    private void Redo()
    {
        var selectedTrackId = _selectedTrack?.Id;
        var next = _history.Redo(_project);
        if (next is null)
            return;
        next.SoundFontPath = _project.SoundFontPath;
        ApplyProject(next, _projectPath, false, selectedTrackId);
        SetDirty();
        ShowStatus("다시 실행");
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryContinueWithUnsavedChanges())
            return;
        var project = new MidiProject { Name = "New Project", LoopEndBeat = 16 };
        project.Tracks.Add(new MidiTrack { Name = "Instrument 1", Program = 4, Channel = 0, Color = TrackColors[0] });
        project.Tracks.Add(new MidiTrack { Name = "Drums 1", Kind = TrackKind.Drums, Channel = 9, Color = TrackColors[1] });
        project.Tracks.Add(new MidiTrack { Name = "Vocal 1", Kind = TrackKind.Vocal, Program = 53, Channel = 1,
            VoicebankPath = VocalIntegrationService.DiscoverVoicebanks(_vocalSettings.VoicebankRootPath).FirstOrDefault()?.Path, Color = TrackColors[2] });
        ApplyProject(project, null, true);
    }

    private async void OpenProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryContinueWithUnsavedChanges())
            return;
        var dialog = new OpenFileDialog
        {
            Title = "PulseGrid 프로젝트 열기",
            Filter = "PulseGrid Project (*.pulsegrid)|*.pulsegrid|JSON (*.json)|*.json|모든 파일 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var project = await Task.Run(() => ProjectFileService.Load(dialog.FileName));
            ApplyProject(project, dialog.FileName, true);
            if (!string.IsNullOrWhiteSpace(project.SoundFontPath) && File.Exists(project.SoundFontPath) &&
                !string.Equals(_audio.SoundFontPath, project.SoundFontPath, StringComparison.OrdinalIgnoreCase))
            {
                _audio.Unload();
                SetSoundFontOffline();
                await LoadSoundFontAsync(project.SoundFontPath);
            }
        }
        catch (Exception exception)
        {
            ShowError("프로젝트를 열지 못했습니다.", exception);
        }
    }

    private void SaveProjectButton_Click(object sender, RoutedEventArgs e) => SaveProject();

    private bool SaveProject()
    {
        var path = _projectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var dialog = new SaveFileDialog
            {
                Title = "PulseGrid 프로젝트 저장",
                Filter = "PulseGrid Project (*.pulsegrid)|*.pulsegrid",
                FileName = SanitizeFileName(_project.Name) + ".pulsegrid",
                AddExtension = true,
                DefaultExt = ".pulsegrid"
            };
            if (dialog.ShowDialog(this) != true)
                return false;
            path = dialog.FileName;
        }

        try
        {
            ProjectFileService.Save(path, _project);
            _projectPath = path;
            _cleanProjectSnapshot = _project.Clone();
            SetDirty(false);
            ShowStatus($"저장됨 · {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception exception)
        {
            ShowError("프로젝트를 저장하지 못했습니다.", exception);
            return false;
        }
    }

    private async void ImportMidiButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryContinueWithUnsavedChanges())
            return;
        var dialog = new OpenFileDialog
        {
            Title = "MIDI 가져오기",
            Filter = "MIDI 파일 (*.mid;*.midi)|*.mid;*.midi|모든 파일 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            ShowStatus($"MIDI 분석 중 · {Path.GetFileName(dialog.FileName)}");
            var imported = await Task.Run(() => MidiFileService.Import(dialog.FileName));
            if (_audio.IsLoaded)
                imported.SoundFontPath = _project.SoundFontPath;
            ApplyProject(imported, null, true);
            _cleanProjectSnapshot = null;
            SetDirty(force: true);
            ShowStatus($"MIDI 가져오기 완료 · {imported.Tracks.Count} tracks");
        }
        catch (Exception exception)
        {
            ShowError("MIDI 파일을 가져오지 못했습니다.", exception);
        }
    }

    private async void ExportMidiButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "MIDI 내보내기",
            Filter = "MIDI 파일 (*.mid)|*.mid",
            FileName = SanitizeFileName(_project.Name) + ".mid",
            AddExtension = true,
            DefaultExt = ".mid"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            ShowStatus("MIDI 내보내는 중…");
            await Task.Run(() => MidiFileService.Export(dialog.FileName, _project.Clone()));
            ShowStatus($"MIDI 내보내기 완료 · {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception exception)
        {
            ShowError("MIDI 파일을 내보내지 못했습니다.", exception);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
            return;

        if (e.Key == Key.Space)
        {
            PlayButton_Click(PlayButton, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            StopButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.None)
        {
            _history.Begin(_project);
            LoopToggle.IsChecked = LoopToggle.IsChecked != true;
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            SelectEditor(!_showingDrums);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Redo();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SaveProject();
            e.Handled = true;
        }
    }

    private void ShowStatus(string message, bool warning = false)
    {
        StatusText.Text = message;
        StatusDot.Fill = warning ? FindBrush("WarningBrush") : FindBrush("AccentBrush");
    }

    private void SetSoundFontOffline()
    {
        SoundFontButton.Content = "SF2 불러오기";
        SoundFontDot.Fill = FindBrush("DangerBrush");
        CpuStatusText.Text = "SF2 OFFLINE";
        CpuStatusText.Foreground = new SolidColorBrush(Color.FromRgb(104, 116, 135));
    }

    private void HistoryToggle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _history.Begin(_project);

    private void HistoryToggle_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter)
            _history.Begin(_project);
    }

    private void SetDirty(bool dirty = true, bool force = false)
    {
        _isDirty = dirty && (force || _cleanProjectSnapshot is null || !HistoryService.AreEquivalent(_cleanProjectSnapshot, _project));
        UpdateTitle();
    }

    private void UpdateTitle() => TitleProjectText.Text = _project is null
        ? "PulseGrid"
        : _project.Name + (_isDirty ? "  •" : string.Empty);

    private bool TryContinueWithUnsavedChanges()
    {
        if (!_isDirty)
            return true;

        var result = MessageBox.Show(this,
            "현재 프로젝트의 변경 내용을 저장할까요?",
            "PulseGrid",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveProject(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void ShowError(string title, Exception exception)
    {
        ShowStatus($"{title} {exception.Message}", warning: true);
        MessageBox.Show(this, $"{title}\n\n{exception.Message}", "PulseGrid", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var character in Path.GetInvalidFileNameChars())
            name = name.Replace(character, '_');
        return string.IsNullOrWhiteSpace(name) ? "Untitled" : name;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        CommitTrackName();
        CommitTempo();
        if (!TryContinueWithUnsavedChanges())
        {
            e.Cancel = true;
            return;
        }
        _uiTimer.Stop();
        _vocalPreview.Dispose();
        _audio.Dispose();
    }

    private sealed record SnapOption(string Label, double Beats)
    {
        public override string ToString() => Label;
    }
}
