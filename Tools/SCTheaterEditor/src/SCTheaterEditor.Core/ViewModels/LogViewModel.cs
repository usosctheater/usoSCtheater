using System.Collections.ObjectModel;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 화면 로그 (최근 MaxLines줄). AppLog를 구독하고, 다른 스레드에서 온 로그는 화면 스레드로 넘긴다.
/// </summary>
public sealed class LogViewModel
{
    public const int MaxLines = 1000;

    private readonly IUiDispatcher _ui;

    public LogViewModel(AppLog log, IUiDispatcher ui)
    {
        _ui = ui;
        log.Written += OnWritten;
    }

    public ObservableCollection<string> Lines { get; } = new();

    private void OnWritten(LogEntry entry)
    {
        if (_ui.CheckAccess()) Add(entry);
        else _ui.Post(() => Add(entry));
    }

    private void Add(LogEntry entry)
    {
        Lines.Add(AppLog.Format(entry));
        while (Lines.Count > MaxLines) Lines.RemoveAt(0);
    }
}
