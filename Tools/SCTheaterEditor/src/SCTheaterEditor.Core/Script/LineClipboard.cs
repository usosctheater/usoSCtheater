using System.Xml;

namespace SCTheaterEditor.Script;

/// <summary>
/// 추가: 줄 복사/붙여넣기 형식 = 막 XML 줄 (탭 + &lt;Line …/&gt; 한 줄씩, CRLF).
/// 붙여넣기는 &lt;Line&gt; 요소를 순서대로 읽음 — 줄만 있어도, &lt;Act&gt;/&lt;Scene&gt; 문서 전체여도 됨. 속성 순서·모르는 속성 그대로
/// </summary>
public static class LineClipboard
{
    public static string ToText(IEnumerable<ScriptLine> lines) => string.Join("\r\n", lines.Select(l => l.Format()));

    /// <summary>텍스트에서 Line 요소를 순서대로 새 라인으로. XML이 아니거나 Line이 없으면 빈 목록</summary>
    public static List<ScriptLine> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        string body = text.Trim();
        if (body.StartsWith("<?xml", StringComparison.Ordinal))   // 선언 줄은 감싸기 전에 뗌
        {
            int end = body.IndexOf("?>", StringComparison.Ordinal);
            if (end >= 0) body = body[(end + 2)..];
        }
        var xml = new XmlDocument { XmlResolver = null };
        try { xml.LoadXml("<Clip>" + body + "</Clip>"); }
        catch (XmlException) { return []; }
        return xml.GetElementsByTagName("Line").OfType<XmlElement>()
                  .Select(e => new ScriptLine(e.Attributes.Cast<XmlAttribute>()
                                               .Select(a => new KeyValuePair<string, string>(a.Name, a.Value))))
                  .ToList();
    }
}
