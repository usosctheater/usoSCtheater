using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Services;
using SCTheaterEditor.Stage;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 메인 창: 탭별 ViewModel을 묶고, 공용 데이터(설정·SpineLibrary·배경)를 나눠 준다.
/// 탭: 다운로드 / 리소스 / 스크립트(3회차: 무대) / 라이선스
/// </summary>
public sealed class MainViewModel
{
    public MainViewModel(AppSettings settings, SpineLibrary library, BackgroundLibrary backgrounds, IStageHost stage,
                         IDialogService dialogs, IUiDispatcher ui,
                         Func<ICatalogSource> catalogFactory, Func<ISetDownloader> downloaderFactory,
                         Action saveSettings, IMessenger messenger, AppLog log,
                         GameData gameData,                   // 추가(번역 모드 2회차): 게임 데이터 복사본 (exe 옆 Data\)
                         AudioMemoStore? audioMemos = null,   // 추가(6회차): 오디오 메모 (exe 옆 AudioMemo.xml)
                         string? voiceMatchPath = null)       // 추가(번역 모드 5회차): VOICEVOX 매칭 (생략하면 exe 옆 VoiceMatch.xml)
    {
        _voicePath = voiceMatchPath ?? VoiceMatchData.DefaultPath;
        VoiceMatch = VoiceMatchData.Load(_voicePath);
        if (VoiceMatch.Problem.Length > 0) log.Warn($"보이스 매칭: {VoiceMatch.Problem}");
        Settings = settings;
        Library = library;
        var units = IdolUnits.LoadEmbedded();
        Log = new LogViewModel(log, ui);
        Download = new DownloadViewModel(settings, library, units, dialogs, catalogFactory, downloaderFactory, saveSettings, log);
        Resource = new ResourceViewModel(settings, library, units, dialogs, saveSettings, messenger, log);
        Script = new ScriptViewModel(settings, library, backgrounds, stage, gameData.Lips, messenger, saveSettings, log, audioMemos,
                                     stageTable: () => gameData.Stage);   //[스테이지 레이아웃] 무대 배치 표   // 변경(번역 모드 2회차): LipRules.LoadEmbedded() → gameData.Lips
        // 추가(4-1): 스크립트 표 (스크립트 탭 아래쪽)
        Editor = new ActEditorViewModel(settings, library, dialogs, ui, saveSettings, log, characters: gameData.Characters, messenger: messenger,
                                         stageTable: () => gameData.Stage);   //[스테이지 레이아웃] F5 무대 동기화의 배치 표   // 변경(번역 모드 2회차): characters
        Script.Editor = Editor;
        Editor.StampProvider = () => Script.CurrentStamp;   // 추가(4-2): 줄 추가 때 무대에서 선택한 캐릭터 정보
        Editor.VoiceMatch = VoiceMatch;   // 추가(번역 모드 5회차): [라이선스 내보내기]
        Editor.UsedCgKeysChanged += () => Resource.SetActKeys(Editor.UsedCgKeys);   // 추가(리소스 탭 막 사용 표시): ▶ · [이 막에서 쓰는 것만]
        SettingsPage = new SettingsViewModel(settings, dialogs, saveSettings, messenger, log, gameData: gameData,   // 변경(번역 모드 2회차): gameData
                                             voices: VoiceMatch, voicePath: _voicePath);                         // 변경(번역 모드 5회차): 보이스

        _ui = ui;                       // 추가(번역 모드 3회차): 번역 창
        _saveSettings = saveSettings;

        // 추가(번역 모드 2회차): 게임 데이터 상태 → 스크립트 표 경고, 다시 읽으면 화자 표시 갱신
        GameData = gameData;
        gameData.Changed += OnGameDataChanged;
        OnGameDataChanged();
    }

    /// <summary>추가(번역 모드 2회차): 게임 데이터 복사본 (번역 창 언어 목록 등에서 씀)</summary>
    public GameData GameData { get; }

    private readonly IUiDispatcher _ui;     // 추가(번역 모드 3회차)
    private readonly Action _saveSettings;

    /// <summary>추가(번역 모드 3회차): 번역 창을 열 때마다 새로 만듦 (닫으면 화면이 Dispose). 무대 동기화 = 스크립트 탭 F5와 같은 명령</summary>
    public TranslationViewModel CreateTranslation() =>
        new(Editor, GameData, Settings.Translation, _saveSettings, () => Script.SyncFromScriptCommand.Execute(null), _ui,
            new ClaudeCli(() => Settings.PreTranslate.ClaudePath, AppLog.Shared),          // 추가(번역 모드 4회차): 가번역
            new TranslationGuideStore(TranslationGuideStore.DefaultFolder),
            () => TimeSpan.FromMinutes(Math.Clamp(Settings.PreTranslate.TimeoutMinutes, PreTranslateSettings.MinTimeout, PreTranslateSettings.MaxTimeout)),
            AppLog.Shared,
            VoiceMatch, () => Settings.Voice.ExportFolder);                                   // 추가(번역 모드 5회차): [TXT로 내보내기]

    /// <summary>추가(번역 모드 5회차): VOICEVOX 매칭 (설정 탭에서 고치면 같은 객체 — 번역 창·스크립트 표가 바로 새 매칭을 씀)</summary>
    public VoiceMatchData VoiceMatch { get; }
    private readonly string _voicePath;

    private void OnGameDataChanged()
    {
        Editor.GameDataWarning = GameData.HasProblem ? "⚠ 게임 데이터가 TE 복사본과 다르거나 없음 — 설정 탭 > 게임 데이터" : "";
        Editor.OnGameDataChanged();
        Script.OnStageDataChanged();   //[스테이지 레이아웃] 표가 바뀌면 분할 목록·배치·안내선 다시
    }

    /// <summary>추가: 설정 탭 (설정 관리 규칙 — TE 핸드오프 0절)</summary>
    public SettingsViewModel SettingsPage { get; }

    public AppSettings Settings { get; }
    public SpineLibrary Library { get; }
    public LogViewModel Log { get; }
    public DownloadViewModel Download { get; }
    public ResourceViewModel Resource { get; }
    public ScriptViewModel Script { get; }

    /// <summary>추가(4-1): 스크립트 표 (막 열기·편집·저장)</summary>
    public ActEditorViewModel Editor { get; }

    /// <summary>창이 뜬 뒤 1회: 저장된 목록 읽기 (매니페스트 v2 이전 포함) + 마지막 막·라인 다시 열기</summary>
    public void Initialize()
    {
        Download.LoadSavedSnapshot();
        Editor.Initialize();   // 추가(4-1)
    }
}
