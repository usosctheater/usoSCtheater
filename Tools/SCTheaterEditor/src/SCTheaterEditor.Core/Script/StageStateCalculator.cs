using System.Globalization;
using UsoSCTheater.Shared;   //[스테이지 레이아웃] StagePlanner · StageTable

namespace SCTheaterEditor.Script;

/// <summary>
/// 동기화 결과의 캐릭터 1명. Position = "N-i", Animation이 null이면 아직 애니메이션이 정해진 적 없음 (무대 기본값 wait),
/// Zoom = 효과 층 배율 (게임 줌의 끝난 상태)   //[스테이지 레이아웃] Zoom 추가
/// </summary>
public sealed record StageActorState(string CgKey, string Position, string? Animation, double Zoom = 1);

/// <summary>
/// 스크립트의 한 줄 시점 무대 상태 (배경 키·보이는 캐릭터·안내).
/// 추가(9회차): LineActors = 선택한 줄(TEXT·SETCG)이 올린 CG 키 — 그룹이면 멤버 전원(그룹 순서), 없으면 빈 목록 (null = 모름, 직접 만든 스냅샷)
/// [스테이지 레이아웃] Actors 순서 = 뒤 → 앞 (게임 정렬 순서와 같음)
/// </summary>
public sealed record StageSnapshot(int LineIndex, string? Background, IReadOnlyList<StageActorState> Actors, IReadOnlyList<string> Notes,
                                   IReadOnlyList<string>? LineActors = null);

/// <summary>
/// [스크립트에 무대 동기화]용 무대 상태 계산 — 게임(DialogManager.ShowLine·ProcessSetCG·ClearScreen, CGManager.SetCG·SetZoom)과 같은 규칙.
/// 막 첫 줄부터 선택한 줄까지 한 번 훑는다 (1,000줄이어도 1ms 안쪽):
/// - [스테이지 레이아웃] 변경: 자리는 공유 StagePlanner (게임·카탈로그 동기화와 같은 코드) — 이전의 '캐릭터별 마지막 위치 기억'·center 대체는 삭제
///   (내렸다 다시 올린 캐릭터의 빈 Position = 자동 배치, 게임과 같음). Pos 오류·분할 불일치 = 그 줄에서 건너뜀 + 안내
/// - 애니메이션은 캐릭터별로 막 전체에서 기억 (게임은 내렸다 다시 올려도 오브젝트에 남음)
/// - [스테이지 레이아웃] 줌: CG가 있는 TEXT·SETCG의 Effect="zoom" + Value(기본 1)를 기억, 다음 CG 줄에서도 유지,
///   SETCG clean·TRANSITION·CG="none"이면 1. CGGROUP 줌은 게임처럼 반영하지 않음(안내). 끝난 상태만 (Duration 무시)
/// - 보이는 캐릭터·배경은 TRANSITION에서 비움 (ClearScreen)
/// - CG="none"(TEXT·SETCG) = 모두 내림, SETCG Effect="clean" = 그 캐릭터(그룹이면 멤버)만 내림
/// - CGGROUP: 게임은 막을 읽을 때 모든 CGGROUP 줄을 그룹으로 등록하고 막 동안 유지 (TRANSITION에서 지우지 않음, 새 막 LoadAct에서만 초기화)
///   → 게임과 똑같이 따름 (핸드오프 미결 #7 '스크립트 동안 유지', 2026-10-26)
/// </summary>
public static class StageStateCalculator
{
    //[스테이지 레이아웃] 삭제: DefaultPosition ("center")

