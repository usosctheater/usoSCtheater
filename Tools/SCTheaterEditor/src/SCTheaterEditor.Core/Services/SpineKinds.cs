namespace SCTheaterEditor.Services;

/// <summary>추가(리소스 탭 LD·SD): Spine 분류. LD = 커뮤니케이션 씬(대화), SD = 엔딩 씬, Other = 그 밖(picture_motion 등)</summary>
public enum SpineKind { LD, SD, Other }

/// <summary>
/// 추가(리소스 탭 LD·SD): CG 키 → LD/SD. **게임 Scripts/Character/CharacterSpineData.cs(SpineKeys)와 같은 규칙** — 키 계약(핸드오프 4절),
/// 한쪽을 바꾸면 반드시 양쪽을 함께 고침 (도구 독립 규칙상 TE가 따로 가짐).
/// 키 = {캐릭터 ID}_{타입}_{enzaId}, 타입 앞 awake_는 떼고 판단: LD = stand · stand_costume · support_stand, SD = cb · cb_costume
/// </summary>
public static class SpineKinds
{
    public const string AwakePrefix = "awake_";

    private static readonly HashSet<string> LdTypes = new(StringComparer.OrdinalIgnoreCase) { "stand", "stand_costume", "support_stand" };
    private static readonly HashSet<string> SdTypes = new(StringComparer.OrdinalIgnoreCase) { "cb", "cb_costume" };

    /// <summary>키 가운데 타입 (첫 _ 뒤 ~ 마지막 _ 앞, 마지막 뒤가 숫자일 때). 형식이 맞지 않으면 null</summary>
    public static string? TypeOf(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        int first = key.IndexOf('_'), last = key.LastIndexOf('_');
        if (first <= 0 || last <= first + 1 || last >= key.Length - 1) return null;
        if (!key[(last + 1)..].All(char.IsDigit)) return null;
        return key[(first + 1)..last];
    }

    public static SpineKind KindOfType(string? type)
    {
        if (string.IsNullOrEmpty(type)) return SpineKind.Other;
        string t = type.StartsWith(AwakePrefix, StringComparison.OrdinalIgnoreCase) ? type[AwakePrefix.Length..] : type;
        if (LdTypes.Contains(t)) return SpineKind.LD;
        if (SdTypes.Contains(t)) return SpineKind.SD;
        return SpineKind.Other;
    }

    public static SpineKind Of(string? cgKey) => KindOfType(TypeOf(cgKey));
}
