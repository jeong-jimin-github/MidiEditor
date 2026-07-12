# PulseGrid MIDI Studio

C#과 WPF로 만든 데스크톱 MIDI 편집기입니다. FL Studio의 빠른 직접 조작 감각을 참고해 피아노 롤, 플레이리스트, 드럼 스텝 시퀀서와 벨로시티 레인을 한 화면에 구성했습니다.

## 주요 기능

- 멀티트랙 편집: 트랙 추가/삭제, 이름, 색상, MIDI 채널, GM 프로그램, Mute/Solo
- 피아노 롤: 클릭으로 생성, 드래그 이동, 오른쪽 끝 길이 조절, 우클릭 삭제, 피아노 키 미리듣기
- 드럼 패턴: GM 채널 10의 35–81 전체 악기, 휠 탐색, 클릭·드래그 스텝 페인팅, 우클릭 삭제
- 벨로시티: 피아노 롤과 드럼 편집기 하단 막대를 위아래로 드래그해 1–127 조절
- SoundFont 재생: SF2를 MeltySynth로 합성하고 NAudio로 44.1 kHz stereo 출력
- 샘플 단위 재생 스케줄러: 짧은 드럼 노트와 루프도 오디오 버퍼 안에서 정확한 위치에 처리
- MIDI 파일: Format 1 멀티트랙 `.mid` 가져오기/내보내기, tempo map·초기 박자·bank/program·채널·벨로시티 보존
- 프로젝트 파일: `.pulsegrid` 저장/열기, SoundFont 경로 포함
- 실행 취소/다시 실행, Snap 1/4–1/64, 수평 확대/축소, 재생 위치 이동

## 실행

Windows 10/11과 .NET 8 SDK가 필요합니다.

```powershell
dotnet restore MidiEditor.sln
dotnet run --project src/MidiEditor/MidiEditor.csproj
```

또는 Visual Studio에서 `MidiEditor.sln`을 열고 `MidiEditor`를 시작 프로젝트로 실행하세요.

앱에는 라이선스가 불분명한 샘플을 포함하지 않기 위해 SoundFont 파일을 번들하지 않았습니다. 왼쪽 아래 **SF2 불러오기**에서 보유한 General MIDI 호환 `.sf2`를 선택하면 즉시 미리듣기와 전체 재생을 사용할 수 있습니다.

처음 화면의 예제 곡은 `PulseGridDemo.pulsegrid`에도 저장되어 있어 **열기**로 다시 불러올 수 있습니다.

## 조작

| 조작 | 기능 |
|---|---|
| 빈 피아노 롤 좌클릭/드래그 | 노트 생성 및 길이 지정 |
| 노트 몸통 드래그 | 시간/음정 이동 |
| 노트 오른쪽 가장자리 드래그 | 길이 조절 |
| 노트 또는 드럼 스텝 우클릭 | 삭제 |
| 드럼 그리드 좌클릭/드래그 | 스텝 입력/연속 입력 |
| 드럼 스텝 `Shift+클릭` | 벨로시티 편집할 스텝 선택 |
| 하단 Velocity 막대 드래그 | 벨로시티 조절 |
| `Space` | 재생/일시정지 |
| `Home` | 정지 후 처음으로 |
| `L` | 루프 켜기/끄기 |
| `Tab` | 피아노 롤/드럼 패턴 전환 |
| `Ctrl+Z` / `Ctrl+Y` | 실행 취소/다시 실행 |
| `Ctrl+S` | 프로젝트 저장 |
| `Ctrl+휠` | 피아노 롤/플레이리스트 확대·축소 |
| `Shift+휠` | 피아노 롤 수평 이동 |
| `Alt` + 편집 | Snap 임시 해제 |

피아노 롤에 포커스가 있을 때 `Delete`, `Ctrl+A`, `Ctrl+D`, `Q`(퀀타이즈)도 사용할 수 있습니다.
드럼 편집기에서는 휠로 GM 악기 행을, `Shift+휠`로 패턴 타임라인을 이동합니다.

## 구조

```text
src/MidiEditor/
├─ Controls/    # DrawingContext 기반 Playlist/Piano Roll/Drum Grid
├─ Models/      # 프로젝트, 트랙, 노트 편집 모델
├─ Services/    # SoundFont 오디오, MIDI I/O, 프로젝트 파일, History
└─ MainWindow.* # DAW 레이아웃과 작업 흐름 연결
tests/MidiEditor.Tests/
└─ 프로젝트·MIDI round-trip, history, 샘플 스케줄 테스트
```

오디오 합성은 [MeltySynth](https://github.com/sinshu/meltysynth), Windows 출력은 [NAudio](https://github.com/naudio/NAudio), MIDI 파일 처리는 [DryWetMIDI](https://github.com/melanchall/drywetmidi)를 사용합니다.

## 검증

```powershell
dotnet test MidiEditor.sln
dotnet build MidiEditor.sln -c Release
```

## 자동 릴리스

`main` 브랜치에 푸시하면 GitHub Actions가 테스트를 실행하고 Windows x64용
self-contained 빌드를 생성합니다. 빌드 결과는
`release-YYYYMMDD-HHMMSS-실행번호` 형식의 UTC 시각 태그가 붙은 GitHub Release에
`PulseGrid-windows-x64.zip`으로 업로드됩니다.

현재 앱은 WPF와 Windows 오디오 출력 API를 사용하므로 macOS 빌드는 지원하지 않습니다.