    public static StageSnapshot Compute(IReadOnlyList<ScriptLine> lines, int index, StageTable table)   //[스테이지 레이아웃] table 추가
    {
        if (lines.Count == 0) return new StageSnapshot(-1, null, [], []);
        index = Math.Clamp(index, 0, lines.Count - 1);

        // 그룹 정의: 막 전체의 CGGROUP 줄 (게임 LoadAct와 같음, 막 동안 유지)
        var groups = new Dictionary<string, List<(string Cg, string Pos, string Anim)>>(StringComparer.OrdinalIgnoreCase);   //[공유 코드 통합] 수정: 그룹 이름 대소문자 무시
        foreach (var l in lines)
        {
            if (!l.Type.Equals("CGGROUP", StringComparison.OrdinalIgnoreCase)) continue;
            string name = l.Get("GroupId").Trim();   // 변경(다국어): Name 쉼표 분리 → GroupId
            if (name.Length == 0) continue;   //[공유 코드 통합] 추가: GroupId 없는 줄 무시 (게임과 같음)
            if (!groups.TryGetValue(name, out var list)) groups[name] = list = new();
            list.Add((l.Get("CG"), l.Get("Position"), l.Get("Animation")));
        }

        var planner = new StagePlanner(table);   //[스테이지 레이아웃]
        var anims = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var zooms = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);   //[스테이지 레이아웃]
        var notes = new List<string>();
        var warnings = new List<string>();
        string? background = null;
        var lineActors = new List<string>();   // 추가(9회차): 선택한 줄이 올린 캐릭터

        for (int i = 0; i <= index; i++)
        {
            var l = lines[i];
            switch (l.Type.ToUpperInvariant())
            {
                case "TRANSITION":
                    planner.Clear();
                    zooms.Clear();
                    background = null;   // 그룹 정의는 유지 (미결 #7)
                    break;

                case "BG":
                    string key = (l.Find("ResourceKey") ?? "").Trim();   //[공유 코드 통합] 수정: Key 대체 삭제
                    if (key.Length > 0) background = key;
                    break;

                case "TEXT":
                case "SETCG":
                    string cg = l.Get("CG").Trim();
                    if (cg.Length == 0) break;   // 게임: CG가 빈 줄의 줌은 무시
                    bool setcg = l.Type.Equals("SETCG", StringComparison.OrdinalIgnoreCase);
                    bool isGroup = groups.TryGetValue(cg, out var members);
                    var targets = isGroup
                        ? members!.Select(m => (Cg: m.Cg.Trim(), m.Pos, m.Anim)).ToList()
                        : [(cg, l.Get("Position"), l.Get("Animation"))];
                    if (setcg && l.Get("Effect").Trim().Equals("clean", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var t in targets)
                        {
                            planner.Remove(t.Cg);
                            zooms.Remove(t.Cg);
                        }
                        break;
                    }
                    if (cg.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        planner.Clear();
                        zooms.Clear();
                        break;
                    }

                    planner.StartLine();
                    foreach (var t in targets)
                    {
                        if (t.Pos.Trim().Length == 0) notes.Add($"{i + 1}번 줄: {t.Cg} Position 없음 (자동 배치)");
                        warnings.Clear();
                        bool shown = planner.Show(t.Cg, t.Pos, warnings);
                        foreach (var w in warnings) notes.Add($"{i + 1}번 줄: {w}");
                        if (!shown) continue;
                        if (t.Anim.Trim().Length > 0) anims[t.Cg] = t.Anim.Trim();
                        if (i == index && !lineActors.Contains(t.Cg)) lineActors.Add(t.Cg);   // 추가(9회차)
                    }

                    //[스테이지 레이아웃] 줌 (끝난 상태)
                    if (l.Get("Effect").Trim().Equals("zoom", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isGroup) notes.Add($"{i + 1}번 줄: CGGROUP 줌은 게임에서 반영하지 않음 (미결 #8)");
                        else if (planner.Contains(cg))
                            zooms[cg] = double.TryParse(l.Get("Value"), NumberStyles.Float, CultureInfo.InvariantCulture, out double z) ? z : 1;
                    }
                    break;
            }
        }

        var actors = planner.Placements()   //[스테이지 레이아웃] 뒤 → 앞
            .Select(p => new StageActorState(p.Key, p.Pos, anims.GetValueOrDefault(p.Key), zooms.GetValueOrDefault(p.Key, 1)))
            .ToList();
        return new StageSnapshot(index, background, actors, notes, lineActors);
    }
}

/// <summary>라인에 적용(도장 찍기)·줄 추가에 쓰는 무대 캐릭터 정보</summary>
public sealed record StageStamp(string CgKey, string Position, string Animation, string NickName);
