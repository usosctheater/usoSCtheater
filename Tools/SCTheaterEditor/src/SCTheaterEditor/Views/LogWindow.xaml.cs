using System.Windows;
using System.Windows.Input;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>추가: 로그 창 — 메인 창 소속(같이 최소화·종료), 위치·크기는 닫을 때 설정에 저장</summary>
public partial class LogWindow : Window
{
    private readonly LogWindowSettings _place;

    public LogWindow(LogViewModel log, LogWindowSettings place)
    {
        InitializeComponent();
        DataContext = log;
        _place = place;
        Width = place.Width;
        Height = place.Height;
        if (place.Left is { } l && place.Top is { } t && IsOnScreen(l, t)) { Left = l; Top = t; }
        else WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closing += (_, _) => Store();
        // 추가: 옮기거나 크기를 바꿀 때마다 저장 — 창을 닫지 않고 TE를 꺼도 마지막 위치·크기 유지 (설정 저장은 0.4초 모아서 한 번)
        LocationChanged += (_, _) => Store();
        SizeChanged += (_, _) => Store();
    }

    /// <summary>창 왼쪽 위가 지금 모니터들 안에 있는지 (모니터 구성이 바뀌어 화면 밖에 뜨는 것 방지)</summary>
    private static bool IsOnScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 20 && top >= SystemParameters.VirtualScreenTop - 20 &&
        left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
        top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;

    private void Store()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (double.IsNaN(r.Left) || double.IsNaN(r.Top) || r.IsEmpty) return;   // 추가: 화면에 놓이기 전(위치 미정)에는 저장하지 않음
        (_place.Left, _place.Top, _place.Width, _place.Height) = (r.Left, r.Top, r.Width, r.Height);
        App.SaveSettingsSoon();
    }

    /// <summary>Ctrl+L: 로그 창에 포커스가 있어도 닫기</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Close();
            e.Handled = true;
        }
    }
}
