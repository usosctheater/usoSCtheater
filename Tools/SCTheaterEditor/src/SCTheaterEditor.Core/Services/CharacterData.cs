using UsoSCTheater.Shared;   //[공유 코드 통합] IdTable · LocalizedNames · CharIds · LanguageInfo

namespace SCTheaterEditor.Services;

/// <summary>
/// 캐릭터 이름 데이터 (게임 Assets/Resources/Data/CharacterData.xml 복사본 — exe 옆 Data\, GameData가 읽음. 도구 독립 규칙).
/// [공유 코드 통합] 변경: 해석·이름 대체 순서·CharId 나누기는 공유 라이브러리 — 게임 CharacterDatabase와 같은 이름이 나옴
/// (삭제: record CharacterInfo(Id, Name, NameJa), KoreanName, LocalizedName, 자체 SplitIds·Name_* 사전)
/// </summary>
public sealed class CharacterData
{
    private Dictionary<string, IdEntry> _byId = new(StringComparer.OrdinalIgnoreCase);
    private List<IdEntry> _all = [];
    private LanguageInfo _default = new("ko", "한국어", true);

    /// <summary>전체 (CharacterData.xml 순서 — 설정 탭 '보이스' 목록)</summary>
    public IReadOnlyList<IdEntry> All => _all;

    public int Count => _byId.Count;

    /// <summary>기본 언어 (LanguageData) — 스크립트 표의 흐린 화자 이름 등</summary>
    public LanguageInfo DefaultLanguage => _default;

    /// <summary>ID로 찾기 (대소문자 무시). 없으면 null</summary>
    public IdEntry? Find(string? id) => id != null && _byId.TryGetValue(id.Trim(), out var c) ? c : null;

    /// <summary>그 언어의 표시 이름 (게임과 같은 대체 순서: Name_{언어} → FullName_{언어} → Name → FullName → Id). DB에 없으면 id 그대로</summary>
    public string Name(string id, LanguageInfo language) =>
        Find(id) is { } c ? LocalizedNames.Get(c.Attrs, c.Id, language, _default, full: false) : id;

    /// <summary>CharId(쉼표로 여러 명) → 그 언어 이름을 언어 구분자로 이어 붙임 (게임 GetSpeakerNames와 같음). 비면 null. language가 null이면 기본 언어</summary>
    public string? SpeakerNames(string? ids, LanguageInfo? language = null)
    {
        var lang = language ?? _default;
        var names = CharIds.Split(ids).Select(id => Name(id, lang)).ToList();
        return names.Count > 0 ? CharIds.Join(names, lang) : null;
    }

    /// <summary>CharacterData.xml 글자 → 데이터 (문제는 warnings, 예외 없음 — 비었거나 깨지면 빈 데이터)</summary>
    public static CharacterData Parse(string? xml, LanguageInfo defaultLanguage, ICollection<string> warnings)
    {
        var data = new CharacterData { _default = defaultLanguage, _all = IdTable.Parse(xml, "Character", warnings) };
        foreach (var e in data._all) data._byId[e.Id] = e;
        return data;
    }

    /// <summary>내용을 다른 데이터로 바꿈 — 이 객체를 받아 둔 쪽은 그대로 새 내용을 봄 ([게임 데이터 다시 복사])</summary>
    internal void ReplaceWith(CharacterData other)
    {
        _byId = other._byId;
        _all = other._all;
        _default = other._default;
    }
}
