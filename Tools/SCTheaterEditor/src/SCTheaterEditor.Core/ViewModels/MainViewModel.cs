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
                         AudioMemoStore? audioMemos = null)   // 추가(6회차): 오디오 메모 (exe 옆 AudioMemo.xml)
    {
        Settings = settings;
        Library = library;
        var units = IdolUnits.LoadEmbedded();
        Log = new LogViewModel(log, ui);
        Download = new DownloadViewModel(settings, library, units, dialogs, catalogFactory, downloaderFactory, saveSettings, log);
        Resource = new ResourceViewModel(settings, library, units, dialogs, saveSettings, messenger, log);
        Script = new ScriptViewModel(settings, library, backgrounds, stage, LipRules.LoadEmbedded(), messenger, saveSettings, log, audioMemos);   // 변경(6회차)
        // 추가(4-1): 스크립트 표 (스크립트 탭 아래쪽)
        Editor = new ActEditorViewModel(settings, library, dialogs, ui, saveSettings, log, messenger: messenger);   // 변경: messenger
        Script.Editor = Editor;
        Editor.StampProvider = () => Script.CurrentStamp;   // 추가(4-2): 줄 추가 때 무대에서 선택한 캐릭터 정보
        SettingsPage = new SettingsViewModel(settings, dialogs, saveSettings, messenger, log);   // 추가: 설정 탭
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
