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
            try { await App.Stage.AttachAsync(StageWeb, App.Settings.SaveFolder, App.Backgrounds.Folder); }
            catch (Exception ex) { AppLog.Shared.Error("무대(WebView2)를 시작하지 못했습니다 — WebView2 런타임 설치 여부를 확인하세요", ex); }
        };
    }

    private ScriptViewModel? Vm => DataContext as ScriptViewModel;

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.OriginalSource is TextBox or ComboBox or ComboBoxItem) return;   // 글자 입력·드롭다운에서는 무시
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
        try
        {
            System.IO.Directory.CreateDirectory(App.Backgrounds.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{App.Backgrounds.Folder}\"") { UseShellExecute = true });
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
