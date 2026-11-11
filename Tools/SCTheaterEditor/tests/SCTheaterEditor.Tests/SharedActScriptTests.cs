using SCTheaterEditor.Script;
using UsoSCTheater.Shared;

namespace SCTheaterEditor.Tests;

/// <summary>[공유 코드 통합] 3회차: 공유 막 줄 읽기(ActAttrs)·CG 키 모으기·스크립트 검사·속성 이름 교정 — 게임 DialogManager·카탈로그 동기화와 TE가 같은 결과</summary>
public class SharedActScriptTests
{
    private static readonly IReadOnlyList<LanguageInfo> Langs = [new("ko", "한국어", true), new("ja", "日本語", false, "、")];

    private static ActAttrs A(params (string Name, string Value)[] attrs) =>
        new(attrs.Select(a => new KeyValuePair<string, string>(a.Name, a.Value)).ToList());

    [Fact]
    public void Attrs_ExactName_TrimKey_InvariantNumber()
    {
        var l = A(("Type", " text "), ("CG", " asahi_1 "), ("Value", "1.5"), ("Volume", "abc"), ("Text", " 대사 "));
        Assert.Equal("TEXT", l.Type);
        Assert.True(l.Is(ActFormat.TypeText));
        Assert.Equal("asahi_1", l.Key("CG"));
        Assert.Equal(" 대사 ", l.Raw("Text"));                  // 대사는 그대로
        Assert.Null(l.Find("cg"));                              // 이름은 대소문자 구분
        Assert.Equal(1.5, l.Number("Value", 1));
        Assert.Equal(0.4, l.Number("Volume", 0.4));             // 못 읽으면 기본값
        Assert.True(l.KeyIs("CG", "ASAHI_1"));                  // 값은 대소문자 무시
    }

    [Fact]
    public void Localized_FallsBackToDefault()
    {
        var l = A(("Text", "안녕"), ("Text_Ja", ""), ("Name", "아사히"), ("Name_Ja", "あさひ"));
        Assert.Equal("안녕", l.Localized("Text", Langs[1]));      // 빈 번역 → 기본
        Assert.Equal("あさひ", l.Localized("Name", Langs[1]));
        Assert.Equal("아사히", l.Localized("Name", Langs[0]));
    }

    [Fact]
    public void SpineKeys_GroupsCaseInsensitive_SkipsEmptyGroupId_NoneAndDuplicates()
    {
        var lines = new List<ActAttrs>
        {
            A(("Type", "TEXT"), ("CG", "asahi_1")),
            A(("Type", "CGGROUP"), ("GroupId", "G1"), ("CG", "mano_1")),
            A(("Type", "CGGROUP"), ("GroupId", " "), ("CG", "ignored_1")),   // GroupId 없음 → 무시
            A(("Type", "TEXT"), ("CG", "g1")),                               // 그룹 이름 (대소문자 무시)
            A(("Type", "SETCG"), ("CG", "none")),
            A(("Type", "TEXT"), ("CG", "ASAHI_1")),                          // 중복 (대소문자 무시)
            A(("Type", "BG"), ("CG", "bg_1")),                               // CG를 쓰지 않는 종류
            A(("Type", "SETCG"), ("CG", " meguru_1 ")),
        };
        Assert.Equal(["asahi_1", "mano_1", "meguru_1"], ActScript.SpineKeys(lines));
        Assert.Equal(["G1"], ActScript.GroupIds(lines));
    }

    [Fact]
    public void CorrectName_KnownNamesOnly()
    {
        Assert.Equal("Text", ActFormat.CorrectName("text", false, Langs));
        Assert.Equal("Text_Ja", ActFormat.CorrectName("Text_ja", false, Langs));
        Assert.Equal("ResourceKey", ActFormat.CorrectName("resourcekey", false, Langs));
        Assert.Null(ActFormat.CorrectName("Text", false, Langs));           // 이미 맞음
        Assert.Null(ActFormat.CorrectName("Zoom", false, Langs));           // 모르는 이름
        Assert.Null(ActFormat.CorrectName("Voice_ja", false, Langs));       // 번역 속성이 아닌 것
        Assert.Equal("MainTitle_Ja", ActFormat.CorrectName("mainTitle_ja", true, Langs));
        Assert.Null(ActFormat.CorrectName("mainTitle", false, Langs));      // 줄에는 제목 없음
    }

    [Fact]
    public void CheckLine_FourKinds()
    {
        var issues = new List<ActIssue>();
        ActScript.CheckLine(A(("Type", "TEXT"), ("CharId", "asahi, nobody"), ("text", "a"), ("Text_Ko", "b")), false, Langs,
                            id => id == "asahi", issues);
        Assert.Equal(3, issues.Count);
        Assert.Contains(issues, i => i.Attr == "text" && i.Message.Contains("→ 'Text'"));
        Assert.Contains(issues, i => i.Attr == "Text_Ko");                    // 기본 언어 접미어
        Assert.Contains(issues, i => i.Attr == "CharId" && i.Message.Contains("'nobody'"));

        issues.Clear();
        ActScript.CheckLine(A(("Type", "CGGROUP"), ("CG", "x")), false, Langs, _ => true, issues);
        Assert.Equal("GroupId", Assert.Single(issues).Attr);

        issues.Clear();
        ActScript.CheckLine(A(("MainTitle", "t"), ("subTitle", "s")), true, Langs, _ => false, issues);
        Assert.Equal("subTitle", Assert.Single(issues).Attr);
    }

    [Fact]
    public void LineTypes_PaletteAttrsAreKnown()
    {
        var known = ActFormat.LineAttrs.ToHashSet();
        foreach (var t in LineTypeCatalog.LoadEmbedded().Types)
            foreach (var f in t.Fields)
                Assert.True(known.Contains(f.Attr), $"{t.Id}.{f.Attr}가 ActFormat.LineAttrs에 없음");
    }
}
