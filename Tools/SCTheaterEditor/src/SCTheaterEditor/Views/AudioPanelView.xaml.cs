using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>
/// 추가(7회차): 오디오 패널 — 목록 · 미리 듣기 · 메모 편집. 동작은 AudioListViewModel.
/// (이전 ScriptView의 처리 코드를 그대로 옮김. Ctrl+Enter 줄에 넣기는 탭 전체 단축키라 ScriptView가 처리)
/// </summary>
public partial class AudioPanelView : UserControl
{
    public AudioPanelView() => InitializeComponent();

    private AudioListViewModel? Vm => DataContext as AudioListViewModel;

    /// <summary>Space = 재생/정지 (목록 기본 동작인 선택 토글 대신). 메모 입력 중이면 그대로 글자</summary>
    private void AudioList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || Keyboard.Modifiers != ModifierKeys.None || Vm == null) return;
        if (e.Key == Key.Space)
        {
            Vm.TogglePlayCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>더블클릭 = 처음부터 재생 (메모 칸은 제외)</summary>
    private void AudioList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null || e.OriginalSource is not DependencyObject d || IsInsideType<TextBox>(d)) return;
        if (ItemsControl.ContainerFromElement(AudioList, d) is ListBoxItem { DataContext: AudioRow row })
        {
            Vm.PlayFromStart(row);
            e.Handled = true;
        }
    }

    /// <summary>메모 클릭 = 그 자리에서 편집</summary>
    private void Memo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AudioRow row } || Vm == null) return;
        Vm.SelectedItem = row;
        row.BeginEdit();
        e.Handled = true;
    }

    private void MemoBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && box.IsVisible)
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { box.Focus(); box.SelectAll(); });
    }

    /// <summary>Enter = 확정, Esc = 취소 → 목록 줄로 포커스</summary>
    private void MemoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: AudioRow row } || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Enter) row.CommitEdit();
        else if (e.Key == Key.Escape) row.CancelEdit();
        else return;
        e.Handled = true;
        (AudioList.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem)?.Focus();
    }

    /// <summary>다른 곳을 누르면 확정</summary>
    private void MemoBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: AudioRow row }) row.CommitEdit();
    }

    private static bool IsInsideType<T>(DependencyObject? d) where T : DependencyObject
    {
        for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T) return true;
        return false;
    }
}
