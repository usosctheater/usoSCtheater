using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SCResourceGrabber.Models;
using SCResourceGrabber.Services;

namespace SCResourceGrabber;

/// <summary>
/// 추적 모드 창. 창이 열려 있는 동안만 추적한다.
/// - 새로 캡처된 리소스(OnLoaded)와 오디오 재생(OnPlayed/OnEnded)을 URL 단위로 합쳐 최신순으로 표시
/// - 저장/상세 창은 메인 창의 기능을 그대로 사용
/// </summary>
public partial class TrackingWindow : Window
{
    private readonly MainWindow _main;
    private readonly Func<string, CapturedResource?> _findResource;
    private readonly Action<CapturedResource> _openDetail;
    private readonly Func<List<CapturedResource>, Task> _saveMany;

    private readonly ObservableCollection<TrackedItem> _items = new();
    private readonly Dictionary<string, TrackedItem> _byUrl = new();
    private readonly Dictionary<string, TrackedItem> _activePlays = new(); // 재생 id → 항목
    private readonly ICollectionView _view;
    private readonly CategoryFilterBar _filter;
    private readonly DateTime _startedAt = DateTime.Now;

    public TrackingWindow(MainWindow main,
                          Func<string, CapturedResource?> findResource,
                          Action<CapturedResource> openDetail,
                          Func<List<CapturedResource>, Task> saveMany)
    {
        InitializeComponent();
        _main = main;
        _findResource = findResource;
        _openDetail = openDetail;
        _saveMany = saveMany;

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = o => o is TrackedItem t && PassesFilter(t);
        TrackList.ItemsSource = _view;
        _filter = new CategoryFilterBar(CategoryPanel, main.FilterCategories, main.DefaultCategories);
        ListViewSorter.Enable(TrackList, _view, new Dictionary<string, string>
        {
            ["종류"] = nameof(TrackedItem.Category),
            ["크기"] = nameof(TrackedItem.Size),
            ["받은 시각"] = nameof(TrackedItem.FirstSeen),
        });
        _filter.Changed += Refresh;
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateStatus();
    }

    /// <summary>메인 창과 같은 단축키 설정. 이 창에 포커스가 있으면 이 창의 목록에 적용된다.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (_main.Hotkeys.Resolve(e))
        {
            case HotkeyMap.SaveChecked: SaveChecked_Click(this, e); break;
            case HotkeyMap.ToggleTracking: Close(); break;
            case HotkeyMap.ToggleAllChecks: ToggleAll_Click(this, e); break;
            case HotkeyMap.ClearList: Clear_Click(this, e); break;
            default: return;
        }
        e.Handled = true;
    }

    // =====================================================================
    // 메인 창에서 호출
    // =====================================================================

    /// <summary>추적 중 새로 캡처된 리소스</summary>
    public void OnLoaded(CapturedResource res)
    {
        var item = GetOrAdd(res.Url);
        item.Resource ??= res;
        item.Loaded = true;
        Refresh();
    }

    public void OnPlayed(string playId, string url, bool loop)
    {
        var item = GetOrAdd(url);
        item.Resource ??= _findResource(url);
        item.PlayCount++;
        item.LastPlayed = DateTime.Now;
        item.ActivePlays++;
        _activePlays[playId] = item;

        // 다시 재생된 항목은 맨 위로
        int idx = _items.IndexOf(item);
        if (idx > 0) _items.Move(idx, 0);
        Refresh();
    }

    public void OnEnded(string playId)
    {
        if (_activePlays.Remove(playId, out var item))
        {
            item.ActivePlays--;
            UpdateStatus();
        }
    }

    /// <summary>페이지 새로고침 등으로 재생이 모두 끊긴 경우</summary>
    public void ResetPlaying()
    {
        foreach (var item in _activePlays.Values) item.ActivePlays = 0;
        _activePlays.Clear();
        Refresh();
    }

    /// <summary>메인 목록을 비웠을 때: 캐시가 삭제되므로 연결만 끊는다</summary>
    public void DetachResources()
    {
        foreach (var item in _items) item.Resource = null;
        Refresh();
    }

    private TrackedItem GetOrAdd(string url)
    {
        if (_byUrl.TryGetValue(url, out var item)) return item;
        item = new TrackedItem { Url = url };
        _byUrl[url] = item;
        _items.Insert(0, item);
        return item;
    }

    // =====================================================================
    // 필터 / 상태
    // =====================================================================

    private void UpdateCategoryCounts() =>
        _filter.UpdateCounts(_items.GroupBy(i => i.Category).ToDictionary(g => g.Key, g => g.Count()));

    private bool PassesFilter(TrackedItem t) => _filter.Allows(t.Category);

    private void Refresh()
    {
        _view.Refresh();
        UpdateCategoryCounts();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int playing = _items.Count(i => i.IsPlaying);
        StatusText.Text = $"● 추적 중 (시작 {_startedAt:HH:mm:ss}) · {_items.Count}개 · 재생 중 {playing}개";
    }

    private IEnumerable<TrackedItem> VisibleItems => _view.Cast<TrackedItem>();

    // =====================================================================
    // 동작
    // =====================================================================

    private void Item_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: TrackedItem { Resource: { } res } })
        {
            _openDetail(res);
            e.Handled = true;
        }
    }

    private void ToggleAll_Click(object sender, RoutedEventArgs e)
    {
        var visible = VisibleItems.ToList();
        bool check = visible.Any(i => !i.IsChecked);
        foreach (var i in visible) i.IsChecked = check;
    }

    private async void SaveChecked_Click(object sender, RoutedEventArgs e) =>
        await SaveAsync(_items.Where(i => i.IsChecked));

    private async Task SaveAsync(IEnumerable<TrackedItem> items)
    {
        var list = items.Where(i => i.Resource != null).Select(i => i.Resource!).Distinct().ToList();
        if (list.Count == 0) return;
        await _saveMany(list);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        // 재생 중 항목은 남겨서 종료 표시가 계속 동작하게 한다
        var keep = _activePlays.Values.Distinct().ToHashSet();
        foreach (var item in _items.Where(i => !keep.Contains(i)).ToList())
        {
            _items.Remove(item);
            _byUrl.Remove(item.Url);
        }
        Refresh();
    }
}
