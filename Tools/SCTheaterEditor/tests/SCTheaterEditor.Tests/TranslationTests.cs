using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>추가(번역 모드 3회차): 번역 창 ViewModel — 행 구성·진행률·흐린 글씨·편집(되돌리기)·대소문자 정리·선택 동기화</summary>
public class TranslationTests
{
    private const string Act =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<Act mainTitle=\"제목\" subTitle=\"부제\" mainTitle_ja=\"タイトル\">\r\n" +
        "\t<Line Type=\"BG\" ResourceKey=\"room\"/>\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"asahi\" Text=\"안녕\" Text_ja=\"こんにちは\"/>\r\n" +
        "\t<Line Type=\"TEXT\" Name=\"방송 PD\" Text=\"큐!\"/>\r\n" +
        "\t<Line Type=\"TEXT\" Text=\"나레이션\"/>\r\n" +
        "</Act>\r\n";

    private sealed class Env : IDisposable
    {
        public TempFolder Temp { get; } = new();
        public ActEditorViewModel Editor { get; }
        public TranslationViewModel Vm { get; }
        public TranslationWindowSettings Settings { get; } = new() { Language = "ja" };
        public int Syncs;
        public string Path1 => Temp.File("IL01.xml");

        public Env(bool autoSync = false)
        {
            File.WriteAllText(Path1, Act);
            var data = new GameData(Path.Combine(AppContext.BaseDirectory, "Data"), () => null, new AppLog());   // 시험용 복사본 (게임 원본 없음)
            data.Refresh();
            var lib = new SpineLibrary(Temp.File("SpineData"), new SpineManifest());
            lib.Rebuild();
            Editor = new ActEditorViewModel(new AppSettings(), lib, new FakeDialogs(), new ImmediateDispatcher(), () => { }, new AppLog(),
                                            Temp.File("Backups"), characters: data.Characters);
            Assert.True(Editor.Open(Path1));
            Settings.AutoStageSync = autoSync;
            Vm = new TranslationViewModel(Editor, data, Settings, () => { }, () => Interlocked.Increment(ref Syncs), new ImmediateDispatcher());
        }

        public ActDocument Doc => Editor.Document!;
        public ScriptLine Line(int i) => Doc.Lines[i];
        public void Dispose() { Vm.Dispose(); Temp.Dispose(); }
    }

    [Fact]
    public void Rows_TitlePlusTextLinesOnly()
    {
        using var env = new Env();
        Assert.Equal(4, env.Vm.Rows.Count);                         // 제목 + TEXT 3줄 (BG 제외)
        Assert.True(env.Vm.Rows[0].IsTitle);
        Assert.Equal("#2", env.Vm.Rows[1].NumberText);              // 막의 줄 번호
        Assert.Equal("아사히", env.Vm.Rows[1].SourceTop);            // CharId → 한국어 DB 이름
        Assert.Equal("방송 PD", env.Vm.Rows[2].SourceTop);
        Assert.Equal("(이름 없음)", env.Vm.Rows[3].SourceTop);
        Assert.Equal("日本語", env.Vm.SelectedLanguage!.NativeName);
    }

    [Fact]
    public void Normalize_FixesCase_OneUndoStep()
    {
        using var env = new Env();
        Assert.Equal("タイトル", env.Doc.GetRootAttribute("mainTitle_Ja"));
        Assert.Null(env.Doc.GetRootAttribute("mainTitle_ja"));
        Assert.Equal("こんにちは", env.Line(1).Find("Text_Ja"));
        Assert.Null(env.Line(1).Find("Text_ja"));
        Assert.True(env.Vm.Rows[0].First.Warning);
        Assert.True(env.Vm.Rows[1].Second.Warning);
        Assert.False(env.Vm.Rows[1].First.Warning);
        Assert.Contains("2곳", env.Vm.StatusText);
        Assert.True(env.Doc.IsDirty);

        Assert.Equal("번역 속성 표기 정리", env.Doc.Undo.UndoLabel);
        env.Doc.UndoLast();                                         // 한 번에 원래 표기로
        Assert.Equal("こんにちは", env.Line(1).Find("Text_ja"));
        Assert.Equal("タイトル", env.Doc.GetRootAttribute("mainTitle_ja"));
        Assert.False(env.Doc.IsDirty);
    }

