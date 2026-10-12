using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCTheaterEditor.Models;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 아이돌 필터 (다운로드·리소스 탭 공용): 유닛 드롭다운 → 그 유닛 멤버 체크박스.
/// - 체크 상태는 유닛을 바꿔도 아이돌별로 유지
/// - [유닛 전체] 버튼: 고른 유닛 멤버가 모두 켜져 있으면 모두 끄고, 아니면 모두 켬
/// - 드롭다운 맨 앞 "전체"는 모든 아이돌
/// </summary>
public sealed partial class IdolFilterViewModel : ObservableObject
{
    public const string AllUnitsName = "전체";

    private readonly Dictionary<int, IdolCheckItem> _items = new();
    private bool _suppress;

    /// <param name="allowUnknown">필터에 없는 아이돌(아이돌 표가 아직 없는 세트 등)을 보여줄지</param>
    public IdolFilterViewModel(bool allowUnknown = false)
    {
        AllowUnknown = allowUnknown;
        Units = [];
        VisibleMembers = [];
        Summary = "";
    }

    public bool AllowUnknown { get; }

    [ObservableProperty]
    public partial IReadOnlyList<UnitOption> Units { get; private set; }

    [ObservableProperty]
    public partial UnitOption? SelectedUnit { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<IdolCheckItem> VisibleMembers { get; private set; }

    /// <summary>"3/33명" 같은 요약</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; }

    /// <summary>체크 상태가 바뀜 (일괄 변경은 1회)</summary>
    public event Action? Changed;

    /// <summary>고른 유닛이 바뀜 (설정 저장용)</summary>
    public event Action? SelectedUnitChanged;

    public IEnumerable<int> AllIds => _items.Keys;

    /// <param name="idols">아이돌 목록 (표시 순서)</param>
    /// <param name="units">유닛 분류</param>
    /// <param name="checkedIds">켜 둘 아이돌. null이면 전부</param>
    /// <param name="selectedUnitName">고를 유닛 이름 (없으면 "전체")</param>
    public void Rebuild(IReadOnlyList<IdolInfo> idols, IReadOnlyList<IdolUnit> units, IEnumerable<int>? checkedIds, string? selectedUnitName)
    {
        var on = checkedIds?.ToHashSet();
        _suppress = true;   // 만드는 동안의 체크 변경은 알리지 않음
        _items.Clear();
        foreach (var idol in idols)
            _items[idol.IdolId] = new IdolCheckItem(idol.IdolId, idol.IdolName, on == null || on.Contains(idol.IdolId), OnItemChanged);

        var list = new List<UnitOption> { new(AllUnitsName, idols.Select(i => i.IdolId).ToList()) };
        list.AddRange(IdolUnits.Arrange(idols, units).Select(u => new UnitOption(u.name, u.members.Select(m => m.IdolId).ToList())));
        Units = list;
        SelectedUnit = list.FirstOrDefault(u => u.Name == selectedUnitName) ?? list[0];
        _suppress = false;
        UpdateSummary();
    }

    public bool Allows(int idolId) =>
        _items.TryGetValue(idolId, out var item) ? item.IsChecked : AllowUnknown;

    /// <summary>켜진 아이돌 (아이돌 목록 순서)</summary>
    public List<int> CheckedIds => _items.Values.Where(i => i.IsChecked).Select(i => i.Id).ToList();

    /// <summary>멤버 이름 옆 표시 (예: "(149/149)"). null·빈 값이면 이름만</summary>
    public void UpdateCounts(Func<int, string?> suffix)
    {
        foreach (var item in _items.Values)
        {
            string? s = suffix(item.Id);
            item.Label = string.IsNullOrEmpty(s) ? item.Name : $"{item.Name} {s}";
        }
    }

    /// <summary>고른 유닛 멤버를 모두 켜거나 끔</summary>
    [RelayCommand]
    private void ToggleUnit()
    {
        if (SelectedUnit == null) return;
        var ids = SelectedUnit.Ids.Where(_items.ContainsKey).ToList();
        bool allOn = ids.All(id => _items[id].IsChecked);
        _suppress = true;
        foreach (var id in ids) _items[id].IsChecked = !allOn;
        _suppress = false;
        Notify();
    }

    partial void OnSelectedUnitChanged(UnitOption? value)
    {
        VisibleMembers = value == null ? [] : value.Ids.Where(_items.ContainsKey).Select(id => _items[id]).ToList();
        SelectedUnitChanged?.Invoke();
    }

    private void OnItemChanged()
    {
        if (!_suppress) Notify();
    }

    private void Notify()
    {
        UpdateSummary();
        Changed?.Invoke();
    }

    private void UpdateSummary()
    {
        int on = _items.Values.Count(i => i.IsChecked);
        Summary = $"{on}/{_items.Count}명 선택";
        foreach (var u in Units)
        {
            int unitOn = u.Ids.Count(id => _items.TryGetValue(id, out var i) && i.IsChecked);
            u.Label = $"{u.Name} ({unitOn}/{u.Ids.Count})";
        }
    }
}

/// <summary>아이돌 체크박스 1개</summary>
public sealed partial class IdolCheckItem : ObservableObject
{
    private readonly Action _onChanged;

    public IdolCheckItem(int id, string name, bool isChecked, Action onChanged)
    {
        Id = id;
        Name = name;
        Label = name;
        _onChanged = onChanged;
        IsChecked = isChecked;   // 처음 값 — 필터가 만드는 동안에는 알림을 막아 둠 (IdolFilterViewModel._suppress)
    }

    public int Id { get; }
    public string Name { get; }

    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; }

    partial void OnIsCheckedChanged(bool value) => _onChanged();
}

/// <summary>유닛 드롭다운 항목 1개</summary>
public sealed partial class UnitOption : ObservableObject
{
    public UnitOption(string name, List<int> ids)
    {
        Name = name;
        Ids = ids;
        Label = name;
    }

    public string Name { get; }
    public IReadOnlyList<int> Ids { get; }

    /// <summary>"イルミネーションスターズ (3/3)"</summary>
    [ObservableProperty]
    public partial string Label { get; set; }
}
