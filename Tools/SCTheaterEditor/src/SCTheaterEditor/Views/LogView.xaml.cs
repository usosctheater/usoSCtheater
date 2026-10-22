using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>새 줄이 오면 맨 아래로 스크롤 (화면 동작이라 View에서 처리). 추가(6회차): 줄 복사</summary>
public partial class LogView : UserControl
{
    private LogViewModel? _vm;

    public LogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.Lines.CollectionChanged -= OnLinesChanged;
            _vm = DataContext as LogViewModel;
            if (_vm != null) _vm.Lines.CollectionChanged += OnLinesChanged;
        };
        // 추가: 처음 보일 때(로그 창을 열 때) 맨 아래로 — 목록이 그려진 뒤에 내림
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, ScrollToEnd);
    }

    private bool _scrollPending;   // 추가(6회차): 여러 줄이 한꺼번에 와도 스크롤은 한 번

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 변경(6회차): 바로 스크롤하면 목록이 새 줄 알림을 받기 전에 다시 그려져 '항목 수 불일치'로 종료됨(E-005) → 반영된 뒤로 미룸
        if (e.Action == NotifyCollectionChangedAction.Add && !_scrollPending)
        {
            _scrollPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { _scrollPending = false; ScrollToEnd(); });
        }
    }

    // 추가
    private void ScrollToEnd()
    {
        if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
    }

    // ------------------------------------------------------------------ 추가(6회차): 줄 복사 (선택한 줄은 목록 순서대로)

    private void LogList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { CopySelected(); e.Handled = true; }
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e) => CopySelected();
    private void CopyAll_Click(object sender, RoutedEventArgs e) => Copy(LogList.Items.Cast<string>());

    private void CopySelected()
    {
        var selected = new HashSet<string>(LogList.SelectedItems.Cast<string>());
        Copy(LogList.Items.Cast<string>().Where(selected.Contains));
    }

    private static void Copy(IEnumerable<string> lines)
    {
        string text = string.Join(Environment.NewLine, lines);
        if (text.Length == 0) return;
        try { Clipboard.SetText(text); }
        catch (Exception ex) { AppLog.Shared.Warn($"로그 복사 실패: {ex.Message}"); }   // 다른 프로그램이 클립보드를 쓰는 중이면
    }
}
