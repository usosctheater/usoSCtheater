using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor;

/// <summary>
/// 메인 창: 탭 틀 + 탭 이동 단축키 + 라이선스 탭. 각 탭 화면은 Views/, 동작은 Core의 ViewModels/.
/// 탭 이동 (E-001 수정, 2026-10-12)
/// - Ctrl+PageUp/PageDown: Window_PreviewKeyDown (무대 WebView2에 포커스가 있어도 WPF WebView2 컨트롤이 Ctrl 조합을 WPF 키 이벤트로 넘겨 줌)
/// - 원인: 탭을 바꿔도 숨겨진 WebView2가 키보드 포커스(Win32)를 쥔 채 남아, 다음 키가 화면에 없는 컨트롤로 가서 사라짐
///   → 탭을 바꾸면 새 탭 내용으로 키보드 포커스를 옮김
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private Views.LogWindow? _logWindow;   // 추가
    private bool _closingApp;              // 추가: 프로그램을 끄면서 로그 창이 같이 닫힐 때는 '열림' 상태를 유지

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        // 변경: 기본 크기 1920×1080 (에디터 작업용). 화면(작업 영역)보다 크면 화면에 맞춤
        var area = SystemParameters.WorkArea;
        if (Width > area.Width) Width = area.Width;
        if (Height > area.Height) Height = area.Height;

        LicenseBox.Text = LoadEmbeddedText("THIRD-PARTY-NOTICES.txt");

        // 탭 사이 메시지: 이 탭으로 (스크립트 탭 미리보기 → Esc → 리소스 탭 복귀 등)
        WeakReferenceMessenger.Default.Register<ShowTabMessage>(this, (_, m) => ShowTab(m.Tab));
        Closed += (_, _) => WeakReferenceMessenger.Default.UnregisterAll(this);

        // 추가(4-1): 저장 안 된 막이 있으면 닫기 전에 저장 / 저장 안 함 / 취소
        Closing += (_, e) =>
        {
            Views.ActEditorView.CommitFocusedBox();   // 변경: 입력 중인 칸도 변경으로 쳐서 물음
            if (!_vm.Editor.ConfirmDiscard()) e.Cancel = true;
            else _closingApp = true;   // 추가
        };

        // 저장된 목록은 창이 뜬 뒤 읽음 (매니페스트 v2 이전 포함)
        Loaded += (_, _) =>
        {
            _vm.Initialize();
            if (App.Settings.LogWindow.Open) ToggleLog();   // 추가: 지난번에 열어 둔 채 껐으면 다시 엶
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 추가: Ctrl+L 로그 창
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleLog();
            e.Handled = true;
            return;
        }
        if (HandleTabKeys(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    private void LogButton_Click(object sender, RoutedEventArgs e) => ToggleLog();   // 추가

    /// <summary>추가: 로그 창 열기/닫기 (닫을 때마다 새로 만듦 — 로그 내용은 LogViewModel에 남아 있음)</summary>
    private void ToggleLog()
    {
        if (_logWindow != null)
        {
            _logWindow.Close();
            return;
        }
        _logWindow = new Views.LogWindow(_vm.Log, App.Settings.LogWindow) { Owner = this };
        _logWindow.Closed += (_, _) =>
        {
            _logWindow = null;
            if (_closingApp) return;
            App.Settings.LogWindow.Open = false;
            App.SaveSettingsSoon();
        };
        _logWindow.Show();
        App.Settings.LogWindow.Open = true;
        App.SaveSettingsSoon();
    }

    /// <summary>Ctrl+PageUp / Ctrl+PageDown → 이전·다음 탭 (끝에서는 반대쪽 끝으로). 처리했으면 true</summary>
    private bool HandleTabKeys(Key key, ModifierKeys mods)
    {
        if (mods != ModifierKeys.Control) return false;
        int step = key switch
        {
            Key.PageDown => 1,
            Key.PageUp => -1,
            _ => 0,
        };
        if (step == 0) return false;
        int count = MainTabs.Items.Count;
        MainTabs.SelectedIndex = (MainTabs.SelectedIndex + step + count) % count;
        return true;
    }

    private void ShowTab(AppTab tab)
    {
        MainTabs.SelectedItem = tab switch
        {
            AppTab.Download => DownloadTab,
            AppTab.Resource => ResourceTab,
            AppTab.Script => ScriptTab,
            _ => LicenseTab,
        };
    }

    /// <summary>탭을 바꾸면 새 탭 내용으로 포커스 이동 (리소스 탭은 선택한 세트로)</summary>
    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabs) return;   // 안쪽 목록의 선택 변경이 올라온 것은 무시
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (MainTabs.SelectedItem == ResourceTab) ResourcePage.FocusSelection();
            else if (MainTabs.SelectedContent is IInputElement content)
            {
                Keyboard.Focus(content);
                if (content is UIElement ui && !ui.IsKeyboardFocusWithin) ui.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }
        });
    }

    private static string LoadEmbeddedText(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
