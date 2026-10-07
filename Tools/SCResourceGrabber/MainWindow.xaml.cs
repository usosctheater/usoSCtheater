using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using SCResourceGrabber.Models;
using SCResourceGrabber.Services;

namespace SCResourceGrabber;

/// <summary>창 종류: 게임 리소스 수집(시작 창) / Spine 뷰어 사이트 수집</summary>
public enum CollectorKind { Game, Spine }

public partial class MainWindow : Window
{
    private readonly CollectorKind _kind;
    private bool IsGame => _kind == CollectorKind.Game;
    private MainWindow? _spineWindow;

    private readonly AppSettings _settings = AppSettings.Shared;
    private readonly ObservableCollection<CapturedResource> _resources = new();
    private readonly ICollectionView _view;
    private readonly CategoryFilterBar _filter;
    private readonly HotkeyMap _hotkeys;
    private readonly Dictionary<int, ResourceDetailWindow> _detailWindows = new();
    private ResourceCapture? _capture;
    private PageAudioMonitor? _audio;
    private TrackingWindow? _tracking;
    private readonly Dictionary<string, CapturedResource> _byUrl = new();

    private static readonly string AudioLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCResourceGrabber", "audio_hook.log");
    private bool _busy;

    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCResourceGrabber", "Cache");

    /// <summary>필터에 표시할 분류 / 처음에 켜 둘 분류 (추적 창도 같은 값 사용)</summary>
    internal IReadOnlyList<ResourceCategory> FilterCategories { get; }
    internal IReadOnlyList<ResourceCategory> DefaultCategories { get; }

    // 게임은 Spine 데이터를 암호화해서 보내므로 게임 창에는 Spine 분류를 두지 않는다
    private static readonly ResourceCategory[] GameFilter =
        [ResourceCategory.Image, ResourceCategory.Audio, ResourceCategory.Video, ResourceCategory.Json,
         ResourceCategory.Font, ResourceCategory.Encrypted, ResourceCategory.Other];
    private static readonly ResourceCategory[] GameDefaults =
        [ResourceCategory.Image, ResourceCategory.Audio, ResourceCategory.Video];
    private static readonly ResourceCategory[] SpineDefaults =
        [ResourceCategory.Image, ResourceCategory.Spine];

    public MainWindow() : this(CollectorKind.Game) { }

    public MainWindow(CollectorKind kind)
    {
        _kind = kind;
        FilterCategories = IsGame ? GameFilter : Enum.GetValues<ResourceCategory>();
        DefaultCategories = IsGame ? GameDefaults : SpineDefaults;

        InitializeComponent();
        if (!IsGame)
        {
            Title = "SC Resource Grabber — Spine 수집";
            SpineButton.Visibility = Visibility.Collapsed;
        }

        _view = CollectionViewSource.GetDefaultView(_resources);
        _view.Filter = o => o is CapturedResource r && PassesFilter(r);
        ResourceList.ItemsSource = _view;
        ListViewSorter.Enable(ResourceList, _view, new Dictionary<string, string>
        {
            ["종류"] = nameof(CapturedResource.Category),
            ["크기"] = nameof(CapturedResource.Size),
            ["받은 시각"] = nameof(CapturedResource.CapturedAt),
        });

        _filter = new CategoryFilterBar(CategoryPanel, FilterCategories, DefaultCategories);
        _filter.Changed += RefreshView;

        SaveFolderBox.Text = SaveFolder;
        OverwriteCheck.IsChecked = _settings.OverwriteExisting;

        _hotkeys = new HotkeyMap(_settings.Hotkeys);
        ApplyHotkeyTooltips();

        Loaded += async (_, _) =>
        {
            await InitWebViewAsync();
            if (_hotkeys.Warnings.Count > 0) SetStatus("단축키 설정 확인: " + string.Join(" / ", _hotkeys.Warnings));
        };
        Closing += (_, _) => OnClosingCleanup();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // =====================================================================
    // 단축키 (설정: settings.json "Hotkeys")
    // =====================================================================

    internal HotkeyMap Hotkeys => _hotkeys;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { WebView.CoreWebView2?.Reload(); e.Handled = true; return; }

        // 게임 화면(WebView2)에 포커스가 있어도 이 이벤트로 들어온다 → 처리하면 브라우저 기본 동작(Ctrl+R 새로고침 등)은 막힘
        switch (_hotkeys.Resolve(e))
        {
            case HotkeyMap.SaveChecked: SaveChecked_Click(this, e); break;
            case HotkeyMap.ToggleTracking: ToggleTracking(); break;
            case HotkeyMap.ToggleAllChecks: ToggleAll_Click(this, e); break;
            case HotkeyMap.ClearList: Clear_Click(this, e); break;
            default: return;
        }
        e.Handled = true;
    }