    [Fact]
    public void Progress_SameRuleAsGameCheck()
    {
        using var env = new Env();
        // 원문 있는 칸: 제목·부제, 2번 대사, 3번 이름·대사, 4번 대사 = 6 (CharId만 있는 2번 이름·빈 4번 이름은 제외) / 채움: 제목·2번 대사
        Assert.Equal("日本語 2/6", env.Vm.ProgressText);
        Assert.False(env.Vm.Rows[1].First.IsRequired);
        env.Vm.Rows[2].First.Value = "放送PD";
        Assert.Equal("日本語 3/6", env.Vm.ProgressText);
    }

    [Fact]
    public void Hint_ShowsWhatGameShowsWhenEmpty()
    {
        using var env = new Env();
        Assert.Equal("あさひ", env.Vm.Rows[1].First.Hint);           // CharId → 일본어 DB 이름
        Assert.Equal("방송 PD", env.Vm.Rows[2].First.Hint);          // 직접 적은 한국어 이름이 그대로 나옴
        Assert.Equal("", env.Vm.Rows[3].First.Hint);
        Assert.Equal("", env.Vm.Rows[1].Second.Hint);               // 대사는 흐린 글씨 없음
    }

    [Fact]
    public void Edit_WritesLanguageAttribute_UndoShared()
    {
        using var env = new Env();
        env.Vm.Rows[2].First.Value = "放送PD";
        Assert.Equal("放送PD", env.Line(2).Find("Name_Ja"));
        env.Vm.Rows[0].Second.Value = "サブ";
        Assert.Equal("サブ", env.Doc.GetRootAttribute("subTitle_Ja"));
        Assert.Contains("subTitle_Ja=\"サブ\"", ActXmlFormat.ToText(env.Doc));

        env.Editor.UndoCommand.Execute(null);                       // 스크립트 표와 같은 되돌리기
        Assert.Null(env.Doc.GetRootAttribute("subTitle_Ja"));
        Assert.Equal("", env.Vm.Rows[0].Second.Value);
        env.Vm.Rows[2].First.Value = "";                            // 빈 값 = 속성 삭제
        Assert.Null(env.Line(2).Find("Name_Ja"));
    }

    [Fact]
    public void SourceEditedInTable_RowFollows()
    {
        using var env = new Env();
        env.Doc.SetAttribute(env.Line(3), "Text", "바뀐 나레이션");
        Assert.Equal("바뀐 나레이션", env.Vm.Rows[3].SourceBottom);
        env.Doc.SetAttribute(env.Line(0), "Type", "TEXT");          // BG → TEXT: 행이 생김
        Assert.Equal(5, env.Vm.Rows.Count);
    }

    [Fact]
    public void Focus_SelectsLine_SyncOnlyWhenOn_Debounced()
    {
        using (var env = new Env(autoSync: false))
        {
            env.Vm.OnCellFocused(env.Vm.Rows[2].Second);
            Assert.Equal(env.Line(2), Assert.Single(env.Editor.Selection).Line);
            Thread.Sleep(TranslationViewModel.SyncDelayMs + 250);
            Assert.Equal(0, env.Syncs);
        }
        using (var env = new Env(autoSync: true))
        {
            env.Vm.OnCellFocused(env.Vm.Rows[1].First);
            env.Vm.OnCellFocused(env.Vm.Rows[1].Second);           // 같은 줄 — 다시 하지 않음
            env.Vm.OnCellFocused(env.Vm.Rows[2].First);
            env.Vm.OnCellFocused(env.Vm.Rows[3].First);            // 빠르게 옮김 → 마지막에 한 번
            Thread.Sleep(TranslationViewModel.SyncDelayMs + 250);
            Assert.Equal(1, env.Syncs);
            Assert.Equal(env.Line(3), Assert.Single(env.Editor.Selection).Line);
            env.Vm.OnCellFocused(env.Vm.Rows[0].First);            // 제목 칸은 줄이 아님
            Thread.Sleep(TranslationViewModel.SyncDelayMs + 250);
            Assert.Equal(1, env.Syncs);
        }
    }

    [Fact]
    public void OtherActOpened_Follows()
    {
        using var env = new Env();
        string other = env.Temp.File("IL02.xml");
        File.WriteAllText(other, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Act mainTitle=\"둘\" subTitle=\"\">\r\n\t<Line Type=\"TEXT\" Text=\"하나\"/>\r\n</Act>\r\n");
        env.Doc.UndoLast();                                         // 정리 되돌려 저장 안 된 변경 없음 (다른 막 열기 확인 창 방지)
        Assert.True(env.Editor.Open(other));
        Assert.Equal(2, env.Vm.Rows.Count);
        Assert.Equal("둘", env.Vm.Rows[0].SourceTop);
        Assert.Equal("IL02", env.Vm.DocumentTitle.Split(" / ")[1]);
    }
}
