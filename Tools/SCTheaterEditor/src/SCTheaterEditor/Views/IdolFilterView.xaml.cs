using System.Windows;
using System.Windows.Controls;

namespace SCTheaterEditor.Views;

public partial class IdolFilterView : UserControl
{
    public IdolFilterView() => InitializeComponent();

    /// <summary>추가(리소스 탭 LD·SD): 'n/33명 선택' 오른쪽에 넣을 칸 (탭마다 다름, 비우면 없음)</summary>
    public static readonly DependencyProperty ExtraProperty =
        DependencyProperty.Register(nameof(Extra), typeof(object), typeof(IdolFilterView),
            new PropertyMetadata(null, (d, e) => ((IdolFilterView)d).ExtraHost.Content = e.NewValue));

    public object? Extra
    {
        get => GetValue(ExtraProperty);
        set => SetValue(ExtraProperty, value);
    }
}
