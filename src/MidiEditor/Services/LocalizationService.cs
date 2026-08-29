using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MidiEditor.Services;

public sealed record LanguageOption(string Code, string DisplayName);

public static class LocalizationService
{
    private static readonly string[] LanguageCodes = ["ko", "en", "ja", "zh-CN"];

    public static IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new("ko", "한국어"),
        new("en", "English"),
        new("ja", "日本語"),
        new("zh-CN", "简体中文")
    ];

    private static readonly Dictionary<string, string[]> Texts = new(StringComparer.Ordinal)
    {
        ["Static.Minimize"] = ["최소화", "Minimize", "最小化", "最小化"],
        ["Static.MaximizeRestore"] = ["최대화/복원", "Maximize / Restore", "最大化 / 元に戻す", "最大化 / 还原"],
        ["Static.Close"] = ["닫기", "Close", "閉じる", "关闭"],
        ["Static.Rewind"] = ["처음으로 (Home)", "Go to start (Home)", "先頭へ (Home)", "回到开头 (Home)"],
        ["Static.Stop"] = ["정지", "Stop", "停止", "停止"],
        ["Static.PlayPause"] = ["재생/일시정지 (Space)", "Play / Pause (Space)", "再生 / 一時停止 (Space)", "播放 / 暂停 (Space)"],
        ["Static.PositionHeader"] = ["마디      박      틱", "BAR      BEAT      TICK", "小節      拍      TICK", "小节      拍      TICK"],
        ["Static.Tempo"] = ["템포", "TEMPO", "テンポ", "速度"],
        ["Static.Snap"] = ["스냅", "SNAP", "スナップ", "吸附"],
        ["Static.Signature"] = ["박자표", "SIGNATURE", "拍子", "拍号"],
        ["Static.Loop"] = ["루프", "LOOP", "ループ", "循环"],
        ["Static.LoopTip"] = ["루프 (L)", "Loop (L)", "ループ (L)", "循环 (L)"],
        ["Static.Follow"] = ["따라가기", "FOLLOW", "追従", "跟随"],
        ["Static.FollowTip"] = ["재생바 자동 따라가기", "Follow playhead automatically", "再生位置を自動追従", "自动跟随播放位置"],
        ["Static.NewProject"] = ["새 프로젝트", "New Project", "新規プロジェクト", "新建项目"],
        ["Static.Open"] = ["열기", "Open", "開く", "打开"],
        ["Static.Save"] = ["저장", "Save", "保存", "保存"],
        ["Static.ImportMidi"] = ["MIDI 가져오기", "Import MIDI", "MIDI を読み込む", "导入 MIDI"],
        ["Static.ExportMidi"] = ["MIDI 내보내기", "Export MIDI", "MIDI を書き出す", "导出 MIDI"],
        ["Static.Tracks"] = ["트랙", "TRACKS", "トラック", "音轨"],
        ["Static.AddInstrument"] = ["악기 트랙 추가", "Add instrument track", "楽器トラックを追加", "添加乐器音轨"],
        ["Static.AddDrums"] = ["드럼 트랙 추가", "Add drum track", "ドラムトラックを追加", "添加鼓组音轨"],
        ["Static.AddVocal"] = ["OpenUtau 보컬 트랙 추가", "Add OpenUtau vocal track", "OpenUtau ボーカルトラックを追加", "添加 OpenUtau 人声音轨"],
        ["Static.VocalSettings"] = ["보컬/OpenUtau 설정", "Vocal / OpenUtau settings", "ボーカル / OpenUtau 設定", "人声 / OpenUtau 设置"],
        ["Static.Mute"] = ["음소거", "Mute", "ミュート", "静音"],
        ["Static.Solo"] = ["솔로", "Solo", "ソロ", "独奏"],
        ["Static.SelectedTrack"] = ["선택한 트랙", "SELECTED TRACK", "選択中のトラック", "已选音轨"],
        ["Static.DeleteTrack"] = ["트랙 삭제", "Delete track", "トラックを削除", "删除音轨"],
        ["Static.MidiChannel"] = ["MIDI 채널", "MIDI channel", "MIDI チャンネル", "MIDI 通道"],
        ["Static.VocalHeader"] = ["보컬 / OPENUTAU", "VOCAL / OPENUTAU", "ボーカル / OPENUTAU", "人声 / OPENUTAU"],
        ["Static.VocalVoicebankTip"] = ["이 보컬 트랙의 보이스뱅크", "Voicebank for this vocal track", "このボーカルトラックのボイスバンク", "此人声音轨的声库"],
        ["Static.LyricTip"] = ["선택한 보컬 노트의 가사/alias", "Lyric / alias for selected vocal notes", "選択したボーカルノートの歌詞 / alias", "所选人声音符的歌词 / alias"],
        ["Static.QuickPreview"] = ["빠른 미리듣기", "Quick Preview", "クイック試聴", "快速试听"],
        ["Static.OpenOpenUtau"] = ["OpenUtau 열기", "Open in OpenUtau", "OpenUtau で開く", "在 OpenUtau 中打开"],
        ["Static.SoundFontEngine"] = ["SOUNDFONT 엔진", "SOUNDFONT ENGINE", "SOUNDFONT エンジン", "SOUNDFONT 引擎"],
        ["Static.LoadSf2"] = ["SF2 불러오기", "Load SF2", "SF2 を読み込む", "加载 SF2"],
        ["Static.Playlist"] = ["플레이리스트", "PLAYLIST", "プレイリスト", "播放列表"],
        ["Static.AllTracks"] = ["  ·  모든 트랙", "  ·  all tracks", "  ·  全トラック", "  ·  全部音轨"],
        ["Static.ArrangementHint"] = ["Ctrl+휠: 가로 확대  ·  Shift+휠: 타임라인  ·  휠: 트랙", "Ctrl+wheel: H zoom  ·  Shift+wheel: timeline  ·  wheel: tracks", "Ctrl+ホイール: 横ズーム  ·  Shift+ホイール: タイムライン  ·  ホイール: トラック", "Ctrl+滚轮: 横向缩放  ·  Shift+滚轮: 时间线  ·  滚轮: 音轨"],
        ["Static.HZoom"] = ["가로 확대", "H ZOOM", "横ズーム", "横向缩放"],
        ["Static.VZoom"] = ["세로 확대", "V ZOOM", "縦ズーム", "纵向缩放"],
        ["Static.PianoRoll"] = ["피아노 롤", "PIANO ROLL", "ピアノロール", "钢琴卷帘"],
        ["Static.VocalRoll"] = ["보컬 롤", "VOCAL ROLL", "ボーカルロール", "人声卷帘"],
        ["Static.DrumPattern"] = ["드럼 패턴", "DRUM PATTERN", "ドラムパターン", "鼓组模式"],
        ["Static.NoTrackTitle"] = ["트랙을 추가하면 편집을 시작할 수 있습니다", "Add a track to start editing", "トラックを追加すると編集を開始できます", "添加音轨后即可开始编辑"],
        ["Static.NoTrackHint"] = ["왼쪽 위 + 버튼은 악기, 격자 버튼은 드럼 트랙을 만듭니다", "Use + for an instrument track or the grid button for drums", "左上の + で楽器、グリッドボタンでドラムトラックを追加します", "左上角 + 添加乐器音轨，网格按钮添加鼓组音轨"],
        ["Static.Sf2Offline"] = ["SF2 오프라인", "SF2 OFFLINE", "SF2 オフライン", "SF2 离线"],
        ["Static.LanguageTip"] = ["표시 언어", "Display language", "表示言語", "显示语言"],
        ["Static.VocalWindowTitle"] = ["PulseGrid 보컬 설정", "PulseGrid Vocal Settings", "PulseGrid ボーカル設定", "PulseGrid 人声设置"],
        ["Static.VocalWindowHeader"] = ["OPENUTAU / 보컬", "OPENUTAU / VOCAL", "OPENUTAU / ボーカル", "OPENUTAU / 人声"],
        ["Static.VocalDescription"] = ["OpenUtau 실행 파일과 UTAU 호환 보이스뱅크 경로를 지정합니다. resampler/wavtool 경로는 OpenUtau로 넘기는 UST 설정에 기록됩니다.", "Choose the OpenUtau executable and a UTAU-compatible voicebank path. resampler/wavtool paths are written into the UST passed to OpenUtau.", "OpenUtau の実行ファイルと UTAU 互換ボイスバンクのパスを指定します。resampler / wavtool のパスは OpenUtau に渡す UST 設定へ記録されます。", "指定 OpenUtau 可执行文件和兼容 UTAU 的声库路径。resampler / wavtool 路径会写入传递给 OpenUtau 的 UST 设置。"],
        ["Static.VoicebankPath"] = ["보이스뱅크 PATH", "VOICEBANK PATH", "ボイスバンク PATH", "声库路径"],
        ["Static.Browse"] = ["찾기…", "Browse…", "参照…", "浏览…"],
        ["Static.Optional"] = ["(선택 사항)", "(optional)", "(任意)", "（可选）"],
        ["Static.Cancel"] = ["취소", "Cancel", "キャンセル", "取消"],
        ["TrackCount"] = ["{0}개 트랙", "{0} tracks", "{0} トラック", "{0} 条音轨"],
        ["NotesCount"] = ["{0}개 노트  ·  {1}", "{0} notes  ·  {1}", "{0} ノート  ·  {1}", "{0} 个音符  ·  {1}"],
        ["ProjectSoundFontMissing"] = ["프로젝트의 SoundFont를 찾을 수 없습니다 · {0}", "Project SoundFont not found · {0}", "プロジェクトの SoundFont が見つかりません · {0}", "找不到项目的 SoundFont · {0}"],
        ["NewProjectReady"] = ["새 프로젝트 · 노트를 클릭하고 드래그해 편집하세요", "New project · click and drag notes to edit", "新規プロジェクト · ノートをクリック/ドラッグして編集", "新建项目 · 点击并拖动音符进行编辑"],
        ["Opened"] = ["열림 · {0}", "Opened · {0}", "開きました · {0}", "已打开 · {0}"],
        ["NoTrackSelected"] = ["선택한 트랙 없음", "No track selected", "トラックが選択されていません", "未选择音轨"],
        ["EditorHint.Drums"] = ["  ·  클릭해서 입력  ·  우클릭 드래그로 삭제  ·  휠로 악기 이동", "  ·  click paint  ·  right-drag erase  ·  wheel instruments", "  ·  クリックで入力  ·  右ドラッグで消去  ·  ホイールで楽器移動", "  ·  点击绘制  ·  右键拖动擦除  ·  滚轮切换乐器"],
        ["EditorHint.Vocal"] = ["  ·  노트 입력  ·  노트 선택 후 LYRIC 편집  ·  빠른 렌더 미리듣기", "  ·  draw notes  ·  select note then edit LYRIC  ·  quick render preview", "  ·  ノート入力  ·  選択後 LYRIC 編集  ·  クイックレンダー試聴", "  ·  绘制音符  ·  选择音符后编辑 LYRIC  ·  快速渲染试听"],
        ["EditorHint.Piano"] = ["  ·  좌클릭 입력/이동  ·  가장자리로 길이 조절  ·  우클릭 드래그 삭제", "  ·  left draw/move  ·  edge resize  ·  right-drag erase", "  ·  左クリックで入力/移動  ·  端で長さ変更  ·  右ドラッグで削除", "  ·  左键绘制/移动  ·  边缘调整长度  ·  右键拖动删除"],
        ["Paused"] = ["일시정지", "Paused", "一時停止", "已暂停"],
        ["NeedSoundFont"] = ["재생하려면 SoundFont(.sf2)를 먼저 선택해 주세요", "Select a SoundFont (.sf2) before playback", "再生するには先に SoundFont (.sf2) を選択してください", "播放前请先选择 SoundFont (.sf2)"],
        ["Playing"] = ["재생 중 · Space로 일시정지", "Playing · Space to pause", "再生中 · Space で一時停止", "播放中 · Space 暂停"],
        ["Stopped"] = ["정지", "Stopped", "停止", "已停止"],
        ["SoundFontSelect"] = ["SoundFont 선택", "Select SoundFont", "SoundFont を選択", "选择 SoundFont"],
        ["AllFiles"] = ["모든 파일 (*.*)", "All files (*.*)", "すべてのファイル (*.*)", "所有文件 (*.*)"],
        ["Sf2LoadingButton"] = ["SF2 로딩 중…", "Loading SF2…", "SF2 読み込み中…", "正在加载 SF2…"],
        ["SoundFontLoading"] = ["SoundFont 로딩 중 · {0}", "Loading SoundFont · {0}", "SoundFont 読み込み中 · {0}", "正在加载 SoundFont · {0}"],
        ["SoundFontReady"] = ["SoundFont 준비 완료 · {0}", "SoundFont ready · {0}", "SoundFont 準備完了 · {0}", "SoundFont 已就绪 · {0}"],
        ["SoundFontKeepOldError"] = ["새 SoundFont를 읽지 못해 기존 SoundFont를 유지합니다.", "Could not load the new SoundFont. Keeping the current one.", "新しい SoundFont を読み込めなかったため、現在の SoundFont を維持します。", "无法加载新的 SoundFont，将继续使用当前 SoundFont。"],
        ["SoundFontLoadError"] = ["SoundFont를 불러오지 못했습니다.", "Could not load SoundFont.", "SoundFont を読み込めませんでした。", "无法加载 SoundFont。"],
        ["BundledSoundFontReady"] = ["기본 CC0 SoundFont 준비 완료 · ChaosBank", "Bundled CC0 SoundFont ready · ChaosBank", "同梱 CC0 SoundFont 準備完了 · ChaosBank", "内置 CC0 SoundFont 已就绪 · ChaosBank"],
        ["EditComplete"] = ["편집 완료 · Ctrl+Z로 실행 취소", "Edit complete · Ctrl+Z to undo", "編集完了 · Ctrl+Z で元に戻す", "编辑完成 · Ctrl+Z 撤销"],
        ["NoMidiChannels"] = ["사용 가능한 MIDI 채널이 없습니다 (Ch 10 제외 최대 15개)", "No MIDI channels available (up to 15, excluding Ch 10)", "使用可能な MIDI チャンネルがありません (Ch 10 を除く最大 15)", "没有可用的 MIDI 通道（除 Ch 10 外最多 15 个）"],
        ["TrackAdded"] = ["{0} 트랙을 추가했습니다", "Added track: {0}", "トラックを追加しました: {0}", "已添加音轨：{0}"],
        ["TrackDeleted"] = ["트랙을 삭제했습니다", "Track deleted", "トラックを削除しました", "已删除音轨"],
        ["DrumChannelOnly"] = ["Ch 10은 GM 드럼 전용 채널입니다", "Ch 10 is reserved for GM drums", "Ch 10 は GM ドラム専用です", "Ch 10 为 GM 鼓组专用通道"],
        ["ChannelInUse"] = ["Ch {0}은 다른 악기 트랙에서 사용 중입니다", "Ch {0} is already used by another instrument track", "Ch {0} は別の楽器トラックで使用中です", "Ch {0} 已被其他乐器音轨使用"],
        ["VoicebankChanged"] = ["보이스뱅크 변경 · {0}", "Voicebank changed · {0}", "ボイスバンク変更 · {0}", "声库已更改 · {0}"],
        ["LyricChangedOne"] = ["가사 변경 · {0}", "Lyric changed · {0}", "歌詞変更 · {0}", "歌词已更改 · {0}"],
        ["LyricChangedMany"] = ["선택 노트 {0}개의 가사를 변경했습니다", "Changed lyrics for {0} selected notes", "選択した {0} ノートの歌詞を変更しました", "已更改 {0} 个所选音符的歌词"],
        ["VocalSettingsSaved"] = ["보컬/OpenUtau 설정을 저장했습니다", "Vocal / OpenUtau settings saved", "ボーカル / OpenUtau 設定を保存しました", "人声 / OpenUtau 设置已保存"],
        ["VocalRendering"] = ["보컬 빠른 미리듣기 렌더 중…", "Rendering quick vocal preview…", "ボーカルのクイック試聴をレンダリング中…", "正在渲染人声快速试听…"],
        ["VocalPreviewPlaying"] = ["보컬 미리듣기 재생 · {0}", "Playing vocal preview · {0}", "ボーカル試聴を再生 · {0}", "正在播放人声试听 · {0}"],
        ["VocalPreviewError"] = ["보컬 미리듣기를 만들지 못했습니다.", "Could not create vocal preview.", "ボーカル試聴を作成できませんでした。", "无法创建人声试听。"],
        ["OpenUtauOpened"] = ["OpenUtau로 열었습니다 · {0}", "Opened in OpenUtau · {0}", "OpenUtau で開きました · {0}", "已在 OpenUtau 中打开 · {0}"],
        ["OpenUtauError"] = ["OpenUtau를 실행하지 못했습니다.", "Could not launch OpenUtau.", "OpenUtau を起動できませんでした。", "无法启动 OpenUtau。"],
        ["Undo"] = ["실행 취소", "Undo", "元に戻す", "撤销"],
        ["Redo"] = ["다시 실행", "Redo", "やり直す", "重做"],
        ["ProjectOpenTitle"] = ["PulseGrid 프로젝트 열기", "Open PulseGrid Project", "PulseGrid プロジェクトを開く", "打开 PulseGrid 项目"],
        ["ProjectSaveTitle"] = ["PulseGrid 프로젝트 저장", "Save PulseGrid Project", "PulseGrid プロジェクトを保存", "保存 PulseGrid 项目"],
        ["ProjectOpenError"] = ["프로젝트를 열지 못했습니다.", "Could not open project.", "プロジェクトを開けませんでした。", "无法打开项目。"],
        ["ProjectSaveError"] = ["프로젝트를 저장하지 못했습니다.", "Could not save project.", "プロジェクトを保存できませんでした。", "无法保存项目。"],
        ["Saved"] = ["저장됨 · {0}", "Saved · {0}", "保存しました · {0}", "已保存 · {0}"],
        ["MidiImportTitle"] = ["MIDI 가져오기", "Import MIDI", "MIDI を読み込む", "导入 MIDI"],
        ["MidiExportTitle"] = ["MIDI 내보내기", "Export MIDI", "MIDI を書き出す", "导出 MIDI"],
        ["MidiFiles"] = ["MIDI 파일", "MIDI files", "MIDI ファイル", "MIDI 文件"],
        ["MidiAnalyzing"] = ["MIDI 분석 중 · {0}", "Analyzing MIDI · {0}", "MIDI 解析中 · {0}", "正在分析 MIDI · {0}"],
        ["MidiImportComplete"] = ["MIDI 가져오기 완료 · {0}개 트랙", "MIDI import complete · {0} tracks", "MIDI 読み込み完了 · {0} トラック", "MIDI 导入完成 · {0} 条音轨"],
        ["MidiImportError"] = ["MIDI 파일을 가져오지 못했습니다.", "Could not import MIDI file.", "MIDI ファイルを読み込めませんでした。", "无法导入 MIDI 文件。"],
        ["MidiExporting"] = ["MIDI 내보내는 중…", "Exporting MIDI…", "MIDI 書き出し中…", "正在导出 MIDI…"],
        ["MidiExportComplete"] = ["MIDI 내보내기 완료 · {0}", "MIDI export complete · {0}", "MIDI 書き出し完了 · {0}", "MIDI 导出完成 · {0}"],
        ["MidiExportError"] = ["MIDI 파일을 내보내지 못했습니다.", "Could not export MIDI file.", "MIDI ファイルを書き出せませんでした。", "无法导出 MIDI 文件。"],
        ["ConfirmSaveChanges"] = ["현재 프로젝트의 변경 내용을 저장할까요?", "Save changes to the current project?", "現在のプロジェクトの変更を保存しますか？", "是否保存当前项目的更改？"],
        ["LanguageChanged"] = ["표시 언어를 한국어로 변경했습니다", "Display language changed to English", "表示言語を日本語に変更しました", "显示语言已切换为简体中文"],
        ["VoicebankBrowseTitle"] = ["보이스뱅크 폴더의 character.txt 또는 oto.ini 선택", "Select character.txt or oto.ini in a voicebank folder", "ボイスバンクフォルダーの character.txt または oto.ini を選択", "选择声库文件夹中的 character.txt 或 oto.ini"],
        ["OpenUtauBrowseTitle"] = ["OpenUtau 실행 파일 선택", "Select OpenUtau executable", "OpenUtau 実行ファイルを選択", "选择 OpenUtau 可执行文件"],
        ["ResamplerBrowseTitle"] = ["resampler 선택", "Select resampler", "resampler を選択", "选择 resampler"],
        ["WavtoolBrowseTitle"] = ["wavtool 선택", "Select wavtool", "wavtool を選択", "选择 wavtool"],
        ["ExecutableFiles"] = ["실행 파일 (*.exe)", "Executable files (*.exe)", "実行ファイル (*.exe)", "可执行文件 (*.exe)"],
        ["Error.SoundFontFileMissing"] = ["SoundFont 파일을 찾을 수 없습니다.", "SoundFont file not found.", "SoundFont ファイルが見つかりません。", "找不到 SoundFont 文件。"],
        ["Error.SmpteUnsupported"] = ["SMPTE time-division MIDI는 아직 지원하지 않습니다. PPQ 형식으로 변환해 주세요.", "SMPTE time-division MIDI is not supported yet. Convert it to PPQ format.", "SMPTE time-division MIDI はまだサポートされていません。PPQ 形式に変換してください。", "暂不支持 SMPTE time-division MIDI，请转换为 PPQ 格式。"],
        ["Error.ProjectUnreadable"] = ["프로젝트 파일을 읽을 수 없습니다.", "Could not read the project file.", "プロジェクトファイルを読み込めません。", "无法读取项目文件。"],
        ["Error.VoicebankNotFound"] = ["사용 가능한 OpenUtau/UTAU 보이스뱅크를 찾을 수 없습니다.", "No usable OpenUtau/UTAU voicebank was found.", "使用可能な OpenUtau/UTAU ボイスバンクが見つかりません。", "找不到可用的 OpenUtau/UTAU 声库。"],
        ["Error.UstVocalOnly"] = ["UST 내보내기는 Vocal 트랙에서만 사용할 수 있습니다.", "UST export is available only for vocal tracks.", "UST 書き出しはボーカルトラックでのみ使用できます。", "UST 导出仅可用于人声音轨。"],
        ["Error.OpenUtauPathRequired"] = ["보컬 설정에서 OpenUtau 실행 파일 경로를 먼저 지정해 주세요.", "Set the OpenUtau executable path in Vocal Settings first.", "ボーカル設定で OpenUtau の実行ファイルパスを先に指定してください。", "请先在“人声设置”中指定 OpenUtau 可执行文件路径。"],
        ["Error.PreviewVocalOnly"] = ["빠른 보컬 미리듣기는 Vocal 트랙에서만 사용할 수 있습니다.", "Quick vocal preview is available only for vocal tracks.", "ボーカルのクイック試聴はボーカルトラックでのみ使用できます。", "人声快速试听仅可用于人声音轨。"],
        ["Error.NoVocalNotes"] = ["렌더할 보컬 노트가 없습니다.", "There are no vocal notes to render.", "レンダリングするボーカルノートがありません。", "没有可渲染的人声音符。"],
        ["Error.VoicebankSamplesMissing"] = ["보이스뱅크에서 oto.ini와 WAV 샘플을 찾지 못했습니다.", "Could not find oto.ini and WAV samples in the voicebank.", "ボイスバンクに oto.ini と WAV サンプルが見つかりません。", "在声库中找不到 oto.ini 和 WAV 采样。"],
    };

    private static readonly Dictionary<string, string> ReverseTextKeys = BuildReverseTextKeys();

    public static string CurrentLanguageCode { get; private set; } = DetectSystemLanguageCode();

    public static string DetectSystemLanguageCode(CultureInfo? culture = null)
    {
        var name = (culture ?? CultureInfo.CurrentUICulture).Name;
        if (name.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
        if (name.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        return "en";
    }

    public static string NormalizeLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "en";
        if (code.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
        if (code.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
        if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
        if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";
        return "en";
    }

    public static void SetLanguage(string? code) => CurrentLanguageCode = NormalizeLanguageCode(code);

    public static string Get(string key, params object?[] args)
    {
        if (!Texts.TryGetValue(key, out var variants)) return key;
        var value = variants[LanguageIndex(CurrentLanguageCode)];
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }

    public static string TranslateLiteral(string text) =>
        ReverseTextKeys.TryGetValue(text, out var key) ? Get(key) : text;

    public static void ApplyTo(DependencyObject root)
    {
        ApplyOne(root);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
            ApplyTo(VisualTreeHelper.GetChild(root, index));
    }

    private static void ApplyOne(DependencyObject element)
    {
        if (element is Window window)
            window.Title = TranslateLiteral(window.Title);
        if (element is TextBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
            textBlock.Text = TranslateLiteral(textBlock.Text);
        if (element is ContentControl contentControl && contentControl.Content is string content)
            contentControl.Content = TranslateLiteral(content);
        if (element is FrameworkElement frameworkElement && frameworkElement.ToolTip is string toolTip)
            frameworkElement.ToolTip = TranslateLiteral(toolTip);
    }

    private static int LanguageIndex(string code)
    {
        for (var index = 0; index < LanguageCodes.Length; index++)
            if (string.Equals(LanguageCodes[index], code, StringComparison.OrdinalIgnoreCase))
                return index;
        return 1;
    }

    private static Dictionary<string, string> BuildReverseTextKeys()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, variants) in Texts)
        {
            if (!key.StartsWith("Static.", StringComparison.Ordinal)) continue;
            foreach (var value in variants)
                result.TryAdd(value, key);
        }
        return result;
    }
}
