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
    private Views.TranslationWindow? _translationWindow;   // 추가(번역 모드 3회차)

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        // 변경(번역 모드 3회차): 저장한 위치·크기·최대화 (처음 = 1920×1080 화면 가운데). 화면(작업 영역)보다 크면 화면에 맞춤
        // (이전) 기본 크기 1920×1080 고정 — if (Width > area.Width) Width = area.Width; if (Height > area.Height) Height = area.Height;
        var place = App.Settings.MainWindow;
        var area = SystemParameters.WorkArea;
        Width = Math.Min(place.Width, area.Width);
        Height = Math.Min(place.Height, area.Height);
        if (place.Left is { } l && place.Top is { } t && Services.WindowPlacement.IsOnScreen(l, t))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = l;
            Top = t;
        }
        if (place.Maximized) WindowState = WindowState.Maximized;
        LocationChanged += (_, _) => StorePlace();
        SizeChanged += (_, _) => StorePlace();
        StateChanged += (_, _) => StorePlace();

        LicenseBox.Text = LoadEmbeddedText("THIRD-PARTY-NOTICES.txt");

        // 탭 사이 메시지: 이 탭으로 (스크립트 탭 미리보기 → Esc → 리소스 탭 복귀 등)
        WeakReferenceMessenger.Default.Register<ShowTabMessage>(this, (_, m) => ShowTab(m.Tab));
        Closed += (_, _) => WeakReferenceMessenger.Default.UnregisterAll(this);

        // 추가(4-1): 저장 안 된 막이 있으면 닫기 전에 저장 / 저장 안 함 / 취소
        Closing += (_, e) =>
        {
            _translationWindow?.CommitPending();      // 추가(번역 모드 3회차): 번역 창에서 입력 중(Enter 전)인 칸도 변경으로 쳐서 물음
            Views.ActEditorView.CommitFocusedBox();   // 변경: 입력 중인 칸도 변경으로 쳐서 물음
            if (_translationWindow?.ConfirmCloseGuide() == false) { e.Cancel = true; return; }   // 추가(번역 모드 4회차): 저장 안 한 번역 지침
            if (!_vm.Editor.ConfirmDiscard()) e.Cancel = true;
            else _closingApp = true;   // 추가
        };

        // 저장된 목록은 창이 뜬 뒤 읽음 (매니페스트 v2 이전 포함)
        Loaded += (_, _) =>
        {
            _vm.Initialize();
            if (App.Settings.LogWindow.Open) ToggleLog();   // 추가: 지난번에 열어 둔 채 껐으면 다시 엶
            if (App.Settings.Translation.Open) ToggleTranslation();   // 추가(번역 모드 3회차): 막을 연 뒤에
        };
        _vm.Editor.TranslationToggleRequested += ToggleTranslation;   // 추가(번역 모드 3회차): 스크립트 표 [번역 창] 버튼
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
        // 추가(번역 모드 3회차): Ctrl+T 번역 창 (무대에 포커스가 있어도 — Ctrl 조합은 WPF 키 이벤트로 옴)
        if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleTranslation();
            e.Handled = true;
            return;
        }
        if (HandleTabKeys(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    /// <summary>추가(번역 모드 3회차): 메인 창 위치·크기 저장 (최소화 중에는 그대로, 최대화면 복원 크기 + 최대화 표시)</summary>
    private void StorePlace()
    {
        if (WindowState == WindowState.Minimized) return;
        var r = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        if (double.IsNaN(r.Left) || double.IsNaN(r.Top) || r.IsEmpty) return;   // 화면에 놓이기 전(위치 미정)
        var place = App.Settings.MainWindow;
        (place.Left, place.Top, place.Width, place.Height) = (r.Left, r.Top, r.Width, r.Height);
        place.Maximized = WindowState == WindowState.Maximized;
        App.SaveSettingsSoon();
    }

    /// <summary>추가(번역 모드 3회차): 번역 창 열기/닫기 (열 때마다 ViewModel을 새로 만들고 닫으면 Dispose — 닫혀 있을 때 비용 없음)</summary>
    private void ToggleTranslation()
    {
        if (_translationWindow != null)
        {
            _translationWindow.Close();
            return;
        }
        Views.ActEditorView.CommitFocusedBox();   // 표에서 입력 중이던 칸을 먼저 확정 (번역 창 왼쪽이 최신 원문)
        _translationWindow = new Views.TranslationWindow(_vm.CreateTranslation(), _vm.Editor, App.Settings.Translation) { Owner = this };
        _translationWindow.Closed += (_, _) =>
        {
            _translationWindow = null;
            if (_closingApp) return;
            App.Settings.Translation.Open = false;
            App.SaveSettingsSoon();
        };
        _translationWindow.Show();
        App.Settings.Translation.Open = true;
        App.SaveSettingsSoon();
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
            AppTab.Settings => SettingsTab,   // 추가
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
