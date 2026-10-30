namespace SCTheaterEditor.Script;

/// <summary>추가(9회차): 보이스 일괄 추가 방식</summary>
public enum VoiceBatchMode
{
    /// <summary>선택한 TEXT 줄 전부 (기존 Voice는 바뀜)</summary>
    Overwrite,

    /// <summary>Voice가 빈 TEXT 줄에만</summary>
    EmptyOnly,
}

/// <summary>추가(9회차): 일괄 추가 미리보기 1줄. NewVoice가 null이면 이 줄은 바꾸지 않음 (Note = 이유)</summary>
public sealed record VoiceBatchItem(ScriptLine Line, int Number, string Name, string Text, string OldVoice, string? NewVoice, string Note)
{
    public bool Changes => NewVoice != null && NewVoice != OldVoice;
}

/// <summary>
/// 추가(9회차): TEXT에 보이스 일괄 추가 — 넣을 목록 계산 (화면 없음).
/// - 대상: 주어진 줄 중 TEXT (막 순서). Name·CharId가 모두 빈 줄(나레이션)은 건너뜀 (변경: 다국어 CharId) — 보이스도 쓰지 않음, 덮어쓰기여도 기존 Voice 그대로
/// - 덮어쓰기: 대상 전부 / 빈 줄에만: Voice가 빈 줄만 (Voice가 있는 줄은 건너뛰고 보이스를 쓰지 않음)
/// - 보이스: keys 순서대로 하나씩, 모자라면 그 뒤 줄은 '보이스 부족'
/// </summary>
public static class VoiceBatch
{
    public const string NoteNoName = "화자 없음 — 건너뜀";
    public const string NoteHasVoice = "Voice 있음 — 건너뜀";
    public const string NoteShort = "보이스 부족";

    public static IReadOnlyList<VoiceBatchItem> Plan(IReadOnlyList<(ScriptLine Line, int Number)> lines, IReadOnlyList<string> keys, VoiceBatchMode mode)
    {
        var items = new List<VoiceBatchItem>();
        int next = 0;
        foreach (var (line, number) in lines.OrderBy(l => l.Number))
        {
            if (!line.Type.Trim().Equals("TEXT", StringComparison.OrdinalIgnoreCase)) continue;
            string name = line.Get("Name").Trim();
            if (name.Length == 0) name = line.Get("CharId").Trim();   // 변경(다국어): 화자 = Name, 비면 CharId (둘 다 비면 나레이션)
            string old = line.Get("Voice").Trim();
            string text = line.Get("Text");
            string? voice = null;
            string note = "";
            if (name.Length == 0) note = NoteNoName;
            else if (mode == VoiceBatchMode.EmptyOnly && old.Length > 0) note = NoteHasVoice;
            else if (next >= keys.Count) note = NoteShort;
            else voice = keys[next++];
            items.Add(new VoiceBatchItem(line, number, name, text, old, voice, note));
        }
        return items;
    }
}
