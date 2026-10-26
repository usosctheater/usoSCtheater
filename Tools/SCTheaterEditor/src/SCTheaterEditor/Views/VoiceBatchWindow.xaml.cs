using System.Windows;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

/// <summary>추가(9회차): TEXT에 보이스 일괄 추가 미리보기 창 (처리는 VoiceBatchViewModel · ActEditorViewModel)</summary>
public partial class VoiceBatchWindow : Window
{
    public VoiceBatchWindow(Window? owner, VoiceBatchViewModel batch)
    {
        InitializeComponent();
        if (owner != null) Owner = owner;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DataContext = batch;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
