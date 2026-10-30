using System.Text;

namespace SCTheaterEditor.Script;

/// <summary>
/// 막 XML의 &lt;Line&gt; 1개. 속성은 파일에 적힌 순서 그대로 보관하고, 모르는 속성·종류도 지우지 않는다.
/// - 읽은 뒤 속성이 그대로면 저장할 때 원래 줄(RawText)을 그대로 써서 파일 모양이 바뀌지 않음
/// - 값 변경은 ActDocument를 거쳐야 되돌리기에 기록됨 (여기의 SetValue는 internal)
/// </summary>
public sealed class ScriptLine
{
    /// <summary>새로 넣는 속성의 자리 기준 (기존 막 파일의 순서). 목록에 없는 속성은 맨 뒤</summary>
    public static readonly IReadOnlyList<string> AttributeOrder =
    [
        "Type", "GroupId", "CharId", "Name", "Name_Ja", "Text", "Text_Ja", "Voice", "CG", "Position", "Animation", "Key", "ResourceKey",   // 변경(다국어): GroupId·CharId·Name_Ja·Text_Ja 자리
        "Value", "Track", "Volume", "Effect", "SE", "Duration", "SpeakerType",
    ];

    private readonly List<KeyValuePair<string, string>> _attrs;
    private readonly KeyValuePair<string, string>[] _original;

    public ScriptLine(IEnumerable<KeyValuePair<string, string>> attributes, string? rawText = null)
    {
        _attrs = attributes.ToList();
        _original = _attrs.ToArray();
        RawText = rawText;
    }

    /// <summary>새 라인 (예: Create(("Type", "TEXT"), ("Name", "")))</summary>
    public static ScriptLine Create(params (string Name, string Value)[] attributes) =>
        new(attributes.Select(a => new KeyValuePair<string, string>(a.Name, a.Value)));

    /// <summary>속성 (파일 순서)</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Attributes => _attrs;

    /// <summary>읽을 때의 원래 줄 (줄바꿈 제외). 원래 모양을 지킬 수 없는 파일이거나 새 라인이면 null</summary>
    public string? RawText { get; }

    /// <summary>이 라인 앞에 있던 Line이 아닌 줄 (주석·빈 줄 등) — 저장할 때 그대로 다시 씀</summary>
    public IReadOnlyList<string>? LeadingRaw { get; internal set; }

    /// <summary>속성이 바뀜 (되돌리기 포함)</summary>
    public event Action<ScriptLine>? Changed;

    public string Type => Get("Type");

    /// <summary>속성 값 (없으면 ""). 이름은 게임(XmlNode 속성 조회)과 같이 대소문자 구분</summary>
    public string Get(string name) => Find(name) ?? "";

    /// <summary>속성 값 (없으면 null)</summary>
    public string? Find(string name)
    {
        int i = IndexOf(name);
        return i < 0 ? null : _attrs[i].Value;
    }

    public int IndexOf(string name)
    {
        for (int i = 0; i < _attrs.Count; i++)
            if (string.Equals(_attrs[i].Key, name, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>읽은 뒤 속성(값·순서)이 바뀌었는지 — 되돌려서 원래와 같아지면 false</summary>
    public bool IsModified
    {
        get
        {
            if (_attrs.Count != _original.Length) return true;
            for (int i = 0; i < _attrs.Count; i++)
                if (!string.Equals(_attrs[i].Key, _original[i].Key, StringComparison.Ordinal) ||
                    !string.Equals(_attrs[i].Value, _original[i].Value, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>값 설정. null이면 속성 삭제. 없던 속성은 insertAt(되돌리기: 원래 자리) 또는 AttributeOrder 기준 자리에 넣음</summary>
    internal void SetValue(string name, string? value, int insertAt = -1)
    {
        int i = IndexOf(name);
        if (value == null)
        {
            if (i < 0) return;
            _attrs.RemoveAt(i);
        }
        else if (i >= 0)
        {
            if (string.Equals(_attrs[i].Value, value, StringComparison.Ordinal)) return;
            _attrs[i] = new(name, value);
        }
        else
        {
            int at = insertAt >= 0 ? Math.Min(insertAt, _attrs.Count) : DefaultIndex(name);
            _attrs.Insert(at, new(name, value));
        }
        Changed?.Invoke(this);
    }

    private int DefaultIndex(string name)
    {
        int rank = Rank(name);
        for (int i = 0; i < _attrs.Count; i++)
            if (Rank(_attrs[i].Key) > rank) return i;
        return _attrs.Count;
    }

    private static int Rank(string name)
    {
        for (int i = 0; i < AttributeOrder.Count; i++)
            if (string.Equals(AttributeOrder[i], name, StringComparison.Ordinal)) return i;
        return int.MaxValue;
    }

    /// <summary>저장할 줄: 바뀌지 않았으면 원래 줄, 바뀌었으면 기존 파일 모양(탭 + 한 줄)으로 새로 씀</summary>
    public string ToXmlLine() => RawText != null && !IsModified ? RawText : Format();

    /// <summary>탭 들여쓰기 + &lt;Line 속성="값" …/&gt;</summary>
    public string Format()
    {
        var sb = new StringBuilder("\t<Line");
        foreach (var a in _attrs)
            sb.Append(' ').Append(a.Key).Append("=\"").Append(ActXmlFormat.Escape(a.Value)).Append('"');
        return sb.Append("/>").ToString();
    }
}
