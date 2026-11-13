using UsoSCTheater.Shared;   //[공유 코드 통합] 추가(5회차): AnimationRules

namespace SCTheaterEditor.Services;

/// <summary>
/// 막 XML의 Animation 속성 값(예: "anger1 face_anger arm_down" — 공백 구분, 2026-10-15 통일)과 트랙 배치 사이의 변환.
/// [공유 코드 통합] 변경(5회차): 트랙·반복·값 나누기 규칙은 공유 AnimationRules (게임 CGManager와 같은 코드). 여기는 TE 화면용(트랙 이름·우선 표시·되쓰기)만
/// </summary>
public static class AnimationScript
{
    public const int TrackCount = AnimationRules.TrackCount;   //[공유 코드 통합] 수정: 공유 상수

    /// <summary>트랙 이름 (0 기본 / 1 face / 2 lip / 3 arm·eye)</summary>
    public static readonly string[] TrackLabels = { "기본", "face", "lip", "arm·eye" };

    //[공유 코드 통합] 삭제(5회차): ForceLoopAnims → AnimationRules.IsForceLoop

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

    /// <summary>접두어 기준 트랙 — [공유 코드 통합] 수정(5회차): 공유 AnimationRules.TrackOf (eye_는 0)</summary>
    public static int TrackIndex(string name) => AnimationRules.TrackOf(name);

    /// <summary>트랙 배치 → Animation 값 (트랙 0→3 순서, 공백 구분, 빈 트랙 제외) — 변경: 쉼표 → 공백 (기존 막 파일과 통일)</summary>
    public static string Build(IEnumerable<string?> tracks) =>
        string.Join(" ", tracks.Where(a => !string.IsNullOrEmpty(a)));

    /// <summary>
    /// Animation 값 → 트랙 배치. 같은 트랙에 여러 개면 뒤의 것이 남음 (게임에서도 같은 트랙에 SetAnimation을 다시 하므로 마지막 것이 재생됨).
    /// [공유 코드 통합] 수정(5회차): 나누기·트랙은 공유 AnimationRules
    /// </summary>
    public static string?[] Parse(string? animation)
    {
        var tracks = new string?[TrackCount];
        foreach (var name in AnimationRules.Split(animation))
            tracks[AnimationRules.TrackOf(name)] = name;
        return tracks;
    }

    /// <summary>접두어 기준 트랙과 다른 트랙에 걸린 애니메이션 (디버그 방식으로 배치한 경우) — 게임에 넣으면 결과가 달라짐</summary>
    public static List<(string name, int track)> Mismatches(IReadOnlyList<string?> tracks) =>
        tracks.Select((a, i) => (name: a, track: i))
              .Where(x => !string.IsNullOrEmpty(x.name) && TrackIndex(x.name!) != x.track)
              .Select(x => (x.name!, x.track))
              .ToList();
}
