using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.Script;

/// <summary>가번역할 칸 1개 (Line이 null이면 막 제목·부제 — 루트 속성). Id는 요청·답변에서 칸을 가리키는 이름 (T.mainTitle, L12.Text 등)</summary>
public sealed record PreTranslationTarget(string Id, ScriptLine? Line, string BaseAttr, string Source);

/// <summary>답변 1개</summary>
public sealed record PreTranslationItem(string Id, string Value);

/// <summary>
/// 추가(번역 모드 4회차): 가번역 요청 만들기 · 답변 읽기 (화면·실행과 분리 — 테스트 가능).
/// - 시스템 프롬프트 = 고정 규칙(출력 형식) + 언어별 번역 지침 파일
/// - 입력 = 막 전체 문맥(TEXT 줄마다 화자·한국어·이미 있는 번역) + 번역할 칸(todo) 목록
/// - 답변 = JSON 배열 [{"id","value"}] — 코드 블록·앞뒤 설명이 붙어도 첫 '['부터 마지막 ']'까지 읽음
/// </summary>
public static class PreTranslation
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),   // 한글·일본어를 \uXXXX로 바꾸지 않음 (읽기 쉽고 짧게)
    };

    public static string SystemPrompt(LanguageInfo lang, string? guide)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"너는 『アイドルマスター シャイニーカラーズ』 2차 창작 시나리오의 번역가다. 한국어 대사를 {lang.NativeName}({lang.Code})로 옮긴다.");
        sb.AppendLine();
        sb.AppendLine("# 출력 규칙 (반드시 지킴)");
        sb.AppendLine("- 입력 JSON의 todo에 있는 id만, 빠짐없이 번역한다. lines·title은 문맥으로만 쓴다");
        sb.AppendLine("- 출력은 JSON 배열 하나뿐: [{\"id\":\"…\",\"value\":\"…\"}, …]. 설명·코드 블록·다른 글은 쓰지 않는다");
        sb.AppendLine("- 원문의 \\n(역슬래시+n 두 글자)은 줄바꿈 표시다. 같은 수만큼 두 글자 그대로 남긴다 (실제 줄바꿈 문자로 바꾸지 않음)");
        sb.AppendLine("- id가 L{번호}.Name이면 화면에 나오는 화자 이름, L{번호}.Text면 대사, T.mainTitle·T.subTitle이면 막 제목·부제");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(guide))
        {
            sb.AppendLine("# 번역 지침");
            sb.AppendLine();
            sb.AppendLine(guide.Trim());
        }
        return sb.ToString();
    }

    /// <summary>번역할 칸 = 원문이 있고 이 언어 번역이 빈 칸 (게임 Check Translations · 번역 창 진행률과 같은 기준)</summary>
    public static List<PreTranslationTarget> Targets(ActDocument doc, LanguageInfo lang)
    {
        var list = new List<PreTranslationTarget>();
        void Root(string attr, string source)
        {
            if (source.Trim().Length > 0 && string.IsNullOrWhiteSpace(doc.GetRootAttribute($"{attr}_{lang.Suffix}")))
                list.Add(new($"T.{attr}", null, attr, source));
        }
        Root(ActXmlFormat.MainTitleAttr, doc.MainTitle);
        Root(ActXmlFormat.SubTitleAttr, doc.SubTitle);
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var line = doc.Lines[i];
            if (!IsText(line)) continue;
            foreach (var attr in new[] { "Name", "Text" })
            {
                string src = line.Get(attr);
                if (src.Trim().Length > 0 && string.IsNullOrWhiteSpace(line.Get($"{attr}_{lang.Suffix}")))
                    list.Add(new($"L{i + 1}.{attr}", line, attr, src));
            }
        }
        return list;
    }

    /// <summary>입력(표준 입력) JSON — 문맥: 제목 + TEXT 줄 전부(번호·화자·한국어·이미 있는 번역), todo: 번역할 칸</summary>
    public static string Input(ActDocument doc, LanguageInfo lang, CharacterData characters, IReadOnlyList<PreTranslationTarget> targets)
    {
        string s = lang.Suffix;
        var lines = new List<object>();
        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var l = doc.Lines[i];
            if (!IsText(l)) continue;
            lines.Add(new
            {
                line = i + 1,
                speaker = characters.SpeakerNames(l.Get("CharId"), lang) ?? "",
                speakerId = l.Get("CharId"),
                name = l.Get("Name"),
                text = l.Get("Text"),
                name_translated = l.Get($"Name_{s}"),
                text_translated = l.Get($"Text_{s}"),
            });
        }
        var payload = new
        {
            language = lang.Code,
            act = $"{doc.Group}/{doc.ActName}",
            title = new { mainTitle = doc.MainTitle, subTitle = doc.SubTitle,
                          mainTitle_translated = doc.GetRootAttribute($"{ActXmlFormat.MainTitleAttr}_{s}") ?? "",
                          subTitle_translated = doc.GetRootAttribute($"{ActXmlFormat.SubTitleAttr}_{s}") ?? "" },
            lines,
            todo = targets.Select(t => new { id = t.Id, source = t.Source }).ToList(),
        };
        return JsonSerializer.Serialize(payload, Json);
    }

    /// <summary>답변 글 → 항목 (코드 블록·설명이 섞여도 첫 '['~마지막 ']'). 읽지 못하면 null</summary>
    public static List<PreTranslationItem>? ParseAnswer(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        int a = text.IndexOf('['), b = text.LastIndexOf(']');
        if (a < 0 || b <= a) return null;
        try
        {
            using var doc = JsonDocument.Parse(EscapeRawNewlines(text[a..(b + 1)]));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<PreTranslationItem>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
                    e.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                    list.Add(new(id.GetString()!, v.GetString()!.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n")));   // 실제 줄바꿈으로 답하면 \n 두 글자로
            }
            return list;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>문자열 값 안의 실제 줄바꿈(JSON에서 허용 안 됨)을 \n 이스케이프로 — 값 밖의 줄바꿈(들여쓰기)은 그대로</summary>
    private static string EscapeRawNewlines(string json)
    {
        var sb = new StringBuilder(json.Length);
        bool inString = false, escaped = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                else if (c == '\r') { if (i + 1 < json.Length && json[i + 1] == '\n') i++; sb.Append("\\n"); continue; }
                else if (c == '\n') { sb.Append("\\n"); continue; }
            }
            else if (c == '"') inString = true;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>\n(두 글자) 개수</summary>
    public static int NewlineCount(string s)
    {
        int n = 0;
        for (int i = s.IndexOf("\\n", StringComparison.Ordinal); i >= 0; i = s.IndexOf("\\n", i + 2, StringComparison.Ordinal)) n++;
        return n;
    }

    private static bool IsText(ScriptLine l) => l.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase);
}
