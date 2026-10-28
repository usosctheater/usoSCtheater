using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SCResourceGrabber.Models;
using SCResourceGrabber.Services;

namespace SCResourceGrabber;

/// <summary>
/// 다운로드 창 (2026-10-28). 메인·추적 창에서 체크한 항목을 받아, 미리보기를 보며 이름을 붙이고 한 번에 저장한다.
/// - 이미지: PNG 변환 / 투명 여백 잘라내기 / 정사각형 가운데 정렬 + 여백(%) — 옵션은 settings.json에 기억
/// - 이미지가 아닌 항목: 이름만 바꿔 원본 그대로 복사
/// - 이름 칸: names.txt 자동 완성, Enter로 다음 항목
/// </summary>
public partial class DownloadWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Shared;
    private readonly HotkeyMap _hotkeys;
    private readonly ObservableCollection<DownloadItem> _items = new();
    private DownloadItem? _current;
    private bool _loading;          // 초기값 채우는 동안 옵션 변경 이벤트 무시
    private bool _busy;             // 저장 중
    private int _previewVersion;    // 미리보기 비동기 결과가 늦게 도착하면 버림

    public DownloadWindow(HotkeyMap hotkeys)
    {
        InitializeComponent();
        _hotkeys = hotkeys;
        ItemList.ItemsSource = _items;

        _loading = true;
        PngCheck.IsChecked = _settings.DownloadConvertPng;
        TrimCheck.IsChecked = _settings.DownloadTrim;
        SquareCheck.IsChecked = _settings.DownloadSquare;
        PaddingBox.Text = _settings.DownloadPadding.ToString(CultureInfo.InvariantCulture);
        OverwriteCheck.IsChecked = _settings.DownloadOverwrite;
        FolderBox.Text = _settings.DownloadSaveFolder;
        _loading = false;

        string? saveKey = _hotkeys.GestureText(HotkeyMap.SaveChecked);
        SaveAllButton.ToolTip = saveKey == null ? "모두 저장" : $"모두 저장 ({saveKey}, 이름 입력 중에도 동작)";

        NameBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => OnNameTyped()));
        ItemList.PreviewTextInput += ItemList_PreviewTextInput;   // 목록 클릭 시 포커스는 목록에 (Delete = 빼기), 글자를 치면 이름 칸으로
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += (_, _) => { CommitName(); ApplyOptions(); };

        ReloadNames();
        ShowSelection();
    }

    // =====================================================================
    // 메인 창에서 호출
    // =====================================================================

    /// <summary>목록 끝에 추가 (이미 있는 항목은 건너뜀). 새로 들어온 첫 항목을 선택. 추가한 개수 반환</summary>
    public int AddItems(IEnumerable<CapturedResource> resources)
    {
        int firstNew = _items.Count, added = 0;
        foreach (var r in resources)
        {
            // URL도 비교: 메인 목록을 비운 뒤 같은 URL을 다시 받으면 새 Id가 붙음
            if (_items.Any(i => i.Resource.Id == r.Id || i.Resource.Url == r.Url)) continue;
            _items.Add(new DownloadItem { Resource = r });
            added++;
        }
        Renumber();
        if (added > 0)
        {
            ItemList.SelectedIndex = firstNew;
            ItemList.ScrollIntoView(_items[firstNew]);
            FocusSelectedRow();
        }
        SetStatus($"{added}개 추가 (이미 있는 항목 {resources.Count() - added}개 건너뜀) · 전체 {_items.Count}개");
        return added;
    }

    /// <summary>다운로드 목록에 있는 리소스 Id — 메인 목록을 비울 때 이 캐시 파일은 남긴다 (종료 시 세션 캐시와 함께 삭제)</summary>
    public HashSet<int> HeldResourceIds => _items.Select(i => i.Resource.Id).ToHashSet();

    // =====================================================================
    // 단축키
    // =====================================================================

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 이름 입력 중에도 저장은 동작해야 하므로 포커스와 무관하게 확인 (저장 외 동작은 처리하지 않음 → Ctrl+A 등 편집 키 보호)
        if (_hotkeys.ResolveIgnoringFocus(e) == HotkeyMap.SaveChecked)
        {
            SaveAll_Click(this, e);
            e.Handled = true;
        }
    }

    private void ItemList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { FocusNameBox(selectAll: true); e.Handled = true; }
        else if (e.Key == Key.Delete) { Remove_Click(this, e); e.Handled = true; }
    }

    // =====================================================================
    // 선택 · 이름 입력
    // =====================================================================

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitName();
        ShowSelection();
    }

    private void ShowSelection()
    {
        _current = ItemList.SelectedItem as DownloadItem;
        _loading = true;
        NameBox.Text = _current?.SaveName ?? "";
        _loading = false;
        NameBox.IsEnabled = _current != null;
        UpdateNameHint();
        RefreshPreview();
        UpdateStatus();
    }

    /// <summary>이름 칸 입력을 바로 반영 (목록·중복 안내 갱신)</summary>
    private void OnNameTyped()
    {
        if (_loading || _current == null) return;
        _current.SaveName = NameBox.Text.Trim();
        UpdateNameHint();
        RefreshPreviewInfo();
    }

    private void CommitName()
    {
        if (_current != null) _current.SaveName = NameBox.Text.Trim();
    }

    private void NameBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitName();
        NameBox.IsDropDownOpen = false;
        if (_current != null)
        {
            int next = _items.IndexOf(_current) + 1;
            if (next < _items.Count)
            {
                ItemList.SelectedIndex = next;
                ItemList.ScrollIntoView(_items[next]);
            }
            else SetStatus("마지막 항목입니다. 이름을 다 붙였으면 [모두 저장]");
        }
        FocusNameBox(selectAll: true);
        e.Handled = true;
    }

    /// <summary>목록에서 바로 글자를 치면 이름 칸으로 넘어가 그 글자부터 입력 (기존 이름은 덮어씀)</summary>
    private void ItemList_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_current == null || !NameBox.IsEnabled || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if (NameBox.Template?.FindName("PART_EditableTextBox", NameBox) is not TextBox tb) return;
        tb.Focus();
        tb.SelectAll();
        tb.SelectedText = e.Text;
        tb.CaretIndex = tb.Text.Length;
        e.Handled = true;
    }

    /// <summary>선택한 줄에 키보드 포커스 (Delete·Enter가 목록에서 동작하도록)</summary>
    private void FocusSelectedRow()
    {
        Dispatcher.InvokeAsync(() =>
        {
            ItemList.UpdateLayout();
            if (ItemList.ItemContainerGenerator.ContainerFromItem(ItemList.SelectedItem) is ListViewItem row) row.Focus();
            else ItemList.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void FocusNameBox(bool selectAll)
    {
        if (!NameBox.IsEnabled) return;
        Dispatcher.InvokeAsync(() =>
        {
            NameBox.Focus();
            if (selectAll && NameBox.Template?.FindName("PART_EditableTextBox", NameBox) is TextBox tb) tb.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>같은 이름을 쓴 다른 항목 안내 (저장 때는 겹치면 저장하지 않음)</summary>
    private void UpdateNameHint()
    {
        if (_current == null) { NameHint.Text = ""; return; }
        string name = _current.SaveName;
        List<DownloadItem> same = name.Length == 0 ? new()
            : _items.Where(i => i != _current && string.Equals(i.SaveName, name, StringComparison.OrdinalIgnoreCase)).ToList();
        NameHint.Text = same.Count > 0
            ? $"⚠ 같은 이름을 쓴 항목: #{string.Join(", #", same.Select(i => i.Index))}"
            : $"자동 완성 후보 {NameBox.Items.Count}개 ({NameCatalog.FileName})";
        NameHint.Foreground = same.Count > 0 ? System.Windows.Media.Brushes.OrangeRed : System.Windows.Media.Brushes.Gray;
    }

    private void ReloadNames()
    {
        var names = NameCatalog.Load();
        string text = NameBox.Text;
        _loading = true;
        NameBox.ItemsSource = names;
        NameBox.Text = text;
        _loading = false;
        UpdateNameHint();
        if (names.Count == 0) SetStatus($"자동 완성 후보 없음 — {NameCatalog.FilePath}");
    }

    private void ReloadNames_Click(object sender, RoutedEventArgs e)
    {
        ReloadNames();
        SetStatus($"이름 목록 다시 읽음: {NameBox.Items.Count}개");
    }

    private void EditNames_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(NameCatalog.FilePath))
                File.WriteAllText(NameCatalog.FilePath, "# 다운로드 창 이름 자동 완성 후보 (한 줄에 하나, # 뒤는 설명)\n");
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{NameCatalog.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { SetStatus("이름 목록을 열 수 없음: " + ex.Message); }
    }

    // =====================================================================
    // 목록 정리
    // =====================================================================

    private void Renumber()
    {
        for (int i = 0; i < _items.Count; i++) _items[i].Index = i + 1;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var sel = ItemList.SelectedItems.Cast<DownloadItem>().ToList();
        if (sel.Count == 0) return;
        int first = _items.IndexOf(sel[0]);
        foreach (var i in sel) _items.Remove(i);
        Renumber();
        if (_items.Count > 0) ItemList.SelectedIndex = Math.Min(first, _items.Count - 1);
        else ShowSelection();
    }

    private void ClearList_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _items.Clear();
        ShowSelection();
    }

    // =====================================================================
    // 옵션 (마지막 설정 기억)
    // =====================================================================

    private ImageOptions Options => new(PngCheck.IsChecked == true, TrimCheck.IsChecked == true,
                                        SquareCheck.IsChecked == true, ParsePadding());

    private double ParsePadding() =>
        double.TryParse(PaddingBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? Math.Min(v, 100)
            : _settings.DownloadPadding;

    private void ApplyOptions()
    {
        if (_loading) return;
        _settings.DownloadConvertPng = PngCheck.IsChecked == true;
        _settings.DownloadTrim = TrimCheck.IsChecked == true;
        _settings.DownloadSquare = SquareCheck.IsChecked == true;
        _settings.DownloadPadding = ParsePadding();
        _settings.DownloadOverwrite = OverwriteCheck.IsChecked == true;
        _settings.DownloadSaveFolder = FolderBox.Text.Trim();
        _settings.Save();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ApplyOptions();
        RefreshPreview();
    }

    private void PaddingBox_LostFocus(object sender, RoutedEventArgs e)
    {
        PaddingBox.Text = ParsePadding().ToString(CultureInfo.InvariantCulture);
        Option_Changed(sender, e);
    }

    private void PaddingBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { PaddingBox_LostFocus(sender, e); e.Handled = true; }
    }

    private void FolderBox_LostFocus(object sender, RoutedEventArgs e) => ApplyOptions();

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "다운로드 저장 폴더 선택" };
        if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text;
        if (dlg.ShowDialog(this) == true)
        {
            FolderBox.Text = dlg.FolderName;
            ApplyOptions();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        ApplyOptions();
        string folder = FolderBox.Text.Trim();
        if (folder.Length == 0) return;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    // =====================================================================
    // 미리보기 (옵션 적용 결과 그대로)
    // =====================================================================

    private async void RefreshPreview()
    {
        int version = ++_previewVersion;
        var item = _current;
        PreviewImage.Source = null;
        PreviewEmpty.Visibility = Visibility.Visible;
        _previewSize = item?.IsImage == true ? "불러오는 중…" : "";
        RefreshPreviewInfo();
        if (item == null || !item.IsImage) return;

        var opt = Options;
        string path = item.Resource.CachePath;
        var (result, origW, origH) = await Task.Run(() =>
        {
            var orig = CapturedResource.LoadBitmap(path);
            var processed = orig == null ? null : ImageProcessor.Process(path, opt);
            return (processed, orig?.PixelWidth ?? 0, orig?.PixelHeight ?? 0);
        });
        if (version != _previewVersion) return;   // 그사이 다른 항목·옵션으로 바뀜

        PreviewImage.Source = result;
        PreviewEmpty.Visibility = result == null ? Visibility.Visible : Visibility.Collapsed;
        _previewSize = result == null ? "읽을 수 없는 이미지" : $"원본 {origW}×{origH} → 저장 {result.PixelWidth}×{result.PixelHeight}";
        RefreshPreviewInfo();
    }

    private string _previewSize = "";

    private void RefreshPreviewInfo()
    {
        if (_current == null) { PreviewInfo.Text = ""; return; }
        string file = Path.GetFileName(TargetPath(_current, ".", Options));
        PreviewInfo.Text = _current.IsImage
            ? $"#{_current.Index} · {_previewSize} · 파일 이름: {file}"
            : $"#{_current.Index} · {_current.CategoryLabel} {_current.Resource.SizeText} — 원본 그대로 저장 · 파일 이름: {file}";
    }

    // =====================================================================
    // 저장
    // =====================================================================

    /// <summary>이미지에 PNG 변환 또는 모양 변경이 걸리면 .png, 아니면 원본 확장자</summary>
    private static bool WillWritePng(DownloadItem item, ImageOptions opt) => item.IsImage && (opt.ConvertToPng || opt.Reshapes);

    private static string TargetPath(DownloadItem item, string folder, ImageOptions opt)
    {
        string stem = item.SaveName.Length > 0 ? item.SaveName
                    : item.OriginalStem.Length > 0 ? item.OriginalStem
                    : item.Resource.Host;
        string ext = WillWritePng(item, opt) ? ".png" : Path.GetExtension(item.OriginalName);
        if (ext.Length == 0) ext = "." + item.Resource.Extension;
        return Path.Combine(folder, ResourceSaver.Sanitize(stem) + ext);
    }

    private async void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _items.Count == 0) return;
        CommitName();
        ApplyOptions();
        string folder = FolderBox.Text.Trim();
        if (folder.Length == 0) { SetStatus("저장 폴더를 지정해 주세요."); return; }

        var opt = Options;
        bool overwrite = OverwriteCheck.IsChecked == true;
        var targets = _items.Select(i => (item: i, path: TargetPath(i, folder, opt))).ToList();

        // 저장 이름이 겹치면 아무것도 저장하지 않음 (덮어쓰기 사고 방지)
        var dups = targets.GroupBy(t => t.path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (dups.Count > 0)
        {
            foreach (var g in dups) foreach (var t in g) t.item.Status = "이름 겹침";
            SetStatus("저장 이름이 겹칩니다: " + string.Join(", ", dups.Select(g => Path.GetFileName(g.Key))) + " — 이름을 고친 뒤 다시 저장");
            return;
        }

        _busy = true;
        SaveAllButton.IsEnabled = false;
        int saved = 0, exists = 0, failed = 0;
        string? lastError = null;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var (item, path) in targets)
            {
                var (status, error) = await Task.Run(() => SaveOne(item, path, opt, overwrite));
                item.Status = status;
                if (status == "저장됨") saved++;
                else if (status == "이미 있음") exists++;
                else { failed++; lastError = error; }
                SetStatus($"저장 중 {saved + exists + failed}/{targets.Count}");
            }
        }
        catch (Exception ex) { lastError = ex.Message; }
        finally
        {
            _busy = false;
            SaveAllButton.IsEnabled = true;
        }
        SetStatus($"저장 완료: {saved}개 저장, {exists}개 같은 이름 파일이 있어 건너뜀" +
                  (exists > 0 ? " ('같은 이름 덮어쓰기'를 켜면 덮어씀)" : "") +
                  $", {failed}개 실패" + (lastError != null ? $" (마지막 오류: {lastError})" : ""));
    }

    private static (string status, string? error) SaveOne(DownloadItem item, string path, ImageOptions opt, bool overwrite)
    {
        try
        {
            string cache = item.Resource.CachePath;
            if (!File.Exists(cache)) return ("캐시 없음", "캐시 파일이 삭제됨");
            if (File.Exists(path) && !overwrite) return ("이미 있음", null);

            if (WillWritePng(item, opt))
            {
                var bmp = ImageProcessor.Process(cache, opt);
                if (bmp == null) return ("실패", "이미지를 읽을 수 없음");
                ImageProcessor.SavePng(bmp, path);
            }
            else File.Copy(cache, path, overwrite: true);
            return ("저장됨", null);
        }
        catch (Exception ex)
        {
            return ("실패", ex.Message);
        }
    }

    // =====================================================================
    // 상태
    // =====================================================================

    private void UpdateStatus()
    {
        if (_busy) return;
        int named = _items.Count(i => i.SaveName.Length > 0);
        SetStatus($"{_items.Count}개 · 이름 붙임 {named}개" + (_current != null ? $" · 선택 #{_current.Index}" : ""));
    }

    private void SetStatus(string text) => StatusText.Text = text;
}
