namespace SCSpineManager.Services;

/// <summary>
/// 막 XML의 Animation 속성 값(예: "anger1, face_anger, arm_down")과 트랙 배치 사이의 변환.
/// 게임(CGManager)과 같은 규칙을 한곳에 모아 둔 것 — 뷰어 재생, 이후 씬 구성 탭의 스크립트 라인 내보내기에서 함께 사용.
/// </summary>
public static class AnimationScript
{
    public const int TrackCount = 4;

    /// <summary>트랙 이름 (0 기본 / 1 face / 2 lip / 3 arm·eye)</summary>
    public static readonly string[] TrackLabels = { "기본", "face", "lip", "arm·eye" };

    /// <summary>무한 반복 애니메이션 (CGManager.FORCE_LOOP_ANIMS)</summary>
    public static readonly HashSet<string> ForceLoopAnims = new() { "wait", "blank" };

    /// <summary>접두어 기준 트랙 (CGManager.GetTrackIndex와 동일 — eye_는 0)</summary>
    public static int TrackIndex(string name)
    {
        string lower = name.ToLowerInvariant();
        if (lower.StartsWith("face_")) return 1;
        if (lower.StartsWith("lip_")) return 2;
        if (lower.StartsWith("arm_")) return 3;
        return 0;
    }

    /// <summary>트랙 배치 → Animation 값 (트랙 0→3 순서, 쉼표 구분, 빈 트랙 제외)</summary>
    public static string Build(IEnumerable<string?> tracks) =>
        string.Join(", ", tracks.Where(a => !string.IsNullOrEmpty(a)));

    /// <summary>
    /// Animation 값 → 트랙 배치. 구분자는 CGManager와 같이 공백·쉼표, 같은 트랙에 여러 개면 뒤의 것이 남음
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
