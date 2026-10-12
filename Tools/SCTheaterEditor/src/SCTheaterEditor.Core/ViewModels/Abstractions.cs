using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>화면 스레드로 넘기기 (WPF: Dispatcher, 테스트: 바로 실행)</summary>
public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action);
}

/// <summary>창·대화상자·탐색기 (ViewModel이 WPF를 직접 부르지 않도록)</summary>
public interface IDialogService
{
    /// <summary>확인/취소 질문. 확인이면 true</summary>
    bool Confirm(string title, string message);

    /// <summary>목록 변경 알림 창</summary>
    void ShowChangeNotice(CatalogDiff diff, DateTime previousAt);

    /// <summary>탐색기 열기 (파일이면 선택한 상태로)</summary>
    void OpenInExplorer(string path);
}

/// <summary>탭 사이 메시지: 이 Spine 세트를 열어 달라 (리소스 탭 → 뷰어, 3회차부터 스크립트 탭)</summary>
public sealed record OpenSpineMessage(SpineAsset Asset);
