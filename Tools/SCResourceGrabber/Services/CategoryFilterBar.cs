using System.Windows.Controls;
using SCResourceGrabber.Models;

namespace SCResourceGrabber.Services;

/// <summary>
/// 분류 필터 체크박스 줄 (메인 목록·추적 창 공용).
/// 맨 왼쪽 [전체]: 하나라도 꺼져 있으면 전부 켜고, 모두 켜져 있으면 전부 끈다. 일부만 켜져 있으면 중간 상태로 표시.
/// </summary>
public sealed class CategoryFilterBar
{
    private readonly CheckBox _all = new() { Content = "전체", FontWeight = System.Windows.FontWeights.Bold };
    private readonly Dictionary<ResourceCategory, CheckBox> _checks = new();
    private bool _suppress;

    /// <summary>체크 상태가 바뀜 (일괄 변경 시 1회만)</summary>
    public event Action? Changed;

    /// <param name="shown">표시할 분류 (선언 순서대로 배치)</param>
    /// <param name="defaultVisible">처음에 켜 둘 분류</param>
    public CategoryFilterBar(Panel panel, IEnumerable<ResourceCategory> shown, IEnumerable<ResourceCategory> defaultVisible)
    {
        var defaults = defaultVisible.ToHashSet();
        var shownSet = shown.ToHashSet();
        _all.Click += (_, _) => SetAll(!AllChecked);
        panel.Children.Add(_all);

        foreach (ResourceCategory cat in Enum.GetValues<ResourceCategory>().Where(shownSet.Contains))
        {
            var cb = new CheckBox { IsChecked = defaults.Contains(cat), Tag = cat };
            cb.Checked += (_, _) => OnItemChanged();
            cb.Unchecked += (_, _) => OnItemChanged();
            _checks[cat] = cb;
            panel.Children.Add(cb);
        }
        UpdateAllBox();
        UpdateCounts(new Dictionary<ResourceCategory, int>());
    }

    public bool Allows(ResourceCategory cat) => _checks.TryGetValue(cat, out var cb) && cb.IsChecked == true;

    public void UpdateCounts(IReadOnlyDictionary<ResourceCategory, int> counts)
    {
        foreach (var (cat, cb) in _checks)
            cb.Content = $"{CapturedResource.CategoryLabels[cat]} ({counts.GetValueOrDefault(cat)})";
    }

    private bool AllChecked => _checks.Values.All(c => c.IsChecked == true);

    private void SetAll(bool value)
    {
        _suppress = true;
        foreach (var cb in _checks.Values) cb.IsChecked = value;
        _suppress = false;
        UpdateAllBox();
        Changed?.Invoke();
    }

    private void OnItemChanged()
    {
        if (_suppress) return;
        UpdateAllBox();
        Changed?.Invoke();
    }

    private void UpdateAllBox()
    {
        int on = _checks.Values.Count(c => c.IsChecked == true);
        _all.IsChecked = on == _checks.Count ? true : on == 0 ? false : null;
    }
}
