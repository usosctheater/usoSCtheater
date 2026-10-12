using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Services;

/// <summary>IUiDispatcher의 WPF 구현</summary>
public sealed class WpfDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();
    public void Post(Action action) => dispatcher.InvokeAsync(action);
}

/// <summary>IDialogService의 WPF 구현 (주인 창 = 메인 창)</summary>
public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

    public bool Confirm(string title, string message) =>
        (Owner != null
            ? MessageBox.Show(Owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)) == MessageBoxResult.OK;

    public void ShowChangeNotice(CatalogDiff diff, DateTime previousAt) =>
        new ChangeNoticeWindow(Owner, diff, previousAt).ShowDialog();

    public void OpenInExplorer(string path)
    {
        // 파일이면 탐색기에서 그 파일을 선택한 상태로, 폴더면 폴더를 연다
        string args = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }
}
