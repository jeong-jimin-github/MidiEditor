<div align="center">
  <img src="docs/images/pulsegrid-logo.svg" alt="PulseGrid MIDI Studio" width="620">
  <p><strong>아이디어를 빠르게 연주 가능한 MIDI와 보컬 트랙으로.</strong></p>
  <p>피아노 롤, 플레이리스트, 드럼 시퀀서와 OpenUtau 보컬 워크플로가 한 화면에 담긴 Windows용 음악 편집기입니다.</p>
</div>

![PulseGrid에서 멀티트랙 곡의 드럼 패턴과 벨로시티를 편집하는 화면](docs/images/pulsegrid-main.jpg)

## 주요 기능

- **한 화면 멀티트랙 작업** — 트랙별 악기·채널·색상과 Mute/Solo를 설정하고 곡 전체 구성을 한눈에 확인할 수 있습니다.
- **직관적인 피아노 롤** — 클릭과 드래그로 노트를 만들고, 이동하고, 길이와 벨로시티를 다듬을 수 있습니다.
- **드럼 스텝 시퀀서** — General MIDI 드럼 악기를 빠르게 탐색하며 리듬을 그리듯 입력할 수 있습니다.
- **Vocal / OpenUtau 워크플로** — Vocal 트랙에서 노트별 가사(alias)를 편집하고, UTAU 호환 보이스뱅크를 즉시 전환하며, 빠른 WAV 미리듣기 또는 OpenUtau로 UST 넘기기를 지원합니다.
- **기본 SoundFont 포함** — CC0로 배포되는 `ChaosBank.sf2`가 함께 제공되어 첫 실행부터 악기/드럼을 재생할 수 있으며 원하는 `.sf2`로 교체할 수 있습니다.
- **MIDI 및 프로젝트 파일** — 멀티트랙 `.mid` 파일을 가져오거나 내보내고, 작업을 `.pulsegrid` 프로젝트로 저장할 수 있습니다.
- **편집 도구** — 실행 취소/다시 실행, Snap, 확대·축소, 루프 재생과 퀀타이즈를 지원합니다.
- **다국어 UI** — 한국어, English, 日本語, 简体中文을 지원합니다. 첫 실행에서는 Windows 표시 언어를 자동 감지하고, 이후에는 하단 언어 선택기에서 즉시 변경한 값을 기억합니다.

## 시작하기

1. [Releases](../../releases)에서 최신 `PulseGrid-windows-x64.zip`을 받습니다.
2. ZIP 파일의 압축을 푼 뒤 `PulseGrid.exe`를 실행합니다.
3. Windows 표시 언어에 맞춰 UI가 자동으로 선택됩니다. 하단의 언어 선택기에서 **한국어 / English / 日本語 / 简体中文** 중 원하는 언어로 언제든 변경할 수 있습니다.
4. 번들된 기본 SoundFont가 자동으로 로드되므로 바로 재생할 수 있습니다. 다른 SF2를 쓰려면 왼쪽 아래 **SoundFont** 버튼에서 선택합니다.
5. 보컬 작업은 트랙 목록 위 **V** 버튼으로 Vocal 트랙을 추가합니다.
6. **⚙ 보컬/OpenUtau 설정**에서 보이스뱅크 PATH, `OpenUtau.exe`, 필요 시 `resampler`/`wavtool`을 지정합니다.

> PulseGrid는 Windows 10/11 및 .NET 8 기반입니다. OpenUtau 자체는 번들하지 않으며, 설치된 OpenUtau를 사용하려면 실행 파일 경로를 한 번 지정하면 됩니다.

## Vocal / OpenUtau 사용법

1. **V** 버튼으로 Vocal 트랙을 추가합니다. 기본으로 PulseGrid의 작은 CC0 테스트 보이스뱅크가 선택됩니다.
2. 피아노 롤에서 Vocal 노트를 만들고 노트를 선택한 뒤 왼쪽 **LYRIC** 칸에 `a`, `ka` 같은 가사/alias를 입력합니다. 여러 노트를 선택하면 한 번에 같은 가사를 넣을 수 있습니다.
3. **Voicebank** 콤보에서 현재 트랙의 보이스뱅크를 즉시 바꿀 수 있습니다.
4. **빠른 미리듣기**는 선택된 UTAU 보이스뱅크 샘플을 이용해 현재 Vocal 트랙을 빠르게 WAV로 렌더해 재생합니다.
5. **OpenUtau 열기**는 현재 Vocal 트랙을 UST로 변환하고, 설정된 VoiceDir/resampler/wavtool 정보를 포함해 OpenUtau에 전달합니다.

기본 보이스뱅크는 즉시 기능을 시험하기 위한 합성 모음 샘플입니다. 실제 제작에는 원하는 OpenUtau/UTAU 호환 보이스뱅크를 연결하는 것을 권장합니다.

## 기본 조작

| 조작 | 기능 |
|---|---|
| 빈 피아노/보컬 롤에서 좌클릭 후 드래그 | 노트 생성 및 길이 지정 |
| 노트 몸통 드래그 | 시간 및 음정 이동 |
| 노트 오른쪽 가장자리 드래그 | 노트 길이 조절 |
| 노트 또는 드럼 스텝 우클릭 | 삭제 |
| 드럼 그리드에서 좌클릭 후 드래그 | 스텝 연속 입력 |
| 하단 Velocity 막대 드래그 | 벨로시티 조절 |
| Vocal 노트 선택 후 LYRIC 입력 | 가사/UTAU alias 편집 |
| `Space` | 재생 / 일시정지 |
| `Home` | 정지 후 처음으로 이동 |
| `L` | 루프 켜기 / 끄기 |
| `Tab` | 피아노/보컬 롤 / 드럼 패턴 전환 |
| `Ctrl+Z` / `Ctrl+Y` | 실행 취소 / 다시 실행 |
| `Ctrl+S` | 프로젝트 저장 |
| `Ctrl+휠` | 가로 확대 / 축소 |
| `Alt`를 누른 채 편집 | Snap 임시 해제 |

피아노/보컬 롤에서는 `Delete`, `Ctrl+A`, `Ctrl+D`, `Q`(퀀타이즈)도 사용할 수 있습니다. 드럼 편집기에서는 마우스 휠로 악기 행을 탐색하고, `Shift+휠`로 타임라인을 이동할 수 있습니다.

## 지원 파일

| 형식 | 용도 |
|---|---|
| `.pulsegrid` | 편집 가능한 PulseGrid 프로젝트 저장 및 열기; Vocal 트랙의 보이스뱅크와 가사도 저장 |
| `.mid` / `.midi` | 멀티트랙 MIDI 가져오기 및 내보내기 |
| `.sf2` | 악기 및 드럼 소리 재생 |
| `.ust` | Vocal 트랙을 OpenUtau로 넘길 때 자동 생성되는 교환 형식 |

## 번들 에셋 라이선스

- `Assets/SoundFonts/ChaosBank.sf2` — Chaos Bank v1.9, 소스 카탈로그에서 **CC0 1.0**으로 명시. 자세한 출처는 `THIRD_PARTY_NOTICES.md` 및 같은 폴더의 `README.txt`를 참조하세요.
- `Assets/Voicebanks/PulseGridDefault` — PulseGrid용으로 직접 생성한 테스트 보이스뱅크이며 **CC0 1.0**으로 배포합니다.

---

<div align="center"><sub>음악에 집중할 수 있는 빠르고 간결한 MIDI + Vocal 작업 공간.</sub></div>
