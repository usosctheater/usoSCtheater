using System.Windows.Controls;
using System.Windows.Input;

namespace SCTheaterEditor.Views;

/// <summary>추가: 설정 탭 화면 — 숫자 칸 Enter 확정만 (동작은 SettingsViewModel)</summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void Number_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        box.SelectAll();
        e.Handled = true;
    }
}
