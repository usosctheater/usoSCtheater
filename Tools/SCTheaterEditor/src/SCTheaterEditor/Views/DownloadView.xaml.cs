using System.Windows.Controls;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Views;

public partial class DownloadView : UserControl
{
    public DownloadView() => InitializeComponent();

    /// <summary>추가: DataGrid의 선택 행(여러 개)은 바인딩할 수 없어 여기서 ViewModel에 넘김 → [선택한 의상만 받기]</summary>
    private void Rows_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DownloadViewModel vm && sender is DataGrid grid)
            vm.SelectedRows = grid.SelectedItems.OfType<SetRowViewModel>().ToList();
    }
}
