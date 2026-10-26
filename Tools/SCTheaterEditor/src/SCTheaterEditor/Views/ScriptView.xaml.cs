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
            // 변경(5회차): 지금 배경 폴더, 변경(6회차): 오디오용 게임 Resources 폴더
            try { await App.Stage.AttachAsync(StageWeb, App.Settings.SaveFolder, () => App.Backgrounds.Folder,
                                              () => App.Settings.Editor.GameResourcesFolder); }
            catch (Exception ex) { AppLog.Shared.Error("무대(WebView2)를 시작하지 못했습니다 — WebView2 런타임 설치 여부를 확인하세요", ex); }
        };
    }

    private ScriptViewModel? Vm => DataContext as ScriptViewModel;

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        // 추가(4-1): 스크립트 표 단축키는 탭 어디서나 (무대에 포커스가 있어도)
        bool inAudio = IsInside(e.OriginalSource as DependencyObject, AudioPanel);   // 추가(6회차): 오디오 목록 안의 키
        // 추가(9회차): 오디오 목록에서 Ctrl+Alt+Enter = [적용] (Voice = 보이스 일괄 추가 창). Alt가 눌리면 WPF는 Key.System + SystemKey로 줌
        if (inAudio && (e.Key == Key.System ? e.SystemKey : e.Key) == Key.Enter
            && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.OriginalSource is not TextBox)
        {
            ActEditorView.CommitFocusedBox();
            Vm.Audio.ApplyCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (Vm.Editor is { } editor && HandleEditorShortcut(e, editor, Vm, inAudio)) { e.Handled = true; return; }
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
        if (inAudio) return;   // 추가(6회차): 오디오 목록의 Space(재생) 등은 목록이 처리 — 무대로 넘기지 않음
        if (IsInside(e.OriginalSource as DependencyObject, EditorPanel)) return;   // 추가(4-1): 표 안의 키(Delete·Space·Enter 등)는 표가 처리
        // 애니메이션 목록에서 Enter는 "이 애니메이션 적용" (목록이 직접 처리)
        if (e.Key == Key.Enter && IsInside(e.OriginalSource as DependencyObject, ActorPanel.AnimListBox)) return;   // 변경(7회차): Spine 패널로 옮김
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
    private static bool HandleEditorShortcut(KeyEventArgs e, ActEditorViewModel editor, ScriptViewModel stage, bool inAudio)
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
        // 추가(6회차): 오디오 목록에 포커스가 있으면 Ctrl+Enter = 선택한 오디오 키를 줄에 넣기 (보이스는 Ctrl+Shift+Enter = 이어 붙이기)
        //             메모 입력 중이면 메모만 확정. 그 밖의 Ctrl+Shift+Enter는 Ctrl+Enter와 같음
        if (e.Key == Key.Enter && inAudio)
        {
            if (e.OriginalSource is TextBox { DataContext: AudioRow memoRow }) memoRow.CommitEdit();
            else stage.Audio.InsertSelected(append: shift);
            return true;
        }
        // 추가(4-2): Ctrl+Enter 라인에 적용 · Ctrl+N 줄 추가 (입력 중인 칸을 먼저 확정)
        if (e.Key == Key.Enter || (!shift && e.Key == Key.N))   // 변경(6회차): Ctrl+Shift+Enter도 라인에 적용
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

    // (이동, 7회차) 애니메이션 목록·립 초·개발자 도구 → ActorPanelView, 오디오 목록·메모 → AudioPanelView

    /// <summary>
    /// 추가(7회차 확인 후): Spine ↔ 오디오 경계선 — 두 패널이 폭을 주고받음 (합은 그대로, 각자 최소 폭 이상).
    /// 값은 Layout(설정)으로 넣고 칸 폭은 바인딩으로 따라옴
    /// </summary>
    private void PanelSplitter_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (Vm?.Layout is not { } layout) return;
        double min = ScriptLayoutSettings.MinPanelWidth;
        double total = layout.ActorWidth + layout.AudioWidth;
        double actor = Math.Round(Math.Clamp(layout.ActorWidth + e.HorizontalChange, min, Math.Max(min, total - min)));
        if (actor == layout.ActorWidth) return;
        layout.ActorWidth = actor;
        layout.AudioWidth = total - actor;
    }

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
