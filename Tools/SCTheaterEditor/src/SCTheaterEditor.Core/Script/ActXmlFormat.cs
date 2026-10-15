using System.IO;
using System.Text;
using System.Xml;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.Script;

/// <summary>막 XML을 읽을 수 없음 (XML 오류, 루트 태그 다름 등)</summary>
public sealed class ActFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// 막 XML 읽기·쓰기 (게임 DialogManager.LoadAct / ActXml과 같은 형식).
/// - 루트: &lt;Act mainTitle subTitle&gt; (기존 &lt;Scene MainTitle SubTitle&gt;도 읽음 → 저장하면 Act로, 제목 속성은 대소문자 무시)
/// - 저장 모양은 기존 파일 그대로: 선언 줄, 탭 들여쓰기, 라인 1줄, 줄바꿈(CRLF/LF)·BOM·마지막 줄바꿈 유무, 주석·빈 줄
/// - 바뀌지 않은 라인은 원래 줄 텍스트 그대로, 바뀐 라인만 새로 씀. 모르는 속성·종류·요소도 보존
/// - 원래 모양을 지킬 수 없는 파일(한 요소가 여러 줄 등)은 전체를 표준 모양으로 다시 씀 (PreservesLayout = false)
/// </summary>
public static class ActXmlFormat
{
    public const string RootTag = "Act";
    public const string LegacyRootTag = "Scene";
    public const string MainTitleAttr = "mainTitle";
    public const string SubTitleAttr = "subTitle";
    public const string DefaultDeclaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly XmlReaderSettings ReaderSettings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
    private static readonly XmlReaderSettings FragmentSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, ConformanceLevel = ConformanceLevel.Fragment,
    };

    // ------------------------------------------------------------------ 읽기

    public static ActDocument Load(string path)
    {
        var doc = Parse(File.ReadAllBytes(path), path);
        var info = new FileInfo(path);
        doc.FileTimeUtc = info.LastWriteTimeUtc;
        doc.FileLength = info.Length;
        return doc;
    }

    public static ActDocument Parse(byte[] bytes, string? path = null)
    {
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        string text;
        try { text = StrictUtf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); }
        catch (DecoderFallbackException ex) { throw new ActFormatException("UTF-8 파일이 아닙니다", ex); }
        return Parse(text, path, bom);
    }

    public static ActDocument Parse(string text, string? path = null, bool bom = false)
    {
        var xml = new XmlDocument { XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), ReaderSettings);
            xml.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new ActFormatException($"XML 형식 오류 ({ex.LineNumber}행 {ex.LinePosition}열): {ex.Message}", ex);
        }

        var root = xml.DocumentElement;
        if (root == null || (root.Name != RootTag && root.Name != LegacyRootTag))
            throw new ActFormatException($"루트 태그가 <{RootTag}> 또는 <{LegacyRootTag}>가 아닙니다: <{root?.Name}>");

        var doc = new ActDocument(path)
        {
            SourceRootName = root.Name,
            HasBom = bom,
            Newline = text.Contains("\r\n") ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n",
            EndsWithNewline = text.EndsWith('\n'),
        };

        string main = "", sub = "";
        foreach (XmlAttribute a in root.Attributes)
        {
            if (a.Name.Equals(MainTitleAttr, StringComparison.OrdinalIgnoreCase)) main = a.Value;
            else if (a.Name.Equals(SubTitleAttr, StringComparison.OrdinalIgnoreCase)) sub = a.Value;
            else doc.ExtraRootAttributes.Add(new(a.Name, a.Value));
        }
        doc.InitTitles(main, sub);

        // XML 기준: Line 요소(게임이 읽는 것) + 그 밖의 노드(주석 등 — 보존만)
        var elems = new List<List<KeyValuePair<string, string>>>();
        var others = new List<(int Before, string Xml)>();
        foreach (XmlNode n in root.ChildNodes)
        {
            if (n is XmlElement { Name: "Line" } e) elems.Add(Attrs(e));
            else if (n.NodeType is XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace) continue;
            else others.Add((elems.Count, n.OuterXml));
        }

        if (!TryPreserveLayout(doc, text, root.Name, elems))
        {
            // 표준 모양: 주석 등은 다음 라인 앞에 붙여 보존
            doc.PreservesLayout = false;
            doc.Prologue = [DefaultDeclaration];
            var lead = others.ToLookup(o => o.Before, o => "\t" + o.Xml);
            for (int i = 0; i < elems.Count; i++)
            {
                var line = new ScriptLine(elems[i]);
                if (lead.Contains(i)) line.LeadingRaw = lead[i].ToList();
                doc.Lines.Add(line);
            }
            doc.TrailingRaw = lead[elems.Count].ToList();
        }
        return doc;
    }

    private static List<KeyValuePair<string, string>> Attrs(XmlElement e)
    {
        var list = new List<KeyValuePair<string, string>>(e.Attributes.Count);
        foreach (XmlAttribute a in e.Attributes) list.Add(new(a.Name, a.Value));
        return list;
    }

    /// <summary>
    /// 줄 단위로 원래 모양 대응: 루트 여는 줄·닫는 줄이 각자 한 줄, 그 사이 Line 요소가 한 줄에 하나씩이면 성공.
    /// 그 밖의 줄(주석·빈 줄)은 다음 라인 앞에 그대로 붙여 둠
    /// </summary>
    private static bool TryPreserveLayout(ActDocument doc, string text, string rootName, List<List<KeyValuePair<string, string>>> elems)
    {
        var src = text.Split('\n').Select(l => l.EndsWith('\r') ? l[..^1] : l).ToList();
        if (doc.EndsWithNewline) src.RemoveAt(src.Count - 1);

        int open = src.FindIndex(l => IsOpenTag(l.Trim(), rootName));
        int close = src.FindLastIndex(l => l.Trim() == $"</{rootName}>");
        if (open < 0 || close <= open) return false;
        if (src.Skip(close + 1).Any(l => l.Trim().Length > 0)) return false;

        var result = new List<ScriptLine>(elems.Count);
        var pending = new List<string>();
        for (int i = open + 1; i < close; i++)
        {
            string line = src[i];
            string t = line.Trim();
            if (IsLineElement(t))
            {
                if (result.Count >= elems.Count) return false;
                var attrs = ParseSingleLine(t);
                if (attrs == null || !SameAttrs(attrs, elems[result.Count])) return false;
                var sl = new ScriptLine(elems[result.Count], line);
                if (pending.Count > 0)
                {
                    sl.LeadingRaw = pending;
                    pending = new();
                }
                result.Add(sl);
            }
            else
            {
                if (t.Contains("<Line", StringComparison.Ordinal) && !t.StartsWith("<!--", StringComparison.Ordinal)) return false;
                pending.Add(line);
            }
        }
        if (result.Count != elems.Count) return false;

        doc.Prologue = src.Take(open).ToList();
        doc.TrailingRaw = pending;
        foreach (var l in result) doc.Lines.Add(l);
        doc.PreservesLayout = true;
        return true;
    }

    private static bool IsOpenTag(string t, string rootName) =>
        t.StartsWith("<" + rootName, StringComparison.Ordinal) && t.Length > rootName.Length + 1 &&
        (char.IsWhiteSpace(t[rootName.Length + 1]) || t[rootName.Length + 1] == '>') &&
        t.EndsWith('>') && !t.EndsWith("/>", StringComparison.Ordinal) && t.IndexOf('<', 1) < 0;

    private static bool IsLineElement(string t) =>
        t.StartsWith("<Line", StringComparison.Ordinal) && t.Length > 5 && (char.IsWhiteSpace(t[5]) || t[5] == '/') &&
        t.EndsWith("/>", StringComparison.Ordinal);

    /// <summary>한 줄짜리 &lt;Line …/&gt;의 속성 (요소가 정확히 하나가 아니면 null)</summary>
    private static List<KeyValuePair<string, string>>? ParseSingleLine(string t)
    {
        try
        {
            using var r = XmlReader.Create(new StringReader(t), FragmentSettings);
            if (!r.Read() || r.NodeType != XmlNodeType.Element || r.Name != "Line" || !r.IsEmptyElement) return null;
            var list = new List<KeyValuePair<string, string>>();
            if (r.MoveToFirstAttribute())
            {
                do list.Add(new(r.Name, r.Value)); while (r.MoveToNextAttribute());
            }
            r.MoveToElement();
            return r.Read() ? null : list;
        }
        catch (XmlException) { return null; }
    }

    private static bool SameAttrs(List<KeyValuePair<string, string>> a, List<KeyValuePair<string, string>> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Key != b[i].Key || a[i].Value != b[i].Value) return false;
        return true;
    }

    // ------------------------------------------------------------------ 쓰기

    public static string ToText(ActDocument doc)
    {
        string nl = doc.Newline;
        var sb = new StringBuilder(doc.Lines.Count * 120);
        foreach (var p in doc.Prologue) sb.Append(p).Append(nl);

        sb.Append('<').Append(RootTag);
        AppendAttr(sb, MainTitleAttr, doc.MainTitle);
        AppendAttr(sb, SubTitleAttr, doc.SubTitle);
        foreach (var a in doc.ExtraRootAttributes) AppendAttr(sb, a.Key, a.Value);
        sb.Append('>').Append(nl);

        foreach (var line in doc.Lines)
        {
            if (line.LeadingRaw != null)
                foreach (var r in line.LeadingRaw) sb.Append(r).Append(nl);
            sb.Append(line.ToXmlLine()).Append(nl);
        }
        foreach (var r in doc.TrailingRaw) sb.Append(r).Append(nl);

        sb.Append("</").Append(RootTag).Append('>');
        if (doc.EndsWithNewline) sb.Append(nl);
        return sb.ToString();
    }

    public static byte[] ToBytes(ActDocument doc)
    {
        byte[] body = new UTF8Encoding(false).GetBytes(ToText(doc));
        return doc.HasBom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    /// <summary>파일로 저장: 임시 파일(.tmp — Unity가 무시)에 끝까지 쓴 뒤 교체. 저장 시각·크기와 저장 시점을 문서에 기록</summary>
    public static void Save(ActDocument doc, string path)
    {
        SafeFile.WriteAllBytes(path, ToBytes(doc));
        var info = new FileInfo(path);
        doc.FilePath = Path.GetFullPath(path);
        doc.FileTimeUtc = info.LastWriteTimeUtc;
        doc.FileLength = info.Length;
        doc.SourceRootName = RootTag;
        doc.Undo.MarkSaved();
    }

    private static void AppendAttr(StringBuilder sb, string name, string value) =>
        sb.Append(' ').Append(name).Append("=\"").Append(Escape(value)).Append('"');

    /// <summary>속성 값 이스케이프 (&amp; &lt; &gt; " 와 탭·줄바꿈 문자). 대사의 \n 글자(역슬래시+n)는 그대로 — 게임이 읽을 때 줄바꿈으로 바꿈</summary>
    public static string Escape(string value)
    {
        if (value.AsSpan().IndexOfAny("&<>\"\t\n\r") < 0) return value;
        var sb = new StringBuilder(value.Length + 16);
        foreach (char c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\t': sb.Append("&#x9;"); break;
                case '\n': sb.Append("&#xA;"); break;
                case '\r': sb.Append("&#xD;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
