using System.Windows;
using System.Windows.Controls;
using SCTheaterEditor.Models;

namespace SCTheaterEditor.Services;

/// <summary>
/// 아이돌 필터 (다운로드·리소스 탭 공용).
/// 왼쪽부터 [전체], 유닛 열들이 가로로 놓이고, 각 유닛 아래에 멤버가 세로로 나열된다.
/// - [전체] / 유닛 체크박스: 하나라도 꺼져 있으면 전부 켜고, 모두 켜져 있으면 전부 끈다 (일부만 켜져 있으면 중간 상태)
/// - 멤버 체크박스: 아이돌 개인 필터
/// 목록이 갱신되면 Rebuild로 다시 만든다 (체크 상태는 idolId 기준으로 유지).
/// </summary>
public sealed class IdolFilterBar
{
    private readonly Panel _panel;
    private readonly CheckBox _all = new() { Content = "전체", FontWeight = FontWeights.Bold };
    private readonly Dictionary<int, CheckBox> _checks = new();
    private readonly Dictionary<int, string> _names = new();
    private readonly List<(CheckBox box, List<int> ids)> _units = new();
    private bool _suppress;

    /// <summary>체크 상태가 바뀜 (일괄 변경 시 1회만)</summary>
    public event Action? Changed;

    /// <param name="panel">유닛 열을 가로로 놓을 패널 (WrapPanel 권장)</param>
    public IdolFilterBar(Panel panel)
    {
        _panel = panel;
        _all.Click += (_, _) => SetMany(_checks.Keys, !AllOn(_checks.Keys));
    }

    /// <param name="idols">아이돌 목록 (idollist 순서)</param>
    /// <param name="units">유닛 분류</param>
    /// <param name="checkedIds">켜 둘 아이돌. null이면 전부 켬</param>
    public void Rebuild(IReadOnlyList<IdolInfo> idols, IReadOnlyList<IdolUnit> units, IEnumerable<int>? checkedIds)
    {
        var on = checkedIds?.ToHashSet();
        (_all.Parent as Panel)?.Children.Remove(_all);   // [전체]는 다시 쓰므로 이전 열에서 떼어 냄
        _panel.Children.Clear();
        _checks.Clear();
        _names.Clear();
        _units.Clear();

        // [전체] 열
        _panel.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 16, 4), Children = { _all } });

        foreach (var (unitName, members) in IdolUnits.Arrange(idols, units))
        {
            var ids = members.Select(m => m.IdolId).ToList();
            var unitBox = new CheckBox { Content = unitName, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) };
            unitBox.Click += (_, _) => SetMany(ids, !AllOn(ids));
            _units.Add((unitBox, ids));

            var column = new StackPanel { Margin = new Thickness(0, 0, 16, 4) };
            column.Children.Add(unitBox);
            foreach (var idol in members)
            {
                var cb = new CheckBox
                {
                    IsChecked = on == null || on.Contains(idol.IdolId),
                    Content = idol.IdolName,
                    Margin = new Thickness(14, 1, 0, 1),
                };
                cb.Checked += (_, _) => OnItemChanged();
                cb.Unchecked += (_, _) => OnItemChanged();
                _checks[idol.IdolId] = cb;
                _names[idol.IdolId] = idol.IdolName;
                column.Children.Add(cb);
            }
            _panel.Children.Add(column);
        }
        UpdateGroupBoxes();
    }

    public bool Allows(int idolId) => _checks.TryGetValue(idolId, out var cb) && cb.IsChecked == true;

    public List<int> CheckedIds => _checks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

    /// <summary>멤버 이름 옆 표시 (예: "(149/149)"). null이나 빈 값이면 이름만</summary>
    public void UpdateCounts(Func<int, string?> suffix)
    {
        foreach (var (id, cb) in _checks)
        {
            string? s = suffix(id);
            cb.Content = string.IsNullOrEmpty(s) ? _names[id] : $"{_names[id]} {s}";
        }
    }

    private bool AllOn(IEnumerable<int> ids) => ids.All(id => _checks[id].IsChecked == true);

    private void SetMany(IEnumerable<int> ids, bool value)
    {
        _suppress = true;
        foreach (var id in ids) _checks[id].IsChecked = value;
        _suppress = false;
        UpdateGroupBoxes();
        Changed?.Invoke();
    }

    private void OnItemChanged()
    {
        if (_suppress) return;
        UpdateGroupBoxes();
        Changed?.Invoke();
    }

    /// <summary>[전체]와 유닛 체크박스를 멤버 상태에 맞춤 (전부 켬 / 전부 끔 / 중간)</summary>
    private void UpdateGroupBoxes()
    {
        _all.IsChecked = State(_checks.Keys);
        foreach (var (box, ids) in _units) box.IsChecked = State(ids);
    }

    private bool? State(IEnumerable<int> ids)
    {
        var list = ids.ToList();
        int on = list.Count(id => _checks[id].IsChecked == true);
        return on == list.Count ? true : on == 0 ? false : null;
    }
}
