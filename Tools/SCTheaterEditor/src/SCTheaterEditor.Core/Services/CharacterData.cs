using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace SCTheaterEditor.Services;

/// <summary>캐릭터 1명 (Id = 소문자 로마자, Name = 기본 언어(한국어) 이름, NameJa = 일본어 이름)</summary>
public sealed record CharacterInfo(string Id, string Name, string NameJa);   // 변경(다국어): Ko/Ja → Name/NameJa

/// <summary>
/// 캐릭터 이름 데이터 (exe에 넣은 Resources/CharacterData.xml).
/// 변경(다국어): 게임 Assets/Resources/Data/CharacterData.xml이 원본, TE는 그 복사본을 씀 (도구 독립 규칙). 속성 규칙 Name / Name_Ja (기본 언어는 접미어 없음)
/// </summary>
public sealed class CharacterData
{
    private readonly Dictionary<string, CharacterInfo> _byId;

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

    public static CharacterData Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException("CharacterData.xml 루트가 없습니다");
        return new CharacterData(root.Elements("Character")
            .Where(e => !string.IsNullOrWhiteSpace((string?)e.Attribute("Id")))
            .Select(e => new CharacterInfo(((string)e.Attribute("Id")!).Trim(),
                (string?)e.Attribute("Name") ?? "", (string?)e.Attribute("Name_Ja") ?? "")));   // 변경(다국어): 속성 이름 Ko/Ja → Name/Name_Ja
    }

    public static CharacterData LoadEmbedded()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CharacterData.xml")
                      ?? throw new InvalidOperationException("CharacterData.xml 리소스가 없습니다");
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    }
}
