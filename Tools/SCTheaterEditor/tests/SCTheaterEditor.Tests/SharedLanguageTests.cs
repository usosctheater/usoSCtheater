using UsoSCTheater.Shared;

namespace SCTheaterEditor.Tests;

/// <summary>추가(공유 라이브러리에 코드 통합 2회차): 공유 언어 규칙 — LanguageData 해석·속성 이름·잘못된 접미어·이름 대체 순서·CharId·IdTable</summary>
public class SharedLanguageTests
{
    private static readonly LanguageInfo Ko = new("ko", "한국어", true);
    private static readonly LanguageInfo Ja = new("ja", "日本語", false, "、");
    private static readonly IReadOnlyList<LanguageInfo> Fallback = [Ko];

    [Fact]
    public void Parse_DefaultCodeAndSuffix()
    {
        var w = new List<string>();
        var list = LanguageRules.Parse(
            "<LanguageData><Language Code=\" KO \" NativeName=\"한국어\"/><Language Code=\"ja\" NativeName=\"日本語\" Default=\"TRUE\" ListSeparator=\"、\"/></LanguageData>",
            Fallback, w);
        Assert.Equal(new[] { "ko", "ja" }, list.Select(l => l.Code));   // 코드는 공백 지우고 소문자
        Assert.False(list[0].IsDefault);
        Assert.True(list[1].IsDefault);                                  // Default 값은 대소문자 무시
        Assert.Equal("Ja", list[1].Suffix);
        Assert.Equal("、", list[1].ListSeparator);
        Assert.Equal(", ", list[0].ListSeparator);
        Assert.Empty(w);
    }

    [Fact]
    public void Parse_NoDefault_FirstIsDefault_NativeNameFallsBackToCode()
    {
        var list = LanguageRules.Parse("<LanguageData><Language Code=\"ko\"/><Language Code=\"ja\"/></LanguageData>", Fallback, new List<string>());
        Assert.True(list[0].IsDefault);
        Assert.False(list[1].IsDefault);
        Assert.Equal("ja", list[1].NativeName);
    }

    [Fact]
    public void Parse_Problems_WarnAndSkip()
    {
        var w = new List<string>();
        var list = LanguageRules.Parse(
            "<LanguageData><Language Code=\"ko\" Default=\"true\"/><Language Code=\"\"/><Language Code=\"KO\"/><Language Code=\"ja\" Default=\"true\"/></LanguageData>",
            Fallback, w);
        Assert.Equal(new[] { "ko", "ja" }, list.Select(l => l.Code));
        Assert.True(list[0].IsDefault);   // Default가 여럿이면 첫 항목
        Assert.Equal(3, w.Count);          // 빈 코드 · 중복 · Default 여럿
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("<깨진")]
    [InlineData("<LanguageData/>")]
    public void Parse_EmptyOrBroken_Fallback(string? xml)
    {
        var w = new List<string>();
        Assert.Same(Fallback, LanguageRules.Parse(xml, Fallback, w));
        Assert.Single(w);
    }

    [Fact]
    public void Find_IgnoresCaseAndSpaces()
    {
        var list = new[] { Ko, Ja };
        Assert.Same(Ja, LanguageRules.Find(list, " JA "));
        Assert.Null(LanguageRules.Find(list, "en"));
        Assert.Null(LanguageRules.Find(list, ""));
        Assert.Null(LanguageRules.Find(list, null));
    }

    [Fact]
    public void AttrName_DefaultHasNoSuffix()
    {
        Assert.Equal("Text", LanguageRules.AttrName("Text", Ko));
        Assert.Equal("Text_Ja", LanguageRules.AttrName("Text", Ja));
        Assert.Equal("Tag_Ja", LanguageRules.AttrName("Tag", Ja));
    }

