using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>더블클릭·Space → 열기 (입력 처리만 View에서, 동작은 ViewModel 명령)</summary>
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

    private void AssetList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None) return;
        if (Vm?.OpenCommand.CanExecute(null) != true) return;
        e.Handled = true;
        Vm.OpenCommand.Execute(null);
    }

    private sealed class SeparatorCheck : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is ResourceSeparator;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
