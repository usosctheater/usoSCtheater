using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>
/// 스크립트 탭 화면. 입력 처리(키·목록 클릭)와 WebView2 연결만 여기서, 동작은 ScriptViewModel.
/// 단축키: Space 재생/일시정지 · R 초기화 · Enter 미리보기 적용 · Esc 취소 · Delete 내리기
/// (무대에 포커스가 있을 때는 페이지가 key 메시지로 넘겨 ViewModel.HandleKey로 같은 처리)
/// </summary>
public partial class ScriptView : UserControl
{
    public ScriptView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try { await App.Stage.AttachAsync(StageWeb, App.Settings.SaveFolder, () => App.Backgrounds.Folder); }   // 변경(5회차): 지금 배경 폴더
            catch (Exception ex) { AppLog.Shared.Error("무대(WebView2)를 시작하지 못했습니다 — WebView2 런타임 설치 여부를 확인하세요", ex); }
        };
    }

    private ScriptViewModel? Vm => DataContext as ScriptViewModel;

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        // 추가(4-1): 스크립트 표 단축키는 탭 어디서나 (무대에 포커스가 있어도)
        if (Vm.Editor is { } editor && HandleEditorShortcut(e, editor, Vm)) { e.Handled = true; return; }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        // 추가(4-2): F5 스크립트 → 무대 (입력 중인 칸을 먼저 확정)
        if (e.Key == Key.F5)
        {
            ActEditorView.CommitFocusedBox();
            Vm.SyncFromScriptCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.OriginalSource is TextBox or ComboBox or ComboBoxItem) return;   // 글자 입력·드롭다운에서는 무시
        if (IsInside(e.OriginalSource as DependencyObject, EditorPanel)) return;   // 추가(4-1): 표 안의 키(Delete·Space·Enter 등)는 표가 처리
        // 애니메이션 목록에서 Enter는 "이 애니메이션 적용" (목록이 직접 처리)
        if (e.Key == Key.Enter && IsInside(e.OriginalSource as DependencyObject, AnimList)) return;
        string? key = e.Key switch
        {
            Key.Space => " ",
            Key.R => "R",
            Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.Delete => "Delete",
            _ => null,
        };
        if (key != null && Vm.HandleKey(key)) e.Handled = true;
    }

    /// <summary>
    /// 추가(4-1): Ctrl+S 저장 · Ctrl+O 파일 열기 · Ctrl+Z 되돌리기 · Ctrl+Y(Ctrl+Shift+Z) 다시 하기. 처리했으면 true
    /// 추가: Ctrl+Shift+C / D / V 줄 복사·잘라내기·붙여넣기
    /// 변경: 표의 칸이 모두 입력 상자가 되어, 확정 전 글자가 있는 상자에서만 Ctrl+Z·Y를 그 상자에 맡기고 나머지는 표 되돌리기.
    /// 저장·열기 전에는 입력 중인 칸을 먼저 확정
    /// </summary>
    private static bool HandleEditorShortcut(KeyEventArgs e, ActEditorViewModel editor, ScriptViewModel stage)
    {
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) == 0 || (mods & ModifierKeys.Alt) != 0) return false;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        bool typing = e.OriginalSource is TextBox tb && tb.GetBindingExpression(TextBox.TextProperty) is { IsDirty: true };
        // 추가: Ctrl+Shift+C / D / V 줄 복사·잘라내기·붙여넣기 — 칸에 입력 중이어도 줄 단위 (입력 중인 칸을 먼저 확정)
        // 잘라내기는 D (Ctrl+Shift+X는 사용자 PC에서 다른 프로그램의 전역 단축키라 TE에 키가 오지 않음, 2026-10-20)
        if (shift && e.Key is Key.C or Key.D or Key.V)
        {
            ActEditorView.CommitFocusedBox();
            var lc = e.Key switch
            {
                Key.C => editor.CopyLinesCommand,
                Key.D => editor.CutLinesCommand,
                _ => editor.PasteLinesCommand,
            };
            if (lc.CanExecute(null)) lc.Execute(null);
            return true;
        }
        // 추가(4-2): Ctrl+Enter 라인에 적용 · Ctrl+N 줄 추가 (입력 중인 칸을 먼저 확정)
        if (!shift && (e.Key == Key.Enter || e.Key == Key.N))
        {
            ActEditorView.CommitFocusedBox();
            var c = e.Key == Key.Enter ? stage.ApplyToLineCommand : editor.AddTextCommand;
            if (c.CanExecute(null)) c.Execute(null);
            return true;
        }
        System.Windows.Input.ICommand? cmd = e.Key switch
        {
            Key.S when !shift => editor.SaveCommand,
            Key.O when !shift => editor.OpenFileCommand,
            Key.Z or Key.Y when typing => null,
            Key.Z => shift ? editor.RedoCommand : editor.UndoCommand,
            Key.Y when !shift => editor.RedoCommand,
            _ => null,
        };
        if (cmd == null) return false;
        if (cmd == editor.SaveCommand || cmd == editor.OpenFileCommand) ActEditorView.CommitFocusedBox();
        if (cmd.CanExecute(null)) cmd.Execute(null);
        return true;
    }

    /// <summary>항목 클릭(같은 항목을 다시 눌러도) → 적용</summary>
    private void AnimList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(AnimList, (DependencyObject)e.OriginalSource) is ListBoxItem { IsEnabled: true } item)
            Vm?.ApplyAnimCommand.Execute(item.DataContext as AnimListItem);
    }

    private void AnimList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || AnimList.SelectedItem is not AnimListItem item) return;
        Vm?.ApplyAnimCommand.Execute(item);
        e.Handled = true;
    }

    private void LipSeconds_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private void DevTools_Click(object sender, RoutedEventArgs e) => App.Stage.OpenDevTools();

    private void OpenBackgroundFolder_Click(object sender, RoutedEventArgs e)
    {
        // 변경(5회차): 게임 Resources\BG — 없으면 만들지 않고 안내만
        string? folder = App.Backgrounds.Folder;
        if (folder == null || !System.IO.Directory.Exists(folder))
        {
            AppLog.Shared.Warn(folder == null
                ? "배경 폴더: 설정 탭에서 게임 Resources 폴더를 지정하세요"
                : $"배경 폴더가 없습니다: {folder}");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { AppLog.Shared.Error("배경 폴더 열기 실패", ex); }
    }

    private static bool IsInside(DependencyObject? element, DependencyObject ancestor)
    {
        for (var d = element; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d == ancestor) return true;
        return false;
    }
}
