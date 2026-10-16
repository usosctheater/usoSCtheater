using SCTheaterEditor.Services;

namespace SCTheaterEditor.Script;

/// <summary>동기화 결과의 캐릭터 1명. Animation이 null이면 아직 애니메이션이 정해진 적 없음 (무대 기본값 wait)</summary>
public sealed record StageActorState(string CgKey, string Position, string? Animation);

/// <summary>스크립트의 한 줄 시점 무대 상태 (배경 키·보이는 캐릭터·안내)</summary>
public sealed record StageSnapshot(int LineIndex, string? Background, IReadOnlyList<StageActorState> Actors, IReadOnlyList<string> Notes);

/// <summary>
/// [스크립트에 무대 동기화]용 무대 상태 계산 — 게임(DialogManager.ShowLine·ProcessSetCG·ClearScreen, CGManager.SetCG)과 같은 규칙.
/// 막 첫 줄부터 선택한 줄까지 한 번 훑는다 (1,000줄이어도 1ms 안쪽):
/// - 캐릭터별 마지막 위치·애니메이션은 막 전체에서 기억 (게임은 내렸다 다시 올려도 GameObject의 위치·애니메이션이 남아 있음)
///   → 위치가 비었거나 모르는 값이면 마지막 위치, 애니메이션이 비었으면 마지막 애니메이션 (그것도 없으면 위치 center + 안내)
/// - 보이는 캐릭터·배경은 TRANSITION에서 비움 (ClearScreen)
/// - CG="none"(TEXT·SETCG) = 모두 내림, SETCG Effect="clean" = 그 캐릭터(그룹이면 멤버)만 내림
/// - CGGROUP: 게임은 막을 읽을 때 모든 CGGROUP 줄을 그룹으로 등록하고 첫 TRANSITION에서 지움(ClearScreen의 cgGroupDict.Clear)
///   → 지금 게임과 똑같이 따름 (핸드오프 미결 #7 '스크립트 동안 유지'로 바뀌면 GroupsSurviveTransition만 true로)
/// 줌·플래시백 등 효과는 반영하지 않음
/// </summary>
public static class StageStateCalculator
{
    public const string DefaultPosition = "center";

    /// <summary>CGGROUP 정의가 TRANSITION 뒤에도 남는지 (지금 게임: false)</summary>
    public const bool GroupsSurviveTransition = false;

    public static StageSnapshot Compute(IReadOnlyList<ScriptLine> lines, int index)
    {
        if (lines.Count == 0) return new StageSnapshot(-1, null, [], []);
        index = Math.Clamp(index, 0, lines.Count - 1);

        // 그룹 정의: 막 전체의 CGGROUP 줄 (게임 LoadAct와 같음)
        var allGroups = new Dictionary<string, List<(string Cg, string Pos, string Anim)>>(StringComparer.Ordinal);
        foreach (var l in lines)
        {
            if (!l.Type.Equals("CGGROUP", StringComparison.OrdinalIgnoreCase)) continue;
            string name = l.Get("Name").Split(',', 2)[0].Trim();
            if (!allGroups.TryGetValue(name, out var list)) allGroups[name] = list = new();
            list.Add((l.Get("CG"), l.Get("Position"), l.Get("Animation")));
        }
        var groups = allGroups;

        var memory = new Dictionary<string, (string? Pos, string? Anim)>(StringComparer.Ordinal);
        var visible = new List<string>();
        var notes = new List<string>();
        string? background = null;

        void Show(string key, string pos, string anim, int line)
        {
            memory.TryGetValue(key, out var mem);
            string p = pos.Trim();
            string? position = StageLayout.Get(p) != null ? p : mem.Pos;
            if (position == null)
            {
                position = DefaultPosition;
                notes.Add(p.Length == 0 ? $"{line + 1}번 줄: {key} 위치 없음 → {DefaultPosition}" : $"{line + 1}번 줄: {key} 모르는 위치 '{p}' → {DefaultPosition}");
            }
            string a = anim.Trim();
            string? animation = a.Length > 0 ? a : mem.Anim;
            memory[key] = (position, animation);
            if (!visible.Contains(key)) visible.Add(key);
        }

        IEnumerable<string> Targets(string cg) =>
            groups.TryGetValue(cg, out var members) ? members.Select(m => m.Cg) : [cg];

        for (int i = 0; i <= index; i++)
        {
            var l = lines[i];
            switch (l.Type.ToUpperInvariant())
            {
                case "TRANSITION":
                    visible.Clear();
                    background = null;
                    if (!GroupsSurviveTransition) groups = new(StringComparer.Ordinal);
                    break;

                case "BG":
                    string key = (l.Find("ResourceKey") ?? l.Find("Key") ?? "").Trim();
                    if (key.Length > 0) background = key;
                    break;

                case "TEXT":
                case "SETCG":
                    string cg = l.Get("CG").Trim();
                    if (cg.Length == 0) break;
                    bool setcg = l.Type.Equals("SETCG", StringComparison.OrdinalIgnoreCase);
                    if (setcg && l.Get("Effect").Trim().Equals("clean", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var t in Targets(cg)) visible.Remove(t);
                        break;
                    }
                    if (cg.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        visible.Clear();
                        break;
                    }
                    if (groups.TryGetValue(cg, out var members))
                        foreach (var m in members) Show(m.Cg.Trim(), m.Pos, m.Anim, i);
                    else
                        Show(cg, l.Get("Position"), l.Get("Animation"), i);
                    break;
            }
        }

        var actors = visible.Select(k => new StageActorState(k, memory[k].Pos!, memory[k].Anim)).ToList();
        return new StageSnapshot(index, background, actors, notes);
    }
}

/// <summary>라인에 적용(도장 찍기)·줄 추가에 쓰는 무대 캐릭터 정보</summary>
public sealed record StageStamp(string CgKey, string Position, string Animation, string NickName);
