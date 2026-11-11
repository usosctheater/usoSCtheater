using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UsoSCTheater.Shared;   //[공유 코드 통합] CharIds

namespace SCTheaterEditor.Services;

/// <summary>추가(번역 모드 5회차): VOICEVOX 캐릭터 1명. Name = VOICEVOX 화면의 캐릭터 이름(TXT 화자 전환 줄), Credit = 크레딧 문구, Use = 이용 구분(OK가 아니면 경고)</summary>
public sealed record VoiceInfo(string Id, string Name, string Credit, string Use, string Note, string Terms)
{
    public const string UseOk = "OK";
    public bool IsRestricted => !Use.Equals(UseOk, StringComparison.OrdinalIgnoreCase);

    /// <summary>이용 구분 한국어</summary>
    public string UseText => Use switch
    {
        "OK" => "상업·비상업 OK",
        "CorporateInquiry" => "법인·기업이 관여하면 확인",
        "CommercialInquiry" => "상업 이용은 문의",
        "NonCommercial" => "비상업만",
        "NoGame" => "게임 작품 사용 불가",
        _ => Use,
    };

    /// <summary>상태 줄 경고 한 줄 (제한 없으면 null)</summary>
    public string? Warning => IsRestricted ? $"{Name}: {UseText}{(Note.Length > 0 ? $" — {Note}" : "")}" : null;
}

/// <summary>인물 → 화자 (Voice = VoiceInfo.Id, 비면 매칭 없음 / Style 비면 기본 스타일)</summary>
public sealed record VoiceMatchEntry(string Voice, string Style);

