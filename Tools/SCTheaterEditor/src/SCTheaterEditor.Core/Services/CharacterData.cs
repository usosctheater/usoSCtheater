using System.IO;
// 삭제(번역 모드 2회차): using System.Reflection; (exe 내장 리소스를 더 읽지 않음)
using System.Xml.Linq;

namespace SCTheaterEditor.Services;

/// <summary>캐릭터 1명 (Id = 소문자 로마자, Name = 기본 언어(한국어) 이름, NameJa = 일본어 이름)</summary>
public sealed record CharacterInfo(string Id, string Name, string NameJa);   // 변경(다국어): Ko/Ja → Name/NameJa

/// <summary>
/// 캐릭터 이름 데이터.
/// 변경(다국어): 게임 Assets/Resources/Data/CharacterData.xml이 원본, TE는 그 복사본을 씀 (도구 독립 규칙). 속성 규칙 Name / Name_Ja (기본 언어는 접미어 없음)
/// 변경(번역 모드 2회차): 복사본 위치 exe 내장 → exe 옆 Data\CharacterData.xml (GameData가 읽음)
/// </summary>
public sealed class CharacterData
{
    private Dictionary<string, CharacterInfo> _byId;   // 변경(번역 모드 2회차): readonly 삭제 (ReplaceWith)

    public CharacterData(IEnumerable<CharacterInfo> items) =>
        _byId = items.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public int Count => _byId.Count;

    /// <summary>ID로 찾기 (대소문자 무시). 없으면 null</summary>
    public CharacterInfo? Find(string? id) => id != null && _byId.TryGetValue(id.Trim(), out var c) ? c : null;

    /// <summary>한국어(기본 언어) 이름 (없거나 비어 있으면 null)</summary>
    public string? KoreanName(string? id) => Find(id)?.Name is { Length: > 0 } s ? s : null;   // 변경(다국어): .Ko → .Name

    /// <summary>추가(다국어): "a, b" → [a, b] (앞뒤 공백·빈 항목 제외) — 막 XML CharId 값 (게임 CharacterDatabase.SplitIds와 같은 규칙)</summary>
    public static IEnumerable<string> SplitIds(string? ids) =>
        (ids ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);

    /// <summary>추가(다국어): CharId(쉼표로 여러 명)의 한국어 이름을 ", "로 이어 붙임. DB에 없는 ID는 그대로, 비면 null</summary>
    public string? SpeakerNames(string? ids)
    {
        var names = SplitIds(ids).Select(id => KoreanName(id) ?? id).ToList();
        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    // ------------------------------------------------------------------ 추가(번역 모드 3회차): 언어별 이름

    /// <summary>id → (언어 코드 소문자 → 이름) — Name_{언어} 속성 전부 (언어가 늘어도 코드 수정 없음)</summary>
    private Dictionary<string, Dictionary<string, string>> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>그 언어의 표시 이름 = Name_{언어} → 없으면 Name (게임 CharacterDatabase와 같은 대체 규칙). 기본 언어면 Name. 캐릭터가 없으면 null</summary>
    public string? LocalizedName(string? id, LanguageInfo language)
    {
        if (Find(id) is not { } c) return null;
        if (!language.IsDefault && _names.TryGetValue(c.Id, out var byLang) && byLang.TryGetValue(language.Code, out var n) && n.Length > 0) return n;
        return c.Name.Length > 0 ? c.Name : null;
    }

    /// <summary>CharId(쉼표로 여러 명)의 그 언어 이름을 언어별 구분자로 이어 붙임. DB에 없는 ID는 그대로, 비면 null</summary>
    public string? SpeakerNames(string? ids, LanguageInfo language)
    {
        var names = SplitIds(ids).Select(id => LocalizedName(id, language) ?? id).ToList();
        return names.Count > 0 ? string.Join(language.ListSeparator, names) : null;
    }

    public static CharacterData Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException("CharacterData.xml 루트가 없습니다");
        var elems = root.Elements("Character").Where(e => !string.IsNullOrWhiteSpace((string?)e.Attribute("Id"))).ToList();
        var data = new CharacterData(elems
            .Select(e => new CharacterInfo(((string)e.Attribute("Id")!).Trim(),
                (string?)e.Attribute("Name") ?? "", (string?)e.Attribute("Name_Ja") ?? "")));   // 변경(다국어): 속성 이름 Ko/Ja → Name/Name_Ja
        // 추가(번역 모드 3회차): Name_{언어} 전부 (FullName_ 등은 제외 — 이름 속성은 정확히 Name_ 로 시작)
        foreach (var e in elems)
        {
            string id = ((string)e.Attribute("Id")!).Trim();
            if (data._names.ContainsKey(id)) continue;   // 중복 id는 첫 항목 (Find와 같음)
            var byLang = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in e.Attributes())
                if (a.Name.LocalName.StartsWith("Name_", StringComparison.OrdinalIgnoreCase) && a.Name.LocalName.Length > 5)
                    byLang[a.Name.LocalName[5..].ToLowerInvariant()] = a.Value;
            data._names[id] = byLang;
        }
        return data;
    }

    /// <summary>추가(번역 모드 2회차): 내용을 다른 데이터로 바꿈 — 이 객체를 받아 둔 쪽은 그대로 새 내용을 봄 ([게임 데이터 다시 복사])</summary>
    internal void ReplaceWith(CharacterData other)
    {
        _byId = other._byId;
        _names = other._names;   // 추가(번역 모드 3회차)
    }

    // 삭제(번역 모드 2회차): LoadEmbedded() — exe 내장 복사본 대신 GameData가 Data\ 파일을 읽음
}
