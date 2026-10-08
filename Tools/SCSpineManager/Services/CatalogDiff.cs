using System.Text;
using SCSpineManager.Models;

namespace SCSpineManager.Services;

/// <summary>
/// 이전 목록 스냅샷과 새 스냅샷 비교 (목록 갱신 시 알림용).
/// - 의상 구분: dressUuid (없으면 enzaId + 의상명)
/// - 세트 구분: 서버 경로
/// </summary>
public sealed class CatalogDiff
{
    public List<string> AddedIdols { get; } = new();
    public List<string> AddedDresses { get; } = new();
    public List<string> RemovedDresses { get; } = new();
    public List<string> ChangedDresses { get; } = new();
    /// <summary>기존 의상에 새로 생긴 세트 (예: awake 추가)</summary>
    public List<string> AddedSetsInExisting { get; } = new();
    public List<string> RemovedSets { get; } = new();
    public int AddedSetCount { get; private set; }

    public bool HasChanges =>
        AddedIdols.Count + AddedDresses.Count + RemovedDresses.Count + ChangedDresses.Count +
        AddedSetsInExisting.Count + RemovedSets.Count > 0;

    private static string DressKey(DressInfo d) => string.IsNullOrEmpty(d.DressUuid) ? $"{d.EnzaId}|{d.DressName}" : d.DressUuid;

    public static CatalogDiff Compare(CatalogSnapshot oldSnap, CatalogSnapshot newSnap)
    {
        var diff = new CatalogDiff();
        var names = newSnap.Idols.Concat(oldSnap.Idols).GroupBy(i => i.IdolId).ToDictionary(g => g.Key, g => g.First().IdolName);
        string Label(DressInfo d) => $"{names.GetValueOrDefault(d.IdolId, d.IdolId.ToString())}  [{d.DressType}] {d.DressName}  ({d.EnzaId})";

        // 아이돌
        var oldIdols = oldSnap.Idols.Select(i => i.IdolId).ToHashSet();
        diff.AddedIdols.AddRange(newSnap.Idols.Where(i => !oldIdols.Contains(i.IdolId)).Select(i => $"{i.IdolName} (idolId {i.IdolId})"));

        // 의상 (exist:true만 비교 — false→true는 추가, true→false는 삭제로 봄)
        Dictionary<string, DressInfo> Dresses(CatalogSnapshot s) => s.Dresses.Values.SelectMany(x => x)
            .Where(d => d.Exist).GroupBy(DressKey).ToDictionary(g => g.Key, g => g.First());
        var oldD = Dresses(oldSnap);
        var newD = Dresses(newSnap);

        foreach (var (key, d) in newD)
        {
            if (!oldD.TryGetValue(key, out var o)) diff.AddedDresses.Add(Label(d));
            else if (o.DressName != d.DressName || o.DressType != d.DressType)
                diff.ChangedDresses.Add($"{Label(o)}  →  [{d.DressType}] {d.DressName}");
        }
        diff.RemovedDresses.AddRange(oldD.Where(kv => !newD.ContainsKey(kv.Key)).Select(kv => Label(kv.Value)));

        // 세트
        var oldSets = SpinePlanner.Build(oldSnap).Sets.ToDictionary(s => s.SourcePath);
        var newSets = SpinePlanner.Build(newSnap).Sets.ToDictionary(s => s.SourcePath);
        var addedDressEnza = newD.Where(kv => !oldD.ContainsKey(kv.Key)).Select(kv => kv.Value.EnzaId).ToHashSet();

        foreach (var (path, s) in newSets.Where(kv => !oldSets.ContainsKey(kv.Key)))
        {
            diff.AddedSetCount++;
            if (!addedDressEnza.Contains(s.EnzaId))
                diff.AddedSetsInExisting.Add($"{s.IdolName}  {s.Targets[0].DressName}  → {s.BaseName}");
        }
        diff.RemovedSets.AddRange(oldSets.Where(kv => !newSets.ContainsKey(kv.Key))
                                         .Select(kv => $"{kv.Value.IdolName}  {kv.Value.Targets[0].DressName}  → {kv.Value.BaseName}"));
        return diff;
    }

    /// <summary>알림 창에 보여 줄 요약 한 줄</summary>
    public string Summary =>
        $"새 의상 {AddedDresses.Count}벌 · 새 세트 {AddedSetCount}개" +
        (AddedIdols.Count > 0 ? $" · 새 아이돌 {AddedIdols.Count}명" : "") +
        (ChangedDresses.Count > 0 ? $" · 정보 변경 {ChangedDresses.Count}건" : "") +
        (RemovedDresses.Count + RemovedSets.Count > 0 ? $" · 목록에서 사라짐 {RemovedDresses.Count}벌/{RemovedSets.Count}세트" : "");

    /// <summary>알림 창 본문</summary>
    public string ToDetailText()
    {
        var sb = new StringBuilder();
        void Section(string title, List<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"■ {title} ({items.Count})");
            foreach (var i in items) sb.AppendLine("  " + i);
            sb.AppendLine();
        }
        Section("새 아이돌", AddedIdols);
        Section("새 의상", AddedDresses);
        Section("기존 의상에 추가된 세트", AddedSetsInExisting);
        Section("의상 정보 변경 (이름·종류)", ChangedDresses);
        Section("목록에서 사라진 의상", RemovedDresses);
        Section("목록에서 사라진 세트", RemovedSets);
        if (RemovedDresses.Count + RemovedSets.Count > 0)
            sb.AppendLine("※ 목록에서 사라져도 이미 받은 파일은 지우지 않습니다.");
        return sb.ToString();
    }
}