/// <summary>
/// 추가(번역 모드 5회차): VoiceMatch.xml (exe 옆, Git 포함 — TE 보이스 작업용 서브 툴 데이터).
/// - Voices: VOICEVOX 캐릭터·크레딧·이용 구분 (공식 리소스 policy.md 기준, 직접 고침)
/// - Matches: CharId → 화자·스타일, Default = 매칭 없는 줄의 화자
/// 설정 탭 '보이스'에서 매칭을 바꾸면 Matches만 고쳐서 다시 씀 (주석·Voices는 그대로)
/// </summary>
public sealed class VoiceMatchData
{
    public const string FileName = "VoiceMatch.xml";
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, FileName);

    private XDocument _xml;
    private readonly Dictionary<string, VoiceMatchEntry> _matches = new(StringComparer.OrdinalIgnoreCase);

    private VoiceMatchData(XDocument xml)
    {
        _xml = xml;
        Read();
    }

    public IReadOnlyList<VoiceInfo> Voices { get; private set; } = [];
    public VoiceMatchEntry Default { get; private set; } = new("", "");

    /// <summary>읽을 때 문제 (파일 없음·형식 오류) — 비면 정상</summary>
    public string Problem { get; private set; } = "";

    public static VoiceMatchData Empty() => new(new XDocument(new XElement("VoiceMatch", new XElement("Voices"), new XElement("Matches", new XElement("Default", new XAttribute("Voice", ""), new XAttribute("Style", ""))))));

    public static VoiceMatchData Parse(string xml) => new(XDocument.Parse(xml));

    /// <summary>파일 읽기. 없거나 읽지 못하면 빈 데이터 + Problem</summary>
    public static VoiceMatchData Load(string path)
    {
        if (!File.Exists(path)) { var e = Empty(); e.Problem = $"{FileName}이 없습니다 ({path})"; return e; }
        try { return new(XDocument.Load(path)); }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            var e = Empty();
            e.Problem = $"{FileName}을 읽지 못했습니다: {ex.Message}";
            return e;
        }
    }

    /// <summary>다시 읽기 (파일을 직접 고친 뒤) — 이 객체를 받아 둔 쪽은 그대로 새 내용을 봄</summary>
    public void ReloadFrom(string path)
    {
        var fresh = Load(path);
        _xml = fresh._xml;
        Read();
        Problem = fresh.Problem;
    }

    private void Read()
    {
        var root = _xml.Root ?? throw new InvalidDataException("VoiceMatch.xml 루트가 없습니다");
        Voices = root.Element("Voices")?.Elements("Voice")
                     .Select(e => new VoiceInfo(A(e, "Id"), A(e, "Name"), A(e, "Credit"), A(e, "Use") is { Length: > 0 } u ? u : VoiceInfo.UseOk, A(e, "Note"), A(e, "Terms")))
                     .Where(v => v.Id.Length > 0)
                     .GroupBy(v => v.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                     .ToList() ?? [];
        _matches.Clear();
        var m = root.Element("Matches");
        Default = m?.Element("Default") is { } d ? new(A(d, "Voice"), A(d, "Style")) : new("", "");
        foreach (var e in m?.Elements("Match") ?? [])
        {
            string id = A(e, "CharId");
            if (id.Length > 0) _matches.TryAdd(id, new(A(e, "Voice"), A(e, "Style")));
        }
    }

    private static string A(XElement e, string name) => ((string?)e.Attribute(name) ?? "").Trim();

    public VoiceInfo? FindVoice(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Voices.FirstOrDefault(v => v.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>인물의 매칭 (없으면 빈 매칭)</summary>
    public VoiceMatchEntry MatchOf(string charId) => _matches.TryGetValue(charId.Trim(), out var e) ? e : new("", "");

    /// <summary>
    /// 그 줄(첫 번째 CharId)의 화자: 매칭 → 없거나 Voice가 비면 기본 화자 → 그것도 없으면 null.
    /// 매칭의 Voice가 Voices에 없는 Id면 null (Unknown에 Id)
    /// </summary>
    public (VoiceInfo Voice, string Style)? Resolve(string? charIds, ICollection<string>? unknown = null)
    {
        string? first = CharIds.Split(charIds).FirstOrDefault();   //[공유 코드 통합] 수정: CharacterData.SplitIds → CharIds.Split
        VoiceMatchEntry e = first != null && MatchOf(first) is { Voice.Length: > 0 } m ? m : Default;
        if (e.Voice.Length == 0) return null;
        if (FindVoice(e.Voice) is { } v) return (v, e.Style);
        unknown?.Add(e.Voice);
        return null;
    }

    /// <summary>VOICEVOX 「テキスト読み込み」 화자 전환 줄: 이름 또는 이름（스타일） (VOICEVOX formatCharacterStyleName과 같은 전각 괄호)</summary>
    public static string SpeakerLine(VoiceInfo voice, string style) => style.Trim().Length == 0 ? voice.Name : $"{voice.Name}（{style.Trim()}）";

    /// <summary>매칭 바꾸기 (charId = null이면 기본 화자). 바뀌면 true — 저장은 Save</summary>
    public bool SetMatch(string? charId, string voice, string style)
    {
        voice = voice.Trim();
        style = style.Trim();
        var root = _xml.Root!;
        var matches = root.Element("Matches") ?? Add(root, new XElement("Matches"));
        XElement el;
        if (charId == null)
        {
            if (Default.Voice == voice && Default.Style == style) return false;
            el = matches.Element("Default") ?? AddFirst(matches, new XElement("Default"));
            Default = new(voice, style);
        }
        else
        {
            if (MatchOf(charId) is var old && old.Voice == voice && old.Style == style) return false;
            el = matches.Elements("Match").FirstOrDefault(e => A(e, "CharId").Equals(charId.Trim(), StringComparison.OrdinalIgnoreCase))
                 ?? Add(matches, new XElement("Match", new XAttribute("CharId", charId.Trim())));
            _matches[charId.Trim()] = new(voice, style);
        }
        el.SetAttributeValue("Voice", voice);
        el.SetAttributeValue("Style", style);
        return true;
    }

    private static XElement Add(XElement parent, XElement child) { parent.Add(child); return child; }
    private static XElement AddFirst(XElement parent, XElement child) { parent.AddFirst(child); return child; }

    /// <summary>파일로 저장 (UTF-8 BOM 없음, 들여쓰기 2칸, 주석 유지)</summary>
    public void Save(string path)
    {
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, IndentChars = "  ", NewLineChars = "\r\n" };
        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, settings)) _xml.Save(w);
        SafeFile.WriteAllBytes(path, ms.ToArray());
        Problem = "";
    }
}
