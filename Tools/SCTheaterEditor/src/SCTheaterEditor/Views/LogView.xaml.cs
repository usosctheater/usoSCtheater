using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>새 줄이 오면 맨 아래로 스크롤 (화면 동작이라 View에서 처리)</summary>
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

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) ScrollToEnd();   // 변경: 공용 함수로
    }

    // 추가
    private void ScrollToEnd()
    {
        if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
    }
}
