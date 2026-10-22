using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 추가: 여러 화면에서 같은 설정을 바꾸는 흐름을 한 곳에 (설정 관리 규칙 — TE 핸드오프 0절).
/// 바꾸면 저장 요청 + SettingsChangedMessage. 목록 갱신 등은 각 기능이 알림을 받아 처리
/// </summary>
internal static class SettingsFlow
{
    /// <summary>
    /// 변경(5회차): 게임 Resources 폴더 지정 (설정 탭에서만, 이전: 시나리오 폴더 지정).
    /// 이름이 Resources가 아니면 한 번 확인. 바꿨으면 그 폴더, 취소면 null
    /// </summary>
    public static string? PickResourcesFolder(AppSettings settings, IDialogService dialogs, Action saveSettings, IMessenger messenger)
    {
        string? picked = dialogs.PickFolder("게임 Resources 폴더 지정 (게임 Assets\\Resources)", settings.Editor.GameResourcesFolder);
        if (picked == null) return null;
        if (!Path.GetFileName(picked.TrimEnd('\\', '/')).Equals("Resources", StringComparison.OrdinalIgnoreCase) &&
            !dialogs.Confirm("게임 Resources 폴더 확인",
                $"{picked}\n\n폴더 이름이 'Resources'가 아닙니다. 이 폴더 아래의 Scenario · Spine · BG 폴더를 씁니다. 계속할까요?"))
            return null;
        settings.Editor.GameResourcesFolder = picked;
        saveSettings();
        messenger.Send(new SettingsChangedMessage(SettingsArea.ResourcesFolder));
        return picked;
    }
}
