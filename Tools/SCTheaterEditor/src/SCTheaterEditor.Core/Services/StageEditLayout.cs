using UsoSCTheater.Shared;

namespace SCTheaterEditor.Services;

/// <summary>
/// TE 무대 편집 계산 (스테이지 레이아웃 5회차, 옛 StageLayout 대체) — 게임 규칙(자리·자동 배치)은 공유 StagePlanner, 여기는 에디터에서 손으로 놓을 때만.
/// 자리 = "N-i" (공유 StageLayout 표기), 숫자는 StageData 표. 캐릭터는 Id로 구분 (같은 자리 두 명도 다룸)
/// - 추가: 지금 분할의 왼쪽 빈 칸, 없으면 무대 전원을 N+1로 (왼쪽→오른쪽 순서 유지, 새 캐릭터는 오른쪽 끝), N+1 행이 없으면 null(꽉 참)
/// - 분할 전환: 인원 ≤ 새 N일 때만, 순서를 지키며 X가 가장 가까운 칸 (이전 3/5 전환 계산을 일반화)
/// - 드래그: 지금 분할 안에서 가장 가까운 칸
/// </summary>
public static class StageEditLayout
{
    public static (int N, int I) Parse(string? pos) => StageLayout.TryParsePos(pos, out int n, out int i) ? (n, i) : (0, 0);

    /// <summary>지금 분할 (캐릭터가 없으면 0)</summary>
    public static int SplitOf(IEnumerable<string> positions) => positions.Select(p => Parse(p).N).DefaultIfEmpty(0).Max();

    /// <summary>화면 배치 (X·Y = 화면 중심 기준 px, Scale = 1.0 기준). 표에 없는 분할이면 null</summary>
    public static (double X, double Y, double Scale)? Place(StageTable table, string? pos)
    {
        var (n, i) = Parse(pos);
        return table.TryGetRow(n, out var row) ? (row.SlotX(i), row.BaseY, row.Scale / 100) : null;
    }

    /// <summary>한 명을 더 올릴 자리. Remap = 기존 캐릭터 자리 변경(Id → 새 자리, 분할을 늘릴 때), Position = 새 자리 (꽉 찼으면 null)</summary>
    public static (IReadOnlyDictionary<int, string> Remap, string? Position) NextSlot(StageTable table, IReadOnlyList<(int Id, string Pos)> occupied)
    {
        var empty = new Dictionary<int, string>();
        int n = SplitOf(occupied.Select(o => o.Pos));
        var used = occupied.Select(o => Parse(o.Pos).I).ToHashSet();
        for (int i = 1; i <= n; i++)
            if (!used.Contains(i)) return (empty, StageLayout.FormatPos(n, i));

        int next = Math.Max(n, occupied.Count) + 1;
        if (!table.TryGetRow(next, out _)) return (empty, null);
        var remap = occupied.OrderBy(o => Parse(o.Pos).I)
                            .Select((o, k) => (o.Id, Pos: StageLayout.FormatPos(next, k + 1)))
                            .ToDictionary(x => x.Id, x => x.Pos);
        return (remap, StageLayout.FormatPos(next, occupied.Count + 1));
    }

    /// <summary>분할 전환: 기존 자리 → 새 자리 (Id → "N-i"). 인원이 새 N보다 많거나 표에 없는 N이면 null</summary>
    public static IReadOnlyDictionary<int, string>? ToSplit(StageTable table, IReadOnlyList<(int Id, string Pos)> occupied, int split)
    {
        if (!table.TryGetRow(split, out var row) || occupied.Count > split) return null;
        var from = occupied.OrderBy(o => Place(table, o.Pos)?.X ?? 0).ToList();
        var targets = Enumerable.Range(1, split).ToList();
        IReadOnlyList<int>? best = null;
        double bestCost = double.MaxValue;
        foreach (var combo in Combinations(targets, from.Count))
        {
            double cost = from.Select((o, k) => Math.Abs((Place(table, o.Pos)?.X ?? 0) - row.SlotX(combo[k]))).Sum();
            if (cost < bestCost) { bestCost = cost; best = combo; }
        }
        return from.Select((o, k) => (o.Id, Pos: StageLayout.FormatPos(split, best![k]))).ToDictionary(x => x.Id, x => x.Pos);
    }

    /// <summary>드래그로 놓은 x(화면 중심 기준)에 가장 가까운 칸 (지금 분할 안에서). 표에 없으면 null</summary>
    public static string? Snap(StageTable table, double x, int split) =>
        table.TryGetRow(split, out var row)
            ? StageLayout.FormatPos(split, Enumerable.Range(1, split).MinBy(i => Math.Abs(row.SlotX(i) - x)))
            : null;

    /// <summary>순서를 지킨 k개 조합 (이전 StageLayout과 같음)</summary>
    private static IEnumerable<IReadOnlyList<int>> Combinations(IReadOnlyList<int> items, int k)
    {
        if (k == 0) { yield return []; yield break; }
        for (int i = 0; i <= items.Count - k; i++)
            foreach (var rest in Combinations(items.Skip(i + 1).ToList(), k - 1))
                yield return new[] { items[i] }.Concat(rest).ToList();
    }
}
