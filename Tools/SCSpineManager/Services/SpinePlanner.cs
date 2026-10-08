using SCSpineManager.Models;

namespace SCSpineManager.Services;

/// <summary>
/// 목록 스냅샷 → 다운로드 계획. 서버 요청 없음.
/// - exist:false 의상, path가 없는 asset은 건너뜀
/// - 같은 서버 경로를 여러 의상이 공유하면 1세트로 묶고 저장 위치만 추가 (같은 폴더면 한 번만)
/// - 서로 다른 경로가 같은 저장 파일로 겹치면 Conflicts에 기록 (명명 규칙 점검)
/// </summary>
public static class SpinePlanner
{
    public static SpinePlan Build(CatalogSnapshot snap)
    {
        var plan = new SpinePlan();
        var byPath = new Dictionary<string, SpineSet>(StringComparer.Ordinal);
        // 저장 파일(상대 폴더 + 이름) → 서버 경로. 다른 경로가 같은 파일을 쓰려 하면 충돌
        var fileOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var idol in snap.Idols)
        {
            if (!snap.Dresses.TryGetValue(idol.IdolId, out var dresses)) continue;

            foreach (var dress in dresses)
            {
                plan.DressCount++;
                if (!dress.Exist)
                {
                    plan.Skipped.Add(new(idol.IdolId, dress.EnzaId, dress.DressName, "", "exist:false"));
                    continue;
                }
                if (dress.Assets == null) continue;

                var target = new SpineTarget(dress.DressType, dress.DressTypeOrder, dress.DressName,
                                             SpineNaming.RelativeFolder(dress.DressType, dress.DressName));

                foreach (var (group, assets) in dress.Assets)
                {
                    foreach (var asset in assets)
                    {
                        if (string.IsNullOrEmpty(asset.Path))
                        {
                            plan.Skipped.Add(new(idol.IdolId, dress.EnzaId, dress.DressName, $"{group}.{asset.Type}", "path 없음"));
                            continue;
                        }

                        if (!byPath.TryGetValue(asset.Path, out var set))
                        {
                            set = new SpineSet
                            {
                                SourcePath = asset.Path,
                                Group = group,
                                SpineType = asset.Type,
                                BaseName = SpineNaming.BaseName(group, asset.Type, SpineNaming.FileId(dress.EnzaId, asset.Path)),
                                IdolId = idol.IdolId,
                                IdolName = idol.IdolName,
                                EnzaId = dress.EnzaId,
                            };
                            byPath[asset.Path] = set;
                            plan.Sets.Add(set);
                        }

                        // 같은 폴더가 이미 있으면(같은 의상이 목록에 두 번) 추가하지 않음
                        if (set.Targets.Any(t => string.Equals(t.RelativeFolder, target.RelativeFolder, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        string fileKey = System.IO.Path.Combine(target.RelativeFolder, set.BaseName);
                        if (fileOwner.TryGetValue(fileKey, out var owner) && owner != asset.Path)
                        {
                            plan.Conflicts.Add($"{fileKey} ← {owner} / {asset.Path}");
                            continue;
                        }
                        fileOwner[fileKey] = asset.Path;
                        set.Targets.Add(target);
                    }
                }
            }
        }
        // 충돌로 저장 위치가 하나도 없는 세트는 계획에서 제외 (Conflicts에 남아 있음)
        plan.Sets.RemoveAll(s => s.Targets.Count == 0);
        return plan;
    }
}
