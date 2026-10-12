using System.IO;
using System.Reflection;
using System.Xml.Linq;
using SCTheaterEditor.Models;

namespace SCTheaterEditor.Services;

/// <summary>유닛 1개 (이름 + 소속 idolId)</summary>
public sealed record IdolUnit(string Name, List<int> IdolIds);

/// <summary>유닛 분류 읽기: exe에 포함된 Resources\IdolUnits.xml</summary>
public static class IdolUnits
{
    public const string OtherUnitName = "기타";

    public static List<IdolUnit> LoadEmbedded()
    {
        try
        {
            using var stream = typeof(IdolUnits).Assembly.GetManifestResourceStream("IdolUnits.xml");
            if (stream == null) { AppLog.Shared.Error("유닛 분류(IdolUnits.xml)가 exe에 없음 — 유닛 없이 표시"); return new(); }
            return XDocument.Load(stream).Root!.Elements("Unit")
                .Select(e => new IdolUnit(
                    (string?)e.Attribute("Name") ?? "",
                    ((string?)e.Attribute("Idols") ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => int.TryParse(s, out int id) ? id : -1).Where(id => id >= 0).ToList()))
                .ToList();
        }
        catch (Exception ex)
        {
            // 변경: 조용히 무시하지 않고 로그 (내장 리소스가 깨진 경우 = 빌드 문제)
            AppLog.Shared.Error("유닛 분류(IdolUnits.xml) 읽기 실패 — 유닛 없이 표시", ex);
            return new();
        }
    }

    /// <summary>
    /// 아이돌 목록 기준으로 유닛을 확정: 멤버는 아이돌 목록 순서로, 목록에 없는 id는 빼고,
    /// 어느 유닛에도 없는 아이돌은 "기타"에 추가(없으면 맨 끝에 새로 만듦). 멤버가 없는 유닛은 숨김.
    /// </summary>
    public static List<(string name, List<IdolInfo> members)> Arrange(IReadOnlyList<IdolInfo> idols, IReadOnlyList<IdolUnit> units)
    {
        var byId = idols.ToDictionary(i => i.IdolId);
        var order = idols.Select((i, n) => (i.IdolId, n)).ToDictionary(x => x.IdolId, x => x.n);
        var assigned = new HashSet<int>();
        var result = new List<(string name, List<IdolInfo> members)>();

        foreach (var u in units)
        {
            var members = u.IdolIds.Where(id => byId.ContainsKey(id) && assigned.Add(id))
                                   .OrderBy(id => order[id]).Select(id => byId[id]).ToList();
            result.Add((u.Name, members));
        }

        var rest = idols.Where(i => !assigned.Contains(i.IdolId)).ToList();
        if (rest.Count > 0)
        {
            int other = result.FindIndex(r => r.name == OtherUnitName);
            if (other < 0) result.Add((OtherUnitName, rest));
            else result[other] = (OtherUnitName, result[other].members.Concat(rest).OrderBy(i => order[i.IdolId]).ToList());
        }
        return result.Where(r => r.members.Count > 0).ToList();
    }
}
