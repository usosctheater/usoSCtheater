namespace SCTheaterEditor.Services;

/// <summary>
/// 막 XML의 Animation 속성 값(예: "anger1 face_anger arm_down" — 공백 구분, 2026-10-15 통일)과 트랙 배치 사이의 변환.
/// 게임(CGManager)과 같은 규칙을 한곳에 모아 둔 것 — 뷰어 재생, 이후 씬 구성 탭의 스크립트 라인 내보내기에서 함께 사용.
/// </summary>
public static class AnimationScript
{
    public const int TrackCount = 4;

    /// <summary>트랙 이름 (0 기본 / 1 face / 2 lip / 3 arm·eye)</summary>
    public static readonly string[] TrackLabels = { "기본", "face", "lip", "arm·eye" };

    /// <summary>무한 반복 애니메이션 (CGManager.FORCE_LOOP_ANIMS)</summary>
    public static readonly HashSet<string> ForceLoopAnims = new() { "wait", "blank" };

    /// <summary>알려진 접두어 (트랙 0의 우선 그룹 = 이 접두어가 없는 것)</summary>
    public static readonly string[] KnownPrefixes = { "face_", "lip_", "arm_", "eye_" };

    /// <summary>트랙별 우선 표시 접두어 (예전 게임 SpineDebugManager에서 가져온 규칙: 3은 arm_ 다음 eye_)</summary>
    private static readonly Dictionary<int, string[]> TrackPriorityPrefixes = new()
    {
        { 1, new[] { "face_" } },
        { 2, new[] { "lip_" } },
        { 3, new[] { "arm_", "eye_" } },
    };

    /// <summary>애니메이션 목록에서 이 트랙의 우선 그룹인지 (이전 뷰어 탭 코드에서 이동)</summary>
    public static bool IsPriority(string name, int track)
    {
        if (track == 0) return !KnownPrefixes.Any(p => name.StartsWith(p));
        return TrackPriorityPrefixes.TryGetValue(track, out var ps) && ps.Any(p => name.StartsWith(p));
    }

    /// <summary>우선 그룹 안의 순서 (3번 트랙: arm_ → eye_)</summary>
    public static int PriorityOrder(string name, int track) =>
        TrackPriorityPrefixes.TryGetValue(track, out var ps) ? Math.Max(0, Array.FindIndex(ps, p => name.StartsWith(p))) : 0;

    /// <summary>접두어 기준 트랙 (CGManager.GetTrackIndex와 동일 — eye_는 0)</summary>
    public static int TrackIndex(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.StartsWith("face_")) return 1;
        if (lower.StartsWith("lip_")) return 2;
        if (lower.StartsWith("arm_")) return 3;
        return 0;
    }

    /// <summary>트랙 배치 → Animation 값 (트랙 0→3 순서, 공백 구분, 빈 트랙 제외) — 변경: 쉼표 → 공백 (기존 막 파일과 통일)</summary>
    public static string Build(IEnumerable<string?> tracks) =>
        string.Join(" ", tracks.Where(a => !string.IsNullOrEmpty(a)));

    /// <summary>
    /// Animation 값 → 트랙 배치. 쓰는 구분자는 공백이지만 읽기는 CGManager와 같이 공백·쉼표 모두, 같은 트랙에 여러 개면 뒤의 것이 남음
    /// (게임에서도 같은 트랙에 SetAnimation을 다시 하므로 마지막 것이 재생됨)
    /// </summary>
    public static string?[] Parse(string? animation)
    {
        var tracks = new string?[TrackCount];
        if (string.IsNullOrWhiteSpace(animation)) return tracks;
        foreach (var name in animation.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            tracks[TrackIndex(name.Trim())] = name.Trim();
        return tracks;
    }

    /// <summary>접두어 기준 트랙과 다른 트랙에 걸린 애니메이션 (디버그 방식으로 배치한 경우) — 게임에 넣으면 결과가 달라짐</summary>
    public static List<(string name, int track)> Mismatches(IReadOnlyList<string?> tracks) =>
        tracks.Select((a, i) => (name: a, track: i))
              .Where(x => !string.IsNullOrEmpty(x.name) && TrackIndex(x.name!) != x.track)
              .Select(x => (x.name!, x.track))
              .ToList();
}
