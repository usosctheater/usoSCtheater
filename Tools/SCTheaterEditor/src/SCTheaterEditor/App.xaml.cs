using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor;

/// <summary>
/// 시작 순서: 중복 실행 확인 → 전역 예외 처리 연결 → 설정 읽기 → 로그 파일 연결 → 백그라운드 정리 → 메인 창
/// </summary>
public partial class App : Application
{
    /// <summary>프로그램 전체 설정 (ViewModel에는 생성자로 넘김 — 뷰어 탭 코드만 MainWindow를 통해 사용)</summary>
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>스크립트 탭 무대(WebView2) 연결 — ScriptView가 WebView2를 붙이고, MainWindow가 단축키를 연결</summary>
    public static WpfStageHost Stage { get; } = new();

    /// <summary>배경 이미지 목록 (변경(5회차): 게임 Resources\BG — 설정을 읽은 뒤 만듦)</summary>
    public static BackgroundLibrary Backgrounds { get; private set; } = new(null);

    private const string MutexName = @"Local\SCTheaterEditor.SingleInstance";
    private Mutex? _singleInstance;
    private static DispatcherTimer? _saveTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 1) 중복 실행 방지: 이미 떠 있으면 그 창을 앞으로 가져오고 종료 (설정 동시 저장 충돌 방지)
        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (!created)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            ActivateExistingWindow();
            Shutdown();
            return;
        }

        // 2) 전역 예외 처리: 놓친 예외로 프로그램이 그대로 꺼지지 않도록 로그에 남기고 알림
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Shared.Error("백그라운드 작업 오류", args.Exception.GetBaseException());
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) AppLog.Shared.Error("복구할 수 없는 오류로 종료", ex);
        };

        base.OnStartup(e);

        // 3) 설정 (예전 SCSpineManager 설정 이전·손상 복구 결과는 로그로 알림)
        Settings = AppSettings.Load();
        Backgrounds = new BackgroundLibrary(Settings.Editor.BackgroundFolder);   // 추가(5회차)
        AppLog.Shared.SetFileFolder(Settings.MetaFolder);
        if (Settings.LoadNotice != null)
        {
            AppLog.Shared.Warn(Settings.LoadNotice);
            // 이전·복구한 설정은 바로 새 경로에 저장 (다음 실행 때 같은 이전을 반복하지 않도록)
            try { Settings.Save(); }
            catch (Exception ex) { AppLog.Shared.Error("설정 저장 실패", ex); }
        }

        // 4) 이전 빌드의 임시 폴더 정리 (시작을 늦추지 않도록 백그라운드)
        _ = Task.Run(() => Housekeeping.CleanExtractionFolders(AppSettings.AppName, AppLog.Shared));

        // 5) 공용 데이터와 화면 (변경: MVVM — App이 서비스를 만들어 ViewModel에 넘김)
        var manifest = SpineManifest.Load(Settings.MetaFolder);
        var library = new SpineLibrary(Settings.SaveFolder, manifest);
        library.Rebuild();
        var vm = new MainViewModel(
            Settings, library, Backgrounds, Stage, new WpfDialogService(), new WpfDispatcher(Dispatcher),
            catalogFactory: () => new SpineCatalogClient(Settings),
            downloaderFactory: () => new SpineDownloader(Settings, library.Manifest, AppLog.Shared),
            saveSettings: SaveSettingsSoon,
            messenger: CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default,
            log: AppLog.Shared,
            audioMemos: new AudioMemoStore(AudioMemoStore.DefaultPath));   // 추가(6회차): exe 옆 AudioMemo.xml

        var window = new MainWindow(vm);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FlushSettings();
        if (_singleInstance != null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    // ------------------------------------------------------------------ 설정 저장

    /// <summary>설정 저장 예약: 짧은 시간 안의 여러 변경을 모아 한 번에 저장 (종료 시에도 저장)</summary>
    public static void SaveSettingsSoon()
    {
        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) => FlushSettings();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private static void FlushSettings()
    {
        if (_saveTimer is not { IsEnabled: true }) return;
        _saveTimer.Stop();
        try { Settings.Save(); }
        catch (Exception ex) { AppLog.Shared.Error("설정 저장 실패", ex); }
    }

    // ------------------------------------------------------------------ 예외

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Shared.Error("처리하지 못한 오류", e.Exception);
        string text = $"예기치 않은 오류가 발생했습니다. 프로그램은 계속 실행됩니다.\n\n{e.Exception.Message}\n\n" +
                      $"자세한 내용: {System.IO.Path.Combine(Settings.MetaFolder, AppLog.FileName)}";
        if (MainWindow is { IsLoaded: true } owner)
            MessageBox.Show(owner, text, "SC Theater Editor", MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(text, "SC Theater Editor", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ 중복 실행

    private static void ActivateExistingWindow()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(current.ProcessName))
        {
            using (p)
            {
                if (p.Id == current.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, SwRestore);
                SetForegroundWindow(p.MainWindowHandle);
                return;
            }
        }
    }

    private const int SwRestore = 9;
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
}
