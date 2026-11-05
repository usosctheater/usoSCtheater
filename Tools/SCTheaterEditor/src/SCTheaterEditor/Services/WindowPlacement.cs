using System.Windows;

namespace SCTheaterEditor.Services;

/// <summary>추가(번역 모드 3회차): 창 위치 복원 공용 (메인 창·로그 창·번역 창 — 이전에는 로그 창·번역 창에 각자 있었음)</summary>
internal static class WindowPlacement
{
    /// <summary>창 왼쪽 위가 지금 모니터들 안에 있는지 (모니터 구성이 바뀌어 화면 밖에 뜨는 것 방지)</summary>
    public static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 20 && top >= SystemParameters.VirtualScreenTop - 20 &&
        left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
        top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;
}
