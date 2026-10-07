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

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<CapturedResource> _resources = new();
    private readonly ICollectionView _view;
    private readonly Dictionary<ResourceCategory, CheckBox> _categoryChecks = new();
    private readonly MediaPlayer _player = new();
    private ResourceCapture? _capture;
    private string[] _searchWords = [];
    private bool _busy;

    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCResourceGrabber", "Cache");

    // 기본으로 표시할 분류
    private static readonly HashSet<ResourceCategory> DefaultVisible =
        [ResourceCategory.Image, ResourceCategory.Audio, ResourceCategory.Video, ResourceCategory.Spine];

    public MainWindow()
    {
        InitializeComponent();

        _view = CollectionViewSource.GetDefaultView(_resources);
        _view.Filter = o => o is CapturedResource r && PassesFilter(r);
        ResourceList.ItemsSource = _view;

        BuildCategoryFilters();

        SaveFolderBox.Text = _settings.SaveFolder;
        KeepPathCheck.IsChecked = _settings.KeepUrlPath;
        OverwriteCheck.IsChecked = _settings.OverwriteExisting;

        Loaded += async (_, _) => await InitWebViewAsync();
        Closing += (_, _) => OnClosingCleanup();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F5) WebView.CoreWebView2?.Reload(); };
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
            core.Settings.AreDevToolsEnabled = true;
            core.SourceChanged += (_, _) => AddressBox.Text = core.Source;

            string sessionCache = Path.Combine(CacheRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            _capture = new ResourceCapture(core, _settings, sessionCache);
            _capture.Captured += OnCaptured;
            _capture.Log += msg => Debug.WriteLine("[Capture] " + msg);

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
        _player.Close();
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

    // =====================================================================
    // 필터
    // =====================================================================

    private void BuildCategoryFilters()
    {
        foreach (ResourceCategory cat in Enum.GetValues<ResourceCategory>())
        {
            var cb = new CheckBox { IsChecked = DefaultVisible.Contains(cat), Tag = cat };
            cb.Checked += (_, _) => RefreshView();
            cb.Unchecked += (_, _) => RefreshView();
            _categoryChecks[cat] = cb;
            CategoryPanel.Children.Add(cb);
        }
        UpdateCategoryCounts();
    }

    private void UpdateCategoryCounts()
    {
        var counts = _resources.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (cat, cb) in _categoryChecks)
            cb.Content = $"{CapturedResource.CategoryLabels[cat]} ({counts.GetValueOrDefault(cat)})";
    }

    private bool PassesFilter(CapturedResource r) =>
        _categoryChecks.TryGetValue(r.Category, out var cb) && cb.IsChecked == true &&
        _searchWords.All(w => r.Url.Contains(w, StringComparison.OrdinalIgnoreCase));

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchWords = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        RefreshView();
    }

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
        _player.Close();
        ShowPreview(null);
        foreach (var r in _resources)
            try { File.Delete(r.CachePath); } catch { }
        _resources.Clear();
        _capture?.ResetSeen();
        UpdateCategoryCounts();
        UpdateStatus();
    }

    // =====================================================================
    // 미리보기
    // =====================================================================

    private CapturedResource? Current => ResourceList.SelectedItem as CapturedResource;

    private void ResourceList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowPreview(Current);

    private void ShowPreview(CapturedResource? r)
    {
        _player.Stop();
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewText.Text = "";
        AudioPanel.Visibility = Visibility.Collapsed;

        if (r == null) { PreviewInfo.Text = ""; return; }

        PreviewInfo.Text = $"{r.FileName}  ·  {r.CategoryLabel}  ·  {r.SizeText}  ·  {r.Mime}";
        PreviewInfo.ToolTip = r.Url;

        switch (r.Category)
        {
            case ResourceCategory.Image:
                var bmp = CapturedResource.LoadBitmap(r.CachePath);
                if (bmp != null)
                {
                    PreviewImage.Source = bmp;
                    PreviewInfo.Text += $"  ·  {bmp.PixelWidth}×{bmp.PixelHeight}";
                }
                else ShowText("(이 이미지 포맷은 미리보기를 지원하지 않습니다)");
                break;

            case ResourceCategory.Audio:
            case ResourceCategory.Video:
                AudioPanel.Visibility = Visibility.Visible;
                break;

            case ResourceCategory.Json:
            case ResourceCategory.Spine when r.Extension is "json" or "atlas":
            case ResourceCategory.Other:
                ShowText(ReadTextHead(r.CachePath, 64 * 1024));
                break;

            default:
                ShowText("(미리보기 없음)");
                break;
        }
    }

    private void ShowText(string text)
    {
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewText.Visibility = Visibility.Visible;
        PreviewText.Text = text;
    }

    private static string ReadTextHead(string path, int maxBytes)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[Math.Min(maxBytes, fs.Length)];
            int n = fs.Read(buf, 0, buf.Length);
            string s = Encoding.UTF8.GetString(buf, 0, n);
            return fs.Length > maxBytes ? s + "\n\n… (이하 생략)" : s;
        }
        catch (Exception ex) { return "읽기 실패: " + ex.Message; }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (Current == null) return;
        try
        {
            _player.Open(new Uri(Current.CachePath));
            _player.Play();
        }
        catch (Exception ex) { SetStatus("재생 실패: " + ex.Message); }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _player.Stop();

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (Current != null) Clipboard.SetText(Current.Url);
    }

    // =====================================================================
    // 저장
    // =====================================================================

    private void SaveFolderBox_LostFocus(object sender, RoutedEventArgs e) => ApplySaveOptions();
    private void SaveOption_Changed(object sender, RoutedEventArgs e) => ApplySaveOptions();

    private void ApplySaveOptions()
    {
        _settings.SaveFolder = SaveFolderBox.Text.Trim();
        _settings.KeepUrlPath = KeepPathCheck.IsChecked == true;
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
        Directory.CreateDirectory(_settings.SaveFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.SaveFolder}\"") { UseShellExecute = true });
    }

    private async void SaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        var targets = ResourceList.SelectedItems.Cast<CapturedResource>().ToList();
        await SaveManyAsync(targets);
    }

    private async void SaveChecked_Click(object sender, RoutedEventArgs e) =>
        await SaveManyAsync(_resources.Where(r => r.IsChecked).ToList());

    private async void SaveVisible_Click(object sender, RoutedEventArgs e) =>
        await SaveManyAsync(VisibleItems.ToList());

    private async Task SaveManyAsync(List<CapturedResource> list)
    {
        if (_busy || list.Count == 0) return;
        ApplySaveOptions();
        if (string.IsNullOrWhiteSpace(_settings.SaveFolder))
        {
            SetStatus("저장 폴더를 지정해 주세요.");
            return;
        }

        _busy = true;
        int saved = 0, skipped = 0, failed = 0;
        string? lastError = null;
        var settings = _settings;

        foreach (var r in list)
        {
            var (outcome, _, error) = await Task.Run(() => ResourceSaver.Save(r, settings));
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
