namespace SCTheaterEditor.Script;

/// <summary>
/// 추가: 조합키 슬롯 1~9 = 줄 종류 (설정 Editor.LineSlots, 설정 탭에서 바꿈). 빈 문자열 = 빈 슬롯.
/// 저장값이 없거나 짧으면 기본값(드롭다운 순서)으로 채움 — 사용자가 써 보며 자주 쓰는 종류를 앞으로 옮기는 용도
/// </summary>
public static class LineSlots
{
    public const int Count = 9;

    /// <summary>저장값 → 슬롯 9개 (없는 자리는 기본값, 모르는 종류는 빈 슬롯, 이름은 목록 표기로 맞춤)</summary>
    public static string[] Normalize(IReadOnlyList<string>? saved, LineTypeCatalog types)
    {
        var result = new string[Count];
        for (int i = 0; i < Count; i++)
        {
            string? v = saved != null && i < saved.Count ? saved[i] : null;
            result[i] = v == null ? Default(types, i)
                      : v.Length == 0 ? ""
                      : types.Find(v)?.Id ?? "";
        }
        return result;
    }

    /// <summary>기본값: i번째(0부터) 슬롯 = 드롭다운 i번째 종류</summary>
    public static string Default(LineTypeCatalog types, int index) => index < types.Ids.Count ? types.Ids[index] : "";
}

/// <summary>추가: 조합키 가이드에 보일 슬롯 1개</summary>
public sealed record LineSlotItem(int Number, string Type)
{
    public bool IsEmpty => Type.Length == 0;
    public string Label => IsEmpty ? "—" : Type;
}
