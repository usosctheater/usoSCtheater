namespace SCTheaterEditor.Services;

/// <summary>무대 위치 1곳 (게임 CGManager.positionDict와 같은 값)</summary>
/// <param name="Name">XML Position 값 (center, wide_left …)</param>
/// <param name="X">화면 중심 기준 x (px, 1920 기준)</param>
/// <param name="Scale">스케일 배율 (3분할 1.0 = CGManager scale3 100, 5분할 0.6 = scale5 60)</param>
/// <param name="Split">3분할 / 5분할</param>
public sealed record StagePosition(string Name, double X, double Scale, int Split);

/// <summary>
/// 무대 위치 표와 배치 규칙 (2026-10-12 결정). 위치 값은 게임 CGManager.Awake와 같음 — 게임 쪽을 고치면 여기도 함께.
/// - 즉시 추가 순서: 3분할 center → left → right, 4명째부터 5분할로 전환(left→wide_left, center→wide_center, right→wide_right),
///   새 캐릭터는 wide_far_left → wide_far_right
/// - 3/5분할 전환, 드래그로 놓은 x에 가장 가까운 자리 찾기
/// 화면 좌표: 1920×1080, Spine 1단위 = 1px(스케일 100 기준), 발 위치 y = BaseY (화면 중심 기준, 위가 +)
/// </summary>
public static class StageLayout
{
    public const double ScreenWidth = 1920;
    public const double ScreenHeight = 1080;

    /// <summary>CGManager.basePosY (화면 중심 기준 px, 위가 +)</summary>
    public const double BaseY = -180;

    /// <summary>Spine 1단위가 화면 몇 px인지 (CGManager 스케일 100 = Spine 임포트 0.01 × 100) — 게임 화면과 비교해 맞춰 가는 값</summary>
    public const double UnitScale = 1.0;

    public static readonly IReadOnlyList<StagePosition> Positions =
    [
        new("left", ScreenWidth * -0.25, 1.0, 3),
        new("center", 0, 1.0, 3),
        new("right", ScreenWidth * 0.25, 1.0, 3),
        new("wide_far_left", ScreenWidth * -0.40, 0.6, 5),
        new("wide_left", ScreenWidth * -0.20, 0.6, 5),
        new("wide_center", 0, 0.6, 5),
        new("wide_right", ScreenWidth * 0.20, 0.6, 5),
        new("wide_far_right", ScreenWidth * 0.40, 0.6, 5),
    ];

    /// <summary>빈자리 채우는 순서</summary>
    public static readonly IReadOnlyList<string> ThreeFillOrder = ["center", "left", "right"];
    public static readonly IReadOnlyList<string> FiveFillOrder = ["wide_center", "wide_left", "wide_right", "wide_far_left", "wide_far_right"];

    /// <summary>3분할 → 5분할 자리 대응</summary>
    public static readonly IReadOnlyDictionary<string, string> ThreeToFive = new Dictionary<string, string>
    {
        ["left"] = "wide_left",
        ["center"] = "wide_center",
        ["right"] = "wide_right",
    };

    private static readonly Dictionary<string, StagePosition> ByName = Positions.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    public static StagePosition? Get(string? name) => name != null ? ByName.GetValueOrDefault(name) : null;

    /// <summary>지금 무대의 분할 (5분할 자리가 하나라도 있으면 5)</summary>
    public static int SplitOf(IEnumerable<string> occupied) => occupied.Any(p => Get(p)?.Split == 5) ? 5 : 3;

    /// <summary>
    /// 한 명을 더 올릴 자리. remap = 기존 캐릭터 자리 변경(4명째에 3→5분할 전환), position = 새 자리 (꽉 찼으면 null)
    /// </summary>
    public static (IReadOnlyDictionary<string, string> remap, string? position) NextSlot(IReadOnlyCollection<string> occupied)
    {
        var empty = new Dictionary<string, string>();
        if (SplitOf(occupied) == 3)
        {
            string? free = ThreeFillOrder.FirstOrDefault(p => !occupied.Contains(p));
            if (free != null) return (empty, free);
            var remap = occupied.Where(ThreeToFive.ContainsKey).ToDictionary(p => p, p => ThreeToFive[p]);
            var after = occupied.Select(p => remap.GetValueOrDefault(p, p)).ToHashSet();
            return (remap, FiveFillOrder.FirstOrDefault(p => !after.Contains(p)));
        }
        return (empty, FiveFillOrder.FirstOrDefault(p => !occupied.Contains(p)));
    }

    /// <summary>
    /// 분할 전환: 기존 자리 → 새 자리. 3→5는 대응표, 5→3은 3명 이하일 때만(왼쪽→오른쪽 순서를 지키며 가장 가까운 자리). 불가능하면 null
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ToSplit(IReadOnlyCollection<string> occupied, int split)
    {
        if (SplitOf(occupied) == split || occupied.Count == 0)
            return occupied.ToDictionary(p => p, p => p);
        if (split == 5)
            return occupied.ToDictionary(p => p, p => ThreeToFive.GetValueOrDefault(p, p));
        if (occupied.Count > 3) return null;

        var from = occupied.OrderBy(p => Get(p)?.X ?? 0).ToList();
        var targets = Positions.Where(p => p.Split == 3).OrderBy(p => p.X).ToList();
        IReadOnlyList<StagePosition>? best = null;
        double bestCost = double.MaxValue;
        foreach (var combo in Combinations(targets, from.Count))
        {
            double cost = from.Select((p, i) => Math.Abs((Get(p)?.X ?? 0) - combo[i].X)).Sum();
            if (cost < bestCost) { bestCost = cost; best = combo; }
        }
        return from.Select((p, i) => (p, best![i].Name)).ToDictionary(x => x.p, x => x.Name);
    }

    /// <summary>드래그로 놓은 x(화면 중심 기준)에 가장 가까운 자리 (지금 분할 안에서)</summary>
    public static string Snap(double x, int split) =>
        Positions.Where(p => p.Split == split).MinBy(p => Math.Abs(p.X - x))!.Name;

    /// <summary>순서를 지킨 k개 조합</summary>
    private static IEnumerable<IReadOnlyList<StagePosition>> Combinations(IReadOnlyList<StagePosition> items, int k)
    {
        if (k == 0) { yield return []; yield break; }
        for (int i = 0; i <= items.Count - k; i++)
            foreach (var rest in Combinations(items.Skip(i + 1).ToList(), k - 1))
                yield return new[] { items[i] }.Concat(rest).ToList();
    }
}
