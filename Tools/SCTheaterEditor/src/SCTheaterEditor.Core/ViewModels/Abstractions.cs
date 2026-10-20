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

    /// <summary>추가(4-1): 저장하지 않은 변경 — 저장 / 저장 안 함 / 취소</summary>
    SaveChoice AskSave(string title, string message);

    /// <summary>추가(4-1): 폴더 고르기 (취소면 null)</summary>
    string? PickFolder(string title, string? initialFolder);

    /// <summary>추가(4-1): 파일 고르기 (filter 예: "막 XML (*.xml)|*.xml", 취소면 null)</summary>
    string? PickFile(string title, string filter, string? initialFolder);

    /// <summary>추가(4-1): 오류 알림</summary>
    void ShowError(string title, string message);

    /// <summary>추가: 클립보드 글자 읽기 (없거나 읽지 못하면 null)</summary>
    string? GetClipboardText();

    /// <summary>추가: 클립보드에 글자 쓰기 (TE를 꺼도 남음). 쓰지 못하면 false</summary>
    bool SetClipboardText(string text);
}

/// <summary>추가(4-1): 저장 확인 결과</summary>
public enum SaveChoice { Save, Discard, Cancel }

/// <summary>
/// 탭 사이 메시지: 이 Spine 세트를 스크립트 탭 무대에 올려 달라 (리소스 탭 → 스크립트 탭).
/// Immediate = false: 미리보기(강조 표시, Enter 적용 / Esc 취소), true: 바로 추가(Ctrl+Space)
/// </summary>
public sealed record OpenSpineMessage(SpineAsset Asset, bool Immediate = false);

public enum AppTab { Download, Resource, Script, License }

/// <summary>탭 사이 메시지: 이 탭으로 이동해 달라 (예: 미리보기 Esc → 리소스 탭으로 복귀)</summary>
public sealed record ShowTabMessage(AppTab Tab);
