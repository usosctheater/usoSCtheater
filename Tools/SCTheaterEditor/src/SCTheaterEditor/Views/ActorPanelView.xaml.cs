using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>
/// 추가(7회차): Spine 패널 — 선택 캐릭터 정보 · 트랙/애니메이션 목록 · [설정]. 동작은 ScriptViewModel.
/// (이전 ScriptView의 처리 코드를 그대로 옮김. 탭 전체 단축키는 ScriptView가 처리)
/// </summary>
public partial class ActorPanelView : UserControl
{
    public ActorPanelView() => InitializeComponent();

    private ScriptViewModel? Vm => DataContext as ScriptViewModel;

    /// <summary>애니메이션 목록 (ScriptView가 "목록 안의 Enter는 목록이 처리"를 판단할 때)</summary>
    internal ListBox AnimListBox => AnimList;

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
}
