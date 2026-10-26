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

    // 추가(6회차): 예/아니오
    public bool AskYesNo(string title, string message) =>
        (Owner != null
            ? MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)) == MessageBoxResult.Yes;

    // 추가(9회차): 보이스 일괄 추가 미리보기
    public bool ShowVoiceBatch(VoiceBatchViewModel batch) =>
        new Views.VoiceBatchWindow(Owner, batch).ShowDialog() == true;

    public void ShowChangeNotice(CatalogDiff diff, DateTime previousAt) =>
        new ChangeNoticeWindow(Owner, diff, previousAt).ShowDialog();

    public void OpenInExplorer(string path)
    {
        // 파일이면 탐색기에서 그 파일을 선택한 상태로, 폴더면 폴더를 연다
        string args = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    // 추가(4-1): 막 열기·저장용

    public SaveChoice AskSave(string title, string message) =>
        Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) switch
        {
            MessageBoxResult.Yes => SaveChoice.Save,
            MessageBoxResult.No => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };

    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        if (Directory.Exists(initialFolder)) dialog.InitialDirectory = initialFolder;
        bool? ok = Owner != null ? dialog.ShowDialog(Owner) : dialog.ShowDialog();
        return ok == true ? dialog.FolderName : null;
    }

    public string? PickFile(string title, string filter, string? initialFolder)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (Directory.Exists(initialFolder)) dialog.InitialDirectory = initialFolder;
        bool? ok = Owner != null ? dialog.ShowDialog(Owner) : dialog.ShowDialog();
        return ok == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    // 추가: 클립보드 (다른 프로그램이 잡고 있으면 실패할 수 있음 → 로그 + null/false)
    public string? GetClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (Exception ex) { AppLog.Shared.Warn($"클립보드 읽기 실패: {ex.Message}"); return null; }
    }

    public bool SetClipboardText(string text)
    {
        try { Clipboard.SetDataObject(text, copy: true); return true; }
        catch (Exception ex) { AppLog.Shared.Warn($"클립보드 쓰기 실패: {ex.Message}"); return false; }
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner != null ? MessageBox.Show(Owner, message, title, buttons, image) : MessageBox.Show(message, title, buttons, image);
}
