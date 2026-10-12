using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor;

/// <summary>
/// 메인 창 (변경 2026-10-12: 다운로드·리소스 탭은 MVVM — Views/, Core의 ViewModels/).
/// 여기에 남은 것: 탭 이동 단축키, 라이선스 탭, 뷰어 탭(MainWindow.Viewer.cs — 3회차에 스크립트 탭으로 전환 예정)
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _settings;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        _settings = vm.Settings;
        InitializeComponent();
        DataContext = vm;

        LicenseBox.Text = LoadEmbeddedText("THIRD-PARTY-NOTICES.txt");

        // 리소스 탭 → 뷰어에서 열기 (탭 사이 메시지)
        WeakReferenceMessenger.Default.Register<OpenSpineMessage>(this, (_, m) => OpenInViewer(m.Asset));
        Closed += (_, _) => WeakReferenceMessenger.Default.UnregisterAll(this);

        // 저장된 목록은 창이 뜬 뒤 읽음 (매니페스트 v2 이전 포함)
        Loaded += (_, _) => _vm.Initialize();
    }

    // Ctrl+PageUp / Ctrl+PageDown → 이전·다음 탭 (끝에서는 반대쪽 끝으로)
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        int step = e.Key switch
        {
            Key.PageDown => 1,
            Key.PageUp => -1,
            _ => 0,
        };
        if (step == 0) return;
        int count = MainTabs.Items.Count;
        MainTabs.SelectedIndex = (MainTabs.SelectedIndex + step + count) % count;
        e.Handled = true;
    }

    private static void Log(string message) => AppLog.Shared.Info(message);
    private static void LogWarn(string message) => AppLog.Shared.Warn(message);
    private static void LogError(string message, Exception ex) => AppLog.Shared.Error(message, ex);

    private static string LoadEmbeddedText(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
