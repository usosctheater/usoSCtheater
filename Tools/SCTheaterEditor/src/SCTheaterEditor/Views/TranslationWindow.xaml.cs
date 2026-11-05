using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>
/// 추가(번역 모드 3회차): 번역 창 — 메인 창 소속(같이 최소화·종료), 위치·크기는 바뀔 때마다 설정에 저장 (로그 창과 같은 방식).
/// 닫으면 ViewModel을 Dispose (문서 알림 구독 해제)
/// </summary>
public partial class TranslationWindow : Window
{
    private readonly TranslationViewModel _vm;
    private readonly ActEditorViewModel _editor;
    private readonly TranslationWindowSettings _place;

    public TranslationWindow(TranslationViewModel vm, ActEditorViewModel editor, TranslationWindowSettings place)
    {
        InitializeComponent();
        _vm = vm;
        _editor = editor;
        _place = place;
        DataContext = vm;
        Width = place.Width;
        Height = place.Height;
        if (place.Left is { } l && place.Top is { } t && WindowPlacement.IsOnScreen(l, t)) { Left = l; Top = t; }
        else WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closing += (_, _) => { CommitPending(); Store(); };
        Closed += (_, _) => _vm.Dispose();
        LocationChanged += (_, _) => Store();
        SizeChanged += (_, _) => Store();
    }

    private void Store()
    {
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (double.IsNaN(r.Left) || double.IsNaN(r.Top) || r.IsEmpty) return;
        (_place.Left, _place.Top, _place.Width, _place.Height) = (r.Left, r.Top, r.Width, r.Height);
        App.SaveSettingsSoon();
    }

    /// <summary>
    /// 이 창에서 입력 중인 칸 확정 (저장·창 닫기 전, 메인 창이 닫히기 전) — 창이 비활성이어도.
    /// Keyboard.FocusedElement는 활성 창 기준이라 메인 창 닫기 버튼을 누르면 이 창의 칸을 놓침 → 창 안의 포커스를 봄
    /// </summary>
    public void CommitPending()
    {
        if (FocusManager.GetFocusedElement(this) is TextBox tb) tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private void Cell_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TranslationCell cell }) _vm.OnCellFocused(cell);
    }

    /// <summary>Enter = 확정 후 다음 칸, Shift+Enter = 확정 후 이전 칸, Esc = 입력 취소 (Tab / Shift+Tab은 WPF 기본 — 같은 순서)</summary>
    private void Cell_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Enter && mods is ModifierKeys.None or ModifierKeys.Shift)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            box.MoveFocus(new TraversalRequest(mods == ModifierKeys.Shift ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && mods == ModifierKeys.None)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+T 닫기 · Ctrl+S 저장 · Ctrl+Z / Ctrl+Y(Ctrl+Shift+Z) 되돌리기·다시 하기 — 확정 전 글자가 있는 칸에서는 그 칸의 되돌리기</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) == 0 || (mods & ModifierKeys.Alt) != 0) return;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        if (e.Key == Key.T && !shift)
        {
            Close();   // 입력 중인 칸은 Closing에서 확정
            e.Handled = true;
            return;
        }
        bool typing = e.OriginalSource is TextBox tb && tb.GetBindingExpression(TextBox.TextProperty) is { IsDirty: true };
        ICommand? cmd = e.Key switch
        {
            Key.S when !shift => _editor.SaveCommand,
            Key.Z or Key.Y when typing => null,
            Key.Z => shift ? _editor.RedoCommand : _editor.UndoCommand,
            Key.Y when !shift => _editor.RedoCommand,
            _ => null,
        };
        if (cmd == null) return;
        if (cmd == _editor.SaveCommand) CommitPending();
        if (cmd.CanExecute(null)) cmd.Execute(null);
        e.Handled = true;
    }
}
