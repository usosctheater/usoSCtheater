using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SCTheaterEditor.Script;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>
/// 스크립트 표 화면 (변경: 종류별 팔레트 행). 선택 동기화·칸 이동·제목 줄 폭 조절만 여기서, 동작은 ActEditorViewModel.
/// (Ctrl+S·O·Z·Y는 스크립트 탭 전체에서 동작하도록 ScriptView가 처리)
/// </summary>
public partial class ActEditorView : UserControl
{
    private bool _selecting;   // ViewModel이 요청한 선택을 반영하는 중

    public ActEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ActEditorViewModel old) old.SelectionRequested -= SelectRows;
            if (e.NewValue is ActEditorViewModel vm) vm.SelectionRequested += SelectRows;
        };
        // 화면이 (다시) 보일 때 ViewModel이 기억한 선택을 적용
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (Vm is { Selection.Count: > 0 } vm) SelectRows(vm.Selection);
        });
        // 칸(입력 상자)에 포커스가 가면 그 줄을 선택 (Ctrl·Shift를 누르고 있으면 기존 선택 유지)
        LineList.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(LineList_GotKeyboardFocus), true);
    }

    private ActEditorViewModel? Vm => DataContext as ActEditorViewModel;

    private void LineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selecting || Vm == null) return;
        Vm.SetSelection(LineList.SelectedItems.OfType<ScriptRow>().ToList());
    }

    private void LineList_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is not DependencyObject d || FindAncestor<ListBoxItem>(d) is not { } item || item.IsSelected) return;
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
        LineList.SelectedItem = item.DataContext;
    }

    /// <summary>ViewModel 요청: 이 행들 선택 + 첫 행이 보이게</summary>
    private void SelectRows(IReadOnlyList<ScriptRow> rows)
    {
        _selecting = true;
        try
        {
            LineList.SelectedItems.Clear();
            foreach (var r in rows) LineList.SelectedItems.Add(r);
            if (rows.Count > 0) LineList.ScrollIntoView(rows[0]);
        }
        finally { _selecting = false; }
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        // Alt+↑/↓: 줄 이동 (칸 입력 중이면 먼저 확정)
        if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key == Key.System && e.SystemKey is Key.Up or Key.Down)
        {
            CommitFocusedBox();
            var cmd = e.SystemKey == Key.Up ? Vm.MoveUpCommand : Vm.MoveDownCommand;
            if (cmd.CanExecute(null)) cmd.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.OriginalSource is TextBox or ComboBox or ComboBoxItem) return;   // 칸·제목 입력, 종류 드롭다운에서는 그쪽 동작 우선
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete && LineList.IsKeyboardFocusWithin)
        {
            if (Vm.DeleteRowsCommand.CanExecute(null)) Vm.DeleteRowsCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>칸 입력 상자: Enter 확정 · Esc 취소 · ↑/↓ 위아래 줄의 같은 칸으로. Ctrl+↑/↓는 표 어디서나 맨 위/맨 아래 줄</summary>
    private void LineList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 변경: Ctrl+↑/↓ = 맨 위/맨 아래 줄로 (기본 동작인 '선택 없이 포커스만 이동' 대신). 칸에 있었으면 같은 칸으로
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Up or Key.Down && Vm is { Rows.Count: > 0 } vm)
        {
            int index = e.Key == Key.Up ? 0 : vm.Rows.Count - 1;
            if (e.OriginalSource is TextBox { Tag: string a } b)
            {
                b.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                FocusCell(index, a);
            }
            else
            {
                CommitFocusedBox();
                LineList.SelectedItem = vm.Rows[index];
                LineList.ScrollIntoView(vm.Rows[index]);
                LineList.UpdateLayout();
                (LineList.ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem)?.Focus();
            }
            e.Handled = true;
            return;
        }
        if (e.OriginalSource is not TextBox { Tag: string attr } box || Keyboard.Modifiers != ModifierKeys.None) return;
        switch (e.Key)
        {
            case Key.Enter:
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                box.SelectAll();
                e.Handled = true;
                break;
            case Key.Escape:
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                box.SelectAll();
                e.Handled = true;
                break;
            case Key.Up:
            case Key.Down:
                if (FindAncestor<ListBoxItem>(box)?.DataContext is ScriptRow row && Vm != null)
                {
                    int index = Vm.Rows.IndexOf(row) + (e.Key == Key.Up ? -1 : 1);
                    if (index >= 0 && index < Vm.Rows.Count)
                    {
                        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                        FocusCell(index, attr);
                    }
                }
                e.Handled = true;
                break;
        }
    }

    /// <summary>index번째 줄의 같은 속성 칸으로 (그 줄 종류에 없는 칸이면 줄만 선택)</summary>
    private void FocusCell(int index, string attr)
    {
        var row = Vm!.Rows[index];
        LineList.ScrollIntoView(row);
        LineList.UpdateLayout();
        if (LineList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem item) return;
        var target = FindDescendants<TextBox>(item).FirstOrDefault(t => t.Tag as string == attr);
        if (target != null)
        {
            target.Focus();
            target.SelectAll();
        }
        else
        {
            LineList.SelectedItem = row;
            item.Focus();
        }
    }

    /// <summary>제목 줄 경계선 드래그 → 그 종류의 칸 폭 (모든 줄이 같은 정의를 보므로 함께 바뀜)</summary>
    private void HeaderThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FieldDef f) f.Width += e.HorizontalChange;
    }

    private void HeaderThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FieldDef f) Vm?.SaveFieldWidth(f);
    }

    /// <summary>제목·부제 입력: Enter로 확정</summary>
    private void Commit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    /// <summary>입력 중인 칸이 있으면 확정 (저장·줄 이동 전)</summary>
    public static void CommitFocusedBox() =>
        (Keyboard.FocusedElement as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var c in FindDescendants<T>(child)) yield return c;
        }
    }
}
