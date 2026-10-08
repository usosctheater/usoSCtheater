using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SCSpineManager.Services;

namespace SCSpineManager;

/// <summary>목록 갱신 후 이전 목록과 달라졌을 때 띄우는 알림 창 (프로그램 안 창)</summary>
public sealed class ChangeNoticeWindow : Window
{
    public ChangeNoticeWindow(Window owner, CatalogDiff diff, DateTime previousAt)
    {
        Owner = owner;
        Title = "목록 변경 알림";
        Width = 760;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var header = new TextBlock
        {
            Text = $"이전 목록({previousAt:yyyy-MM-dd HH:mm})과 비교해 바뀐 점이 있습니다.\n{diff.Summary}",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        };
        var hint = new TextBlock
        {
            Text = "받지 않은 세트는 다운로드 목록 맨 위에 표시됩니다.",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var body = new TextBox
        {
            Text = diff.ToDetailText(),
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var ok = new Button { Content = "확인", Width = 90, Padding = new Thickness(0, 3, 0, 3), IsDefault = true, IsCancel = true,
                              HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        ok.Click += (_, _) => Close();

        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(ok, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(hint);
        root.Children.Add(ok);
        root.Children.Add(body);
        Content = root;
    }
}
