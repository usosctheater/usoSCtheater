using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;
using UsoSCTheater.Shared;   //[공유 코드 통합] LanguageInfo

namespace SCTheaterEditor.Tests;

/// <summary>추가(번역 모드 5회차): VOICEVOX 내보내기 — 매칭 읽기·저장, 대사 TXT(화자 전환·\n·쉼표), 라이선스(그룹·순서·경고), 스크립트 탭 [라이선스 내보내기], 설정 탭 '보이스'</summary>
public class VoiceExportTests
{
    private const string Match =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<!-- 주석은 저장해도 남음 -->\r\n" +
        "<VoiceMatch>\r\n" +
        "  <Voices>\r\n" +
        "    <Voice Id=\"zundamon\" Name=\"ずんだもん\" Credit=\"VOICEVOX:ずんだもん\" Use=\"OK\" Note=\"\" Terms=\"\" />\r\n" +
        "    <Voice Id=\"metan\" Name=\"四国めたん\" Credit=\"VOICEVOX:四国めたん\" Use=\"OK\" Note=\"\" Terms=\"\" />\r\n" +
        "    <Voice Id=\"no7\" Name=\"No.7\" Credit=\"VOICEVOX:No.7\" Use=\"NonCommercial\" Note=\"비상업만\" Terms=\"\" />\r\n" +
        "  </Voices>\r\n" +
        "  <Matches>\r\n" +
        "    <Default Voice=\"\" Style=\"\" />\r\n" +
        "    <Match CharId=\"asahi\" Voice=\"zundamon\" Style=\"\" />\r\n" +
        "    <Match CharId=\"fuyuko\" Voice=\"metan\" Style=\"あまあま\" />\r\n" +
        "    <Match CharId=\"mei\" Voice=\"no7\" Style=\"\" />\r\n" +
        "    <Match CharId=\"toru\" Voice=\"ghost\" Style=\"\" />\r\n" +
        "  </Matches>\r\n" +
        "</VoiceMatch>\r\n";

    private const string Act1 =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<Act MainTitle=\"제목\" MainTitle_Ja=\"タイトル\">\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"asahi\" Text=\"안녕\" Text_Ja=\"こんにちは、プロデューサーさん\\nっす\"/>\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"asahi\" Text=\"또\" Text_Ja=\"もう一回っす\"/>\r\n" +
        "\t<Line Type=\"BG\" ResourceKey=\"room\"/>\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"fuyuko, asahi\" Text=\"둘\" Text_Ja=\"ふゆ,です\"/>\r\n" +
        "\t<Line Type=\"TEXT\" Name=\"방송 PD\" Text=\"큐\" Text_Ja=\"キュー\"/>\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"asahi\" Text=\"번역 없음\"/>\r\n" +
        "</Act>\r\n";

    private const string Act2 =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<Act MainTitle=\"둘째\">\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"mei\" Text=\"메이\"/>\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"toru\" Text=\"토오루\"/>\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"asahi\" Text=\"\"/>\r\n" +
        "</Act>\r\n";

    private static readonly LanguageInfo Ja = new("ja", "日本語", false, "、");
    private static ActDocument Doc(string xml, string name = "IL01") => ActXmlFormat.Parse(xml, $@"C:\Scenario\IL\{name}.xml");

    [Fact]
    public void Parse_ReadsVoicesAndMatches()
    {
        var v = VoiceMatchData.Parse(Match);
        Assert.Equal(3, v.Voices.Count);
        Assert.Equal(new VoiceMatchEntry("metan", "あまあま"), v.MatchOf("FUYUKO"));
        Assert.Equal(new VoiceMatchEntry("", ""), v.MatchOf("mano"));
        Assert.True(v.FindVoice("no7")!.IsRestricted);
        Assert.Equal("No.7: 비상업만 — 비상업만", v.FindVoice("no7")!.Warning);
        Assert.Equal("四国めたん（あまあま）", VoiceMatchData.SpeakerLine(v.FindVoice("metan")!, "あまあま"));
    }

    [Fact]
    public void Script_SpeakerOnChange_JoinsNewline_ReplacesComma()
    {
        var v = VoiceMatchData.Parse(Match);
        var r = VoiceExport.BuildScript(Doc(Act1), Ja, v);
        Assert.Equal(
            "ずんだもん\r\n" +
            "こんにちは、プロデューサーさんっす\r\n" +   // \n은 이어 붙임
            "もう一回っす\r\n" +                          // 같은 화자는 전환 줄 없이 나눠서
            "四国めたん（あまあま）\r\n" +               // 여러 명이면 첫 번째
            "ふゆ、です\r\n" +                            // 반각 쉼표 → 、
            "キュー\r\n", r.Text);                        // 매칭 없음·기본 화자 없음 → 전환 줄 없이 (번역 없는 줄은 빠짐)
        Assert.Equal(4, r.Lines);
        Assert.Empty(r.Warnings);
        Assert.Equal("IL01_ja.txt", VoiceExport.ScriptFileName(Doc(Act1), Ja));
    }

    [Fact]
    public void Script_DefaultSpeakerForUnmatched()
    {
        var v = VoiceMatchData.Parse(Match);
        Assert.True(v.SetMatch(null, "zundamon", "ノーマル"));
        string text = VoiceExport.BuildScript(Doc(Act1), Ja, v).Text;
        Assert.EndsWith("ふゆ、です\r\nずんだもん（ノーマル）\r\nキュー\r\n", text);
    }