    [Theory]
    [InlineData("Text_Ja", false)]
    [InlineData("FullName_Ja", false)]
    [InlineData("Text", false)]
    [InlineData("_Ja", false)]       // 밑줄 앞이 없으면 접미어 아님
    [InlineData("Text_", false)]
    [InlineData("Text_ja", true)]    // 속성 이름은 대소문자 구분 (2026-11-11)
    [InlineData("Text_Jp", true)]    // 없는 언어
    [InlineData("Name_Ko", true)]    // 기본 언어는 접미어 없이
    public void HasInvalidSuffix(string name, bool invalid) =>
        Assert.Equal(invalid, LanguageRules.HasInvalidSuffix(name, new[] { Ko, Ja }));

    [Fact]
    public void TryGet_EmptyIsMissing()
    {
        var attrs = new Dictionary<string, string> { ["Text"] = "가", ["Text_Ja"] = "" };
        Assert.True(LanguageRules.TryGet(attrs, "Text", Ko, out string v));
        Assert.Equal("가", v);
        Assert.False(LanguageRules.TryGet(attrs, "Text", Ja, out v));
        Assert.Equal("", v);
    }

    [Fact]
    public void LocalizedNames_FallbackOrder()
    {
        var all = new Dictionary<string, string> { ["Name"] = "후유코", ["FullName"] = "마유즈미 후유코", ["Name_Ja"] = "冬優子", ["FullName_Ja"] = "黛冬優子" };
        Assert.Equal("冬優子", LocalizedNames.Get(all, "fuyuko", Ja, Ko, full: false));
        Assert.Equal("黛冬優子", LocalizedNames.Get(all, "fuyuko", Ja, Ko, full: true));

        var fullJaOnly = new Dictionary<string, string> { ["Name"] = "후유코", ["FullName_Ja"] = "黛冬優子" };
        Assert.Equal("黛冬優子", LocalizedNames.Get(fullJaOnly, "fuyuko", Ja, Ko, full: false));   // 같은 언어 다른 형식이 기본 언어보다 먼저

        var koOnly = new Dictionary<string, string> { ["FullName"] = "마유즈미 후유코" };
        Assert.Equal("마유즈미 후유코", LocalizedNames.Get(koOnly, "fuyuko", Ja, Ko, full: false));   // 기본 언어 다른 형식
        Assert.Equal("fuyuko", LocalizedNames.Get(new Dictionary<string, string>(), "fuyuko", Ja, Ko, full: false));
    }

    [Fact]
    public void CharIds_SplitAndJoin()
    {
        Assert.Equal(new[] { "asahi", "mei" }, CharIds.Split(" asahi, ,mei ,"));
        Assert.Empty(CharIds.Split(null));
        Assert.Equal("아사히, 메이", CharIds.Join(new[] { "아사히", "메이" }, Ko));
        Assert.Equal("あさひ、めい", CharIds.Join(new[] { "あさひ", "めい" }, Ja));
    }

    [Fact]
    public void IdTable_TrimDuplicateOrderAndCase()
    {
        var w = new List<string>();
        var list = IdTable.Parse(
            "<CharacterData><Character Id=\" asahi \" Name=\" 아사히 \" name_ja=\"x\"/><Character Name=\"없음\"/><Character Id=\"ASAHI\" Name=\"중복\"/><Character Id=\"mei\" Name=\"메이\"/><Unit Id=\"u\"/></CharacterData>",
            "Character", w);
        Assert.Equal(new[] { "asahi", "mei" }, list.Select(e => e.Id));   // 파일 순서, 다른 요소 제외
        Assert.Equal("아사히", list[0].Attrs["Name"]);                    // 값 공백 지움
        Assert.False(list[0].Attrs.ContainsKey("Id"));
        Assert.False(list[0].Attrs.ContainsKey("Name_Ja"));               // 속성 이름은 대소문자 구분
        Assert.True(list[0].Attrs.ContainsKey("name_ja"));
        Assert.Equal(2, w.Count);                                         // Id 없음 · 중복
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<깨진")]
    public void IdTable_EmptyOrBroken_EmptyWithWarning(string? xml)
    {
        var w = new List<string>();
        Assert.Empty(IdTable.Parse(xml, "Character", w));
        Assert.Single(w);
    }
}