    private void ApplyHotkeyTooltips()
    {
        static string Tip(string text, string? key) => key == null ? text : $"{text} ({key})";
        SaveCheckedButton.ToolTip = Tip("체크한 항목 저장", _hotkeys.GestureText(HotkeyMap.SaveChecked));
        TrackingButton.ToolTip = Tip("추적 창 열기/닫기 — 이후 새로 받은 리소스와 재생된 오디오를 따로 모아 봅니다",
                                     _hotkeys.GestureText(HotkeyMap.ToggleTracking));
        ToggleAllButton.ToolTip = Tip("보이는 항목 전체 체크/해제", _hotkeys.GestureText(HotkeyMap.ToggleAllChecks));
        ClearButton.ToolTip = Tip("목록 비우기", _hotkeys.GestureText(HotkeyMap.ClearList));
    }

    // =====================================================================
    // WebView2 초기화 / 캡처
    // =====================================================================

    private async Task InitWebViewAsync()
    {
        try
        {
            if (IsGame) CleanupOldCaches(); // Spine 창이 지우면 게임 창의 캐시까지 지워짐
            Directory.CreateDirectory(_settings.ProfileFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, _settings.ProfileFolder);
            await WebView.EnsureCoreWebView2Async(env);

            var core = WebView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = true;
            core.SourceChanged += (_, _) => AddressBox.Text = core.Source;

            string sessionCache = Path.Combine(CacheRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + _kind);
            _capture = new ResourceCapture(core, _settings, sessionCache);
            _capture.Captured += OnCaptured;
            _capture.Log += msg => Debug.WriteLine("[Capture] " + msg);

            // 오디오 재생 추적 (게임 창만. 페이지 생성 시점에 스크립트 주입 → 탐색 전에 설정해야 함)
            if (IsGame)
            {
                _audio = new PageAudioMonitor(core);
                _audio.Played += (id, url, loop) => _tracking?.OnPlayed(id, url, loop);
                _audio.Ended += id => _tracking?.OnEnded(id);
                _audio.PageReset += () => _tracking?.ResetPlaying();
                _audio.Log += WriteAudioLog;
                try { File.WriteAllText(AudioLogPath, $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} 시작\n"); } catch { }
                await _audio.InitializeAsync();
            }

            core.Navigate(StartUrl);
            SetStatus(IsGame
                ? "준비됨. 처음 실행이면 게임 화면에서 로그인해 주세요 (이후 자동 유지)."
                : "준비됨. 뷰어에서 캐릭터를 고르면 atlas·json·텍스처가 목록에 쌓입니다. 저장 시 URL 폴더 구조를 유지합니다.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "WebView2 초기화 실패:\n" + ex.Message, "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCaptured(CapturedResource res)
    {
        res.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CapturedResource.IsChecked)) UpdateStatus();
        };
        _resources.Add(res);
        _byUrl[res.Url] = res;
        _tracking?.OnLoaded(res);
        UpdateCategoryCounts();
        UpdateStatus();
    }

    private void CaptureToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_capture != null) _capture.IsEnabled = CaptureToggle.IsChecked == true;
    }

    private void OnClosingCleanup()
    {
        _settings.Save();
        _spineWindow?.Close();   // Spine 창도 정리(캐시 삭제)되도록 먼저 닫음
        _tracking?.Close();
        CloseAllDetailWindows(); // 재생 중인 캐시 파일 잠금 해제 후 캐시 삭제
        try
        {
            if (_capture != null && Directory.Exists(_capture.CacheFolder))
                Directory.Delete(_capture.CacheFolder, recursive: true);
        }
        catch { /* 다음 실행 때 정리 */ }
    }

    /// <summary>비정상 종료 등으로 남은 이전 세션 캐시 삭제.</summary>
    private static void CleanupOldCaches()
    {
        try
        {
            if (!Directory.Exists(CacheRoot)) return;
            foreach (var dir in Directory.GetDirectories(CacheRoot))
                try { Directory.Delete(dir, true); } catch { }
        }
        catch { }
    }

    // =====================================================================
    // 브라우저 조작
    // =====================================================================

    private void Back_Click(object sender, RoutedEventArgs e) { if (WebView.CanGoBack) WebView.GoBack(); }
    private void Reload_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.Reload();
    private void Home_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.Navigate(StartUrl);
    private void DevTools_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.OpenDevToolsWindow();
    private void Go_Click(object sender, RoutedEventArgs e) => NavigateToAddress();
    private void AddressBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) NavigateToAddress(); }

    private void NavigateToAddress()
    {
        string url = AddressBox.Text.Trim();
        if (url.Length == 0 || WebView.CoreWebView2 == null) return;
        if (!url.Contains("://")) url = "https://" + url;
        try { WebView.CoreWebView2.Navigate(url); } catch (Exception ex) { SetStatus("이동 실패: " + ex.Message); }
    }

    // =====================================================================
    // 필터
    // =====================================================================

    private void UpdateCategoryCounts() =>
        _filter.UpdateCounts(_resources.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count()));

    private bool PassesFilter(CapturedResource r) => _filter.Allows(r.Category);

    private void RefreshView()
    {
        _view.Refresh();
        UpdateStatus();
    }

    private IEnumerable<CapturedResource> VisibleItems => _view.Cast<CapturedResource>();

    private void ToggleAll_Click(object sender, RoutedEventArgs e)
    {
        var visible = VisibleItems.ToList();
        bool check = visible.Any(r => !r.IsChecked);
        foreach (var r in visible) r.IsChecked = check;
        UpdateStatus();
    }

    private void ResourceList_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter: 선택된 행의 상세 창 열기
        if (e.Key == Key.Enter && ResourceList.SelectedItem is CapturedResource cur)
        {
            OpenDetail(cur);
            e.Handled = true;
            return;
        }

        // 스페이스: 선택(하이라이트)된 행들의 체크 토글
        if (e.Key != Key.Space) return;
        var sel = ResourceList.SelectedItems.Cast<CapturedResource>().ToList();
        if (sel.Count == 0) return;
        bool check = sel.Any(r => !r.IsChecked);
        foreach (var r in sel) r.IsChecked = check;
        e.Handled = true;
        UpdateStatus();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        CloseAllDetailWindows();
        foreach (var r in _resources)
            try { File.Delete(r.CachePath); } catch { }
        _resources.Clear();
        _byUrl.Clear();
        _tracking?.DetachResources();
        _capture?.ResetSeen();
        UpdateCategoryCounts();
        UpdateStatus();
    }

    // =====================================================================
    // 창 종류별 값 / Spine 수집 창
    // =====================================================================

    private string StartUrl => IsGame ? _settings.StartUrl : _settings.SpineStartUrl;

    private string SaveFolder
    {
        get => IsGame ? _settings.SaveFolder : _settings.SpineSaveFolder;
        set { if (IsGame) _settings.SaveFolder = value; else _settings.SpineSaveFolder = value; }
    }

    /// <summary>Spine 창은 data.json·data.atlas처럼 이름이 겹치므로 URL 폴더 구조를 유지해서 저장</summary>
    private bool KeepUrlPath => !IsGame;

    private void Spine_Click(object sender, RoutedEventArgs e)
    {
        if (_spineWindow != null)
        {
            if (_spineWindow.WindowState == WindowState.Minimized) _spineWindow.WindowState = WindowState.Normal;
            _spineWindow.Activate();
            return;
        }
        _spineWindow = new MainWindow(CollectorKind.Spine);
        _spineWindow.Closed += (_, _) => _spineWindow = null;
        _spineWindow.Show();
    }

    // =====================================================================
    // 추적 모드
    // =====================================================================

    private void Tracking_Click(object sender, RoutedEventArgs e)
    {
        if (_tracking != null)
        {
            if (_tracking.WindowState == WindowState.Minimized) _tracking.WindowState = WindowState.Normal;
            _tracking.Activate();
            return;
        }
        OpenTracking();
    }

    /// <summary>단축키: 추적 창이 없으면 열고, 있으면 닫는다(추적 종료).</summary>
    internal void ToggleTracking()
    {
        if (_tracking != null) _tracking.Close();
        else OpenTracking();
    }

    private void OpenTracking()
    {
        _tracking = new TrackingWindow(
            this,
            url => _byUrl.GetValueOrDefault(url),
            OpenDetail,
            SaveManyAsync);
        _tracking.Closed += (_, _) =>
        {
            _tracking = null;
            TrackingButton.Content = "● 추적 모드";
            TrackingButton.FontWeight = FontWeights.Normal;
            Activate();
        };
        TrackingButton.Content = "● 추적 중";
        TrackingButton.FontWeight = FontWeights.Bold;
        _tracking.Show();
    }

    /// <summary>오디오 훅 진단 로그 (%LocalAppData%\SCResourceGrabber\audio_hook.log, 실행마다 새로 씀)</summary>
    private static void WriteAudioLog(string msg)
    {
        try { File.AppendAllText(AudioLogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); } catch { }
    }

    // =====================================================================
    // 리소스 상세 창
    // =====================================================================

    private void ResourceItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: CapturedResource res })
        {
            // 체크박스를 빠르게 두 번 클릭한 경우는 제외
            if (e.OriginalSource is DependencyObject d && FindParent<CheckBox>(d) != null) return;
            OpenDetail(res);
            e.Handled = true;
        }
    }

    /// <summary>리소스마다 창 1개. 이미 열려 있으면 앞으로 가져온다.</summary>
    private void OpenDetail(CapturedResource res)
    {
        if (_detailWindows.TryGetValue(res.Id, out var opened))
        {
            if (opened.WindowState == WindowState.Minimized) opened.WindowState = WindowState.Normal;
            opened.Activate();
            return;
        }
        var win = new ResourceDetailWindow(res, r => _ = SaveManyAsync([r]), _hotkeys);
        win.Closed += (_, _) => _detailWindows.Remove(res.Id);
        _detailWindows[res.Id] = win;
        win.Show();
    }

    private void CloseAllDetailWindows()
    {
        foreach (var w in _detailWindows.Values.ToList()) w.Close();
        _detailWindows.Clear();
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null && d is not T)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    // =====================================================================
    // 저장
    // =====================================================================

    private void SaveFolderBox_LostFocus(object sender, RoutedEventArgs e) => ApplySaveOptions();
    private void SaveOption_Changed(object sender, RoutedEventArgs e) => ApplySaveOptions();

    private void ApplySaveOptions()
    {
        SaveFolder = SaveFolderBox.Text.Trim();
        _settings.OverwriteExisting = OverwriteCheck.IsChecked == true;
        _settings.Save();
    }

    private void BrowseSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "저장 폴더 선택" };
        if (Directory.Exists(SaveFolderBox.Text)) dlg.InitialDirectory = SaveFolderBox.Text;
        if (dlg.ShowDialog(this) == true)
        {
            SaveFolderBox.Text = dlg.FolderName;
            ApplySaveOptions();
        }
    }

    private void OpenSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        ApplySaveOptions();
        Directory.CreateDirectory(SaveFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SaveFolder}\"") { UseShellExecute = true });
    }

    private async void SaveChecked_Click(object sender, RoutedEventArgs e) =>
        await SaveManyAsync(_resources.Where(r => r.IsChecked).ToList());

    private async Task SaveManyAsync(List<CapturedResource> list)
    {
        if (_busy || list.Count == 0) return;
        ApplySaveOptions();
        if (string.IsNullOrWhiteSpace(SaveFolder))
        {
            SetStatus("저장 폴더를 지정해 주세요.");
            return;
        }

        _busy = true;
        int saved = 0, skipped = 0, failed = 0;
        string? lastError = null;
        string folder = SaveFolder;
        bool keepPath = KeepUrlPath, overwrite = _settings.OverwriteExisting;

        foreach (var r in list)
        {
            var (outcome, _, error) = await Task.Run(() => ResourceSaver.Save(r, folder, keepPath, overwrite));
            switch (outcome)
            {
                case ResourceSaver.Outcome.Saved: saved++; r.Status = "저장됨"; break;
                case ResourceSaver.Outcome.Skipped: skipped++; r.Status = "중복"; break;
                default: failed++; r.Status = "실패"; lastError = error; break;
            }
            SetStatus($"저장 중 {saved + skipped + failed}/{list.Count}");
        }

        _busy = false;
        SetStatus($"저장 완료: {saved}개 저장, {skipped}개 동일 파일 건너뜀, {failed}개 실패" +
                  (lastError != null ? $" (마지막 오류: {lastError})" : ""));
    }

    // =====================================================================
    // 상태 표시
    // =====================================================================

    private void UpdateStatus()
    {
        if (_busy) return;
        int visible = VisibleItems.Count();
        int checkedCount = _resources.Count(r => r.IsChecked);
        SetStatus($"캡처 {_resources.Count}개 · 표시 {visible}개 · 체크 {checkedCount}개");
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
