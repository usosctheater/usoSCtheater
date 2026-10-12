using System.Collections.Specialized;
using System.Windows.Controls;
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
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[^1]);
    }
}