    [Fact]
    public void License_GroupOrderAndWarnings()
    {
        var v = VoiceMatchData.Parse(Match);
        var r = VoiceExport.BuildLicense([Doc(Act1), Doc(Act2, "IL02")], v);
        // VoiceMatch 순서, 대사가 있는 줄만 (Act2 asahi는 빈 대사지만 Act1에서 나옴), 매칭 없는 방송 PD는 없음
        Assert.Equal(VoiceExport.LicenseHeader + "\r\nVOICEVOX:ずんだもん\r\nVOICEVOX:四国めたん\r\nVOICEVOX:No.7\r\n", r.Text);
        Assert.Equal(3, r.Lines);
        Assert.Contains(r.Warnings, w => w.StartsWith("No.7: 비상업만"));
        Assert.Contains(r.Warnings, w => w.Contains("'ghost'"));      // 없는 화자 Id
    }

    [Fact]
    public void SetMatch_SaveKeepsCommentsAndVoices()
    {
        using var temp = new TempFolder();
        string path = temp.File("VoiceMatch.xml");
        File.WriteAllText(path, Match);
        var v = VoiceMatchData.Load(path);
        Assert.Equal("", v.Problem);
        Assert.False(v.SetMatch("asahi", "zundamon", ""));            // 같으면 바뀌지 않음
        Assert.True(v.SetMatch("mano", "metan", ""));                 // 없던 인물은 추가
        Assert.True(v.SetMatch("fuyuko", "metan", ""));
        v.Save(path);
        string saved = File.ReadAllText(path);
        Assert.Contains("주석은 저장해도 남음", saved);
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);           // BOM 없음
        var again = VoiceMatchData.Load(path);
        Assert.Equal(3, again.Voices.Count);
        Assert.Equal(new VoiceMatchEntry("metan", ""), again.MatchOf("mano"));
        Assert.Equal(new VoiceMatchEntry("metan", ""), again.MatchOf("fuyuko"));
    }

    [Fact]
    public void Load_MissingFile_EmptyWithProblem()
    {
        using var temp = new TempFolder();
        var v = VoiceMatchData.Load(temp.File("없음.xml"));
        Assert.Contains("없습니다", v.Problem);
        Assert.Empty(v.Voices);
        Assert.Equal("こんにちは、プロデューサーさんっす\r\nもう一回っす\r\nふゆ、です\r\nキュー\r\n", VoiceExport.BuildScript(Doc(Act1), Ja, v).Text);
    }

    [Fact]
    public void WriteFile_Utf8NoBom()
    {
        using var temp = new TempFolder();
        string path = VoiceExport.WriteFile(temp.File("out"), "a.txt", "ずんだもん\r\n");
        byte[] b = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, b[0]);
        Assert.Equal("ずんだもん\r\n", File.ReadAllText(path));
    }

    [Fact]
    public void EditorExportLicense_UsesWholeGroupAndUnsavedEdits()
    {
        using var temp = new TempFolder();
        string group = temp.File("Scenario\\IL");
        Directory.CreateDirectory(group);
        File.WriteAllText(Path.Combine(group, "IL01.xml"), Act1);
        File.WriteAllText(Path.Combine(group, "IL02.xml"), Act2);
        var settings = new AppSettings();
        settings.Voice.ExportFolder = temp.File("export");
        var lib = new SpineLibrary(temp.File("SpineData"), new SpineManifest());
        lib.Rebuild();
        var editor = new ActEditorViewModel(settings, lib, new FakeDialogs(), new ImmediateDispatcher(), () => { }, new AppLog(), temp.File("Backups"));
        editor.VoiceMatch = VoiceMatchData.Parse(Match);
        Assert.True(editor.Open(Path.Combine(group, "IL01.xml")));
        var doc = editor.Document!;
        doc.SetValuesMany([(doc.Lines[3], "CharId", null)], "시험");   // 저장 안 한 편집: 후유코 줄을 매칭 없는 줄로
        editor.ExportLicenseCommand.Execute(null);
        string text = File.ReadAllText(Path.Combine(temp.File("export"), "IL_license.txt"));
        Assert.Equal(VoiceExport.LicenseHeader + "\r\nVOICEVOX:ずんだもん\r\nVOICEVOX:No.7\r\n", text);
        Assert.Contains("막 2개", editor.StatusText);
        Assert.Contains("⚠ No.7", editor.StatusText);
    }

    [Fact]
    public void SettingsVoiceRows_ChangeSavesFile()
    {
        using var temp = new TempFolder();
        string path = temp.File("VoiceMatch.xml");
        File.WriteAllText(path, Match);
        var voices = VoiceMatchData.Load(path);
        var data = new GameData(Path.Combine(AppContext.BaseDirectory, "Data"), () => null, new AppLog());
        data.Refresh();
        var vm = new SettingsViewModel(new AppSettings(), new FakeDialogs(), () => { }, new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger(), new AppLog(),
                                       gameData: data, voices: voices, voicePath: path);
        Assert.Equal("기본 화자 (매칭 없는 줄)", vm.VoiceRows[0].Label);
        Assert.Equal(data.Characters.Count + 1, vm.VoiceRows.Count);
        var asahi = vm.VoiceRows.First(r => r.CharId == "asahi");
        Assert.Equal("zundamon", asahi.Selected.Id);
        var mei = vm.VoiceRows.First(r => r.CharId == "mei");
        Assert.StartsWith("⚠ No.7", mei.Warning);

        asahi.Selected = vm.VoiceOptions.First(o => o.Id == "metan");
        asahi.Style = "ツンツン";
        Assert.Contains("四国めたん（ツンツン）", vm.VoiceNote);
        Assert.Equal(new VoiceMatchEntry("metan", "ツンツン"), VoiceMatchData.Load(path).MatchOf("asahi"));
        Assert.Equal(new VoiceMatchEntry("metan", "ツンツン"), voices.MatchOf("asahi"));   // 같은 객체 — 번역 창·스크립트 표가 바로 씀
    }
}
