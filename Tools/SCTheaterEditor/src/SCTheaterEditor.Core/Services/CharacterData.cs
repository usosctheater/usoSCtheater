using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace SCTheaterEditor.Services;

/// <summary>캐릭터 1명 (Id = 소문자 로마자, Ko/Ja = 언어별 이름)</summary>
public sealed record CharacterInfo(string Id, string Ko, string Ja);

/// <summary>
/// 캐릭터 이름 데이터 (exe에 넣은 Resources/CharacterData.xml).
/// 게임 쪽 데이터와 같은 형식으로 두어, 나중에 게임 파일을 원본으로 바꿔도 이 코드는 그대로 쓴다
/// </summary>
public sealed class CharacterData
{
    private readonly Dictionary<string, CharacterInfo> _byId;

    public CharacterData(IEnumerable<CharacterInfo> items) =>
        _byId = items.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public int Count => _byId.Count;

    /// <summary>ID로 찾기 (대소문자 무시). 없으면 null</summary>
    public CharacterInfo? Find(string? id) => id != null && _byId.TryGetValue(id.Trim(), out var c) ? c : null;

    /// <summary>한국어 이름 (없거나 비어 있으면 null)</summary>
    public string? KoreanName(string? id) => Find(id)?.Ko is { Length: > 0 } s ? s : null;

    public static CharacterData Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException("CharacterData.xml 루트가 없습니다");
        return new CharacterData(root.Elements("Character")
            .Where(e => !string.IsNullOrWhiteSpace((string?)e.Attribute("Id")))
            .Select(e => new CharacterInfo(((string)e.Attribute("Id")!).Trim(), (string?)e.Attribute("Ko") ?? "", (string?)e.Attribute("Ja") ?? "")));
    }

    public static CharacterData LoadEmbedded()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CharacterData.xml")
                      ?? throw new InvalidOperationException("CharacterData.xml 리소스가 없습니다");
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    }
}
