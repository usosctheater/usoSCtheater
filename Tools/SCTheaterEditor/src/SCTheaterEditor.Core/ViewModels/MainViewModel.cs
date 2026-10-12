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
                         Action saveSettings, IMessenger messenger, AppLog log)
    {
        Settings = settings;
        Library = library;
        var units = IdolUnits.LoadEmbedded();
        Log = new LogViewModel(log, ui);
        Download = new DownloadViewModel(settings, library, units, dialogs, catalogFactory, downloaderFactory, saveSettings, log);
        Resource = new ResourceViewModel(settings, library, units, dialogs, saveSettings, messenger, log);
        Script = new ScriptViewModel(settings, library, backgrounds, stage, LipRules.LoadEmbedded(), messenger, saveSettings, log);
    }

    public AppSettings Settings { get; }
    public SpineLibrary Library { get; }
    public LogViewModel Log { get; }
    public DownloadViewModel Download { get; }
    public ResourceViewModel Resource { get; }
    public ScriptViewModel Script { get; }

    /// <summary>창이 뜬 뒤 1회: 저장된 목록 읽기 (매니페스트 v2 이전 포함)</summary>
    public void Initialize() => Download.LoadSavedSnapshot();
}
