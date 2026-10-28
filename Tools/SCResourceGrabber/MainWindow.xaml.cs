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

// Spine 수집 창은 2026-10-28 삭제 (Spine 관리는 TE로 이관)
public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Shared;
    private readonly ObservableCollection<CapturedResource> _resources = new();
    private readonly ICollectionView _view;
    private readonly CategoryFilterBar _filter;
    private readonly HotkeyMap _hotkeys;
    private readonly Dictionary<int, ResourceDetailWindow> _detailWindows = new();
    private ResourceCapture? _capture;
    private PageAudioMonitor? _audio;
    private TrackingWindow? _tracking;
    private DownloadWindow? _download;   // 다운로드 창 (2026-10-28, 창마다 1개)
    private readonly Dictionary<string, CapturedResource> _byUrl = new();

    private static readonly string AudioLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCResourceGrabber", "audio_hook.log");
    private bool _busy;

    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCResourceGrabber", "Cache");

    /// <summary>필터에 표시할 분류 / 처음에 켜 둘 분류 (추적 창도 같은 값 사용)</summary>
    internal IReadOnlyList<ResourceCategory> FilterCategories { get; }
    internal IReadOnlyList<ResourceCategory> DefaultCategories { get; }

    private static readonly ResourceCategory[] Defaults =
        [ResourceCategory.Image, ResourceCategory.Audio, ResourceCategory.Video];

    public MainWindow()
    {
        FilterCategories = Enum.GetValues<ResourceCategory>();
        DefaultCategories = Defaults;

        InitializeComponent();

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
        ShowMute();

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
            case HotkeyMap.ClearList: Clear_Click(this, e); break;
            case HotkeyMap.SendToDownload: SendDownload_Click(this, e); break;
            case HotkeyMap.ToggleMute: ToggleMute(); break;
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
        CheckSelectedButton.ToolTip = CheckSelectedTip;
        ClearButton.ToolTip = Tip("목록 비우기", _hotkeys.GestureText(HotkeyMap.ClearList));
        SendDownloadButton.ToolTip = Tip("체크한 항목을 다운로드 창으로 보내 이름을 붙여 저장", _hotkeys.GestureText(HotkeyMap.SendToDownload));
    }

    // =====================================================================
    // WebView2 초기화 / 캡처
    // =====================================================================

    private async Task InitWebViewAsync()
    {
        try
        {
            CleanupOldCaches();
            Directory.CreateDirectory(_settings.ProfileFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, _settings.ProfileFolder);
            await WebView.EnsureCoreWebView2Async(env);

            var core = WebView.CoreWebView2;
            core.IsMuted = _settings.Muted;
            ShowMute();
            core.Settings.AreDevToolsEnabled = true;
            core.SourceChanged += (_, _) => AddressBox.Text = core.Source;

            string sessionCache = Path.Combine(CacheRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            _capture = new ResourceCapture(core, _settings, sessionCache);
            _capture.Captured += OnCaptured;
            _capture.Log += msg => Debug.WriteLine("[Capture] " + msg);

            // 오디오 재생 추적 (페이지 생성 시점에 스크립트 주입 → 탐색 전에 설정해야 함)
            _audio = new PageAudioMonitor(core);
            _audio.Played += (id, url, loop) => _tracking?.OnPlayed(id, url, loop);
            _audio.Ended += id => _tracking?.OnEnded(id);
            _audio.PageReset += () => _tracking?.ResetPlaying();
            _audio.Log += WriteAudioLog;
            try { File.WriteAllText(AudioLogPath, $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} 시작\n"); } catch { }
            await _audio.InitializeAsync();

            core.Navigate(_settings.StartUrl);
            SetStatus("준비됨. 처음 실행이면 게임 화면에서 로그인해 주세요 (이후 자동 유지).");
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
        _tracking?.Close();
        _download?.Close();
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
    private void Home_Click(object sender, RoutedEventArgs e) => WebView.CoreWebView2?.Navigate(_settings.StartUrl);
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

    // ---------- 음소거 (2026-10-28: 게임 화면 소리만, 상태 기억) ----------

    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();

    /// <summary>게임 소리 끄기/켜기 (추적 창 단축키에서도 호출). 출력만 끄므로 추적 창의 재생 기록은 그대로 동작</summary>
    internal void ToggleMute()
    {
        _settings.Muted = !_settings.Muted;
        if (WebView.CoreWebView2 != null) WebView.CoreWebView2.IsMuted = _settings.Muted;
        _settings.Save();
        ShowMute();
    }

    private void ShowMute()
    {
        MuteButton.Content = _settings.Muted ? "🔇 음소거" : "🔊 소리";
        MuteButton.FontWeight = _settings.Muted ? FontWeights.Bold : FontWeights.Normal;
        string? key = _hotkeys.GestureText(HotkeyMap.ToggleMute);
        MuteButton.ToolTip = "게임 소리 끄기/켜기" + (key != null ? $" ({key})" : "") + " — 추적 창의 재생 기록은 그대로 동작";
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

    /// <summary>[선택 항목 체크] 버튼 툴팁 (추적 창도 사용)</summary>
    internal const string CheckSelectedTip =
        "목록에서 선택한(파란색) 항목을 한꺼번에 체크/해제 (목록에서 Space). Shift·Ctrl+클릭으로 여러 개 선택, Ctrl+A 전체 선택";

    // Preview: 목록 기본 Space 동작(선택을 한 줄로 바꿈)·체크박스 단일 토글보다 먼저 처리
    private void ResourceList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter: 선택된 행의 상세 창 열기
        if (e.Key == Key.Enter && ResourceList.SelectedItem is CapturedResource cur)
        {
            OpenDetail(cur);
            e.Handled = true;
            return;
        }

        // Space: 선택(하이라이트)된 행들의 체크 토글
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        {
            CheckSelected();
            e.Handled = true;
        }
    }

    private void CheckSelected_Click(object sender, RoutedEventArgs e) => CheckSelected();

    /// <summary>선택한 항목 중 하나라도 체크가 안 돼 있으면 모두 체크, 다 체크돼 있으면 모두 해제</summary>
    private void CheckSelected()
    {
        var sel = ResourceList.SelectedItems.Cast<CapturedResource>().ToList();
        if (sel.Count == 0) { SetStatus("체크할 항목을 목록에서 먼저 선택해 주세요."); return; }
        bool check = sel.Any(r => !r.IsChecked);
        foreach (var r in sel) r.IsChecked = check;
        UpdateStatus();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        CloseAllDetailWindows();
        // 다운로드 창에 있는 항목의 캐시 파일은 남김 (다운로드 목록 유지, 종료 시 세션 캐시와 함께 삭제)
        var held = _download?.HeldResourceIds ?? new HashSet<int>();
        foreach (var r in _resources)
            if (!held.Contains(r.Id))
                try { File.Delete(r.CachePath); } catch { }
        _resources.Clear();
        _byUrl.Clear();
        _tracking?.DetachResources();
        _capture?.ResetSeen();
        UpdateCategoryCounts();
        UpdateStatus();
    }

    private string SaveFolder
    {
        get => _settings.SaveFolder;
        set => _settings.SaveFolder = value;
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
    // 다운로드 창 (2026-10-28)
    // =====================================================================

    private void SendDownload_Click(object sender, RoutedEventArgs e) =>
        SendToDownload(_resources.Where(r => r.IsChecked).ToList());

    /// <summary>항목을 다운로드 창으로 보냄 (창이 없으면 열고, 있으면 앞으로). 추적 창에서도 호출</summary>
    internal void SendToDownload(List<CapturedResource> list)
    {
        if (list.Count == 0)
        {
            SetStatus("다운로드 창으로 보낼 항목을 체크해 주세요.");
            return;
        }
        if (_download == null)
        {
            _download = new DownloadWindow(_hotkeys) { Title = "다운로드" };
            _download.Closed += (_, _) => _download = null;
            _download.Show();
        }
        else
        {
            if (_download.WindowState == WindowState.Minimized) _download.WindowState = WindowState.Normal;
            _download.Activate();
        }
        int added = _download.AddItems(list);
        SetStatus($"다운로드 창으로 {added}개 보냄");
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
        bool overwrite = _settings.OverwriteExisting;

        foreach (var r in list)
        {
            var (outcome, _, error) = await Task.Run(() => ResourceSaver.Save(r, folder, overwrite));
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
