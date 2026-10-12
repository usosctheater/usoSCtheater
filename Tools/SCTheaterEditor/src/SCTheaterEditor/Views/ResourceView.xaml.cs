using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>더블클릭·Space → 스크립트 탭에 미리보기, Ctrl+Space → 바로 추가 (입력 처리만 View에서, 동작은 ViewModel 명령)</summary>
public partial class ResourceView : UserControl
{
    /// <summary>목록 항목이 구분선인지 (ItemContainerStyle 트리거용)</summary>
    public static IValueConverter IsSeparatorConverter { get; } = new SeparatorCheck();

    public ResourceView() => InitializeComponent();

    private ResourceViewModel? Vm => DataContext as ResourceViewModel;

    private void AssetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm?.OpenCommand.CanExecute(null) != true) return;
        e.Handled = true;
        Vm.OpenCommand.Execute(null);
    }

    /// <summary>Space → 스크립트 탭에 미리보기, Ctrl+Space → 바로 추가</summary>
    private void AssetList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Vm == null) return;
        var command = Keyboard.Modifiers switch
        {
            ModifierKeys.None => Vm.OpenCommand,
            ModifierKeys.Control => Vm.AddImmediatelyCommand,
            _ => null,
        };
        if (command?.CanExecute(null) != true) return;
        e.Handled = true;
        command.Execute(null);
    }

    /// <summary>탭으로 돌아왔을 때 (미리보기 Esc 등) 선택한 세트로 포커스 — 바로 위아래 키로 다른 세트를 고를 수 있게</summary>
    public void FocusSelection()
    {
        if (AssetList.SelectedItem != null)
        {
            AssetList.ScrollIntoView(AssetList.SelectedItem);
            AssetList.UpdateLayout();
            if (AssetList.ItemContainerGenerator.ContainerFromItem(AssetList.SelectedItem) is ListBoxItem item)
            {
                item.Focus();
                return;
            }
        }
        AssetList.Focus();
    }

    private sealed class SeparatorCheck : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is ResourceSeparator;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
