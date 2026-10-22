using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 추가: 여러 화면에서 같은 설정을 바꾸는 흐름을 한 곳에 (설정 관리 규칙 — TE 핸드오프 0절).
/// 바꾸면 저장 요청 + SettingsChangedMessage. 목록 갱신 등은 각 기능이 알림을 받아 처리
/// </summary>
internal static class SettingsFlow
{
    /// <summary>시나리오 폴더 지정 (스크립트 표 [폴더 지정…]·설정 탭). 바꿨으면 그 폴더, 취소면 null</summary>
    public static string? PickScenarioFolder(AppSettings settings, IDialogService dialogs, Action saveSettings, IMessenger messenger)
    {
        string? folder = dialogs.PickFolder("시나리오 폴더 지정 (게임 Assets\\Resources\\Scenario)", settings.Editor.ScenarioFolder);
        if (folder == null) return null;
        settings.Editor.ScenarioFolder = folder;
        saveSettings();
        messenger.Send(new SettingsChangedMessage(SettingsArea.ScenarioFolder));
        return folder;
    }
}
