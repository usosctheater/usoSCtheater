using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;
using UsoSCTheater.Shared;   //[공유 코드 통합] LanguageInfo

namespace SCTheaterEditor.Tests;

/// <summary>추가(번역 모드 4회차): 가번역 — 대상 칸·요청·답변 읽기·적용(빈 칸만, 되돌리기 1단계)·\n 경고·지침 파일·Claude 실행 결과 읽기</summary>
public class PreTranslationTests
{
    private const string Act =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<Act mainTitle=\"제목\" subTitle=\"부제\" mainTitle_Ja=\"タイトル\">\r\n" +
        "\t<Line Type=\"BG\" ResourceKey=\"room\"/>\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"asahi\" Text=\"안녕\" Text_Ja=\"こんにちは\"/>\r\n" +
        "\t<Line Type=\"TEXT\" Name=\"방송 PD\" Text=\"큐!\"/>\r\n" +
        "\t<Line Type=\"TEXT\" Text=\"첫 줄\\n둘째 줄\"/>\r\n" +
        "</Act>\r\n";

    /// <summary>가짜 Claude Code: 받은 요청을 남기고 정해 둔 답을 돌려줌 (Before = 답하기 전에 할 일 — 실행 중 입력 흉내)</summary>
    private sealed class FakeRunner : IClaudeRunner
    {
        public ClaudeRunResult Result = new(true, "[]", null, 0.01, TimeSpan.FromSeconds(3));
        public Action? Before;
        public string? System, Input;
        public int Calls;

        public Task<ClaudeRunResult> RunAsync(string systemPrompt, string input, TimeSpan timeout, CancellationToken cancel)
        {
            Calls++;
            System = systemPrompt;
            Input = input;
            Before?.Invoke();
            return Task.FromResult(Result);
        }
    }

    private sealed class Env : IDisposable
    {
        public TempFolder Temp { get; } = new();
        public ActEditorViewModel Editor { get; }
        public TranslationViewModel Vm { get; }
        public FakeRunner Runner { get; } = new();
        public TranslationGuideStore Guides { get; }
        public GameData Data { get; }

        public Env()
        {
            File.WriteAllText(Temp.File("IL01.xml"), Act);
            Data = new GameData(Path.Combine(AppContext.BaseDirectory, "Data"), () => null, new AppLog());
            Data.Refresh();
            var lib = new SpineLibrary(Temp.File("SpineData"), new SpineManifest());
            lib.Rebuild();
            Editor = new ActEditorViewModel(new AppSettings(), lib, new FakeDialogs(), new ImmediateDispatcher(), () => { }, new AppLog(),
                                            Temp.File("Backups"), characters: Data.Characters);
            Assert.True(Editor.Open(Temp.File("IL01.xml")));
            Guides = new TranslationGuideStore(Temp.File("guides"));
            Vm = new TranslationViewModel(Editor, Data, new TranslationWindowSettings { Language = "ja" }, () => { }, () => { },
                                          new ImmediateDispatcher(), Runner, Guides, () => TimeSpan.FromMinutes(5), new AppLog());
        }

        public ActDocument Doc => Editor.Document!;
        public LanguageInfo Ja => Vm.SelectedLanguage!;
        public ScriptLine Line(int i) => Doc.Lines[i];
        public void Dispose() { Vm.Dispose(); Temp.Dispose(); }
    }

    [Fact]
    public void Targets_SourceButNoTranslation()
    {
        using var env = new Env();
        var ids = PreTranslation.Targets(env.Doc, env.Ja).Select(t => t.Id).ToList();
        // 제목은 번역 있음, 2번 이름은 CharId만(원문 없음), 2번 대사는 번역 있음
        Assert.Equal(["T.subTitle", "L3.Name", "L3.Text", "L4.Text"], ids);
    }

    [Fact]
    public void Input_HasContextAndTodo()
    {
        using var env = new Env();
        var targets = PreTranslation.Targets(env.Doc, env.Ja);
        string input = PreTranslation.Input(env.Doc, env.Ja, env.Data.Characters, targets);
        Assert.Contains("\"speaker\":\"あさひ\"", input);              // 화자 = 그 언어 DB 이름
        Assert.Contains("\"text_translated\":\"こんにちは\"", input);  // 이미 있는 번역도 문맥으로
        Assert.Contains("\"id\":\"L4.Text\",\"source\":\"첫 줄\\\\n둘째 줄\"", input);
        Assert.DoesNotContain("room", input);                          // TEXT가 아닌 줄은 보내지 않음
    }

    [Fact]
    public void SystemPrompt_GuideOptional()
    {
        using var env = new Env();
        Assert.DoesNotContain("# 번역 지침", PreTranslation.SystemPrompt(env.Ja, null));
        Assert.Contains("あさひは～っす", PreTranslation.SystemPrompt(env.Ja, "あさひは～っす"));
    }

    [Fact]
    public void ParseAnswer_ToleratesFenceAndRealNewlines()
    {
        var items = PreTranslation.ParseAnswer("결과입니다\n```json\n[{\"id\":\"L4.Text\",\"value\":\"一行目\n二行目\"},{\"id\":3}]\n```");
        Assert.NotNull(items);
        var item = Assert.Single(items);
        Assert.Equal("一行目\\n二行目", item.Value);
        Assert.Null(PreTranslation.ParseAnswer("죄송합니다"));
        Assert.Null(PreTranslation.ParseAnswer("[{\"id\":"));
    }

    [Fact]
    public async Task PreTranslate_FillsEmptyOnly_OneUndo()
    {
        using var env = new Env();
        env.Runner.Result = env.Runner.Result with
        {
            Text = "[{\"id\":\"T.subTitle\",\"value\":\"サブ\"},{\"id\":\"L3.Name\",\"value\":\"放送PD\"},{\"id\":\"L3.Text\",\"value\":\"キュー！\"}," +
                   "{\"id\":\"L4.Text\",\"value\":\"一行目\\\\n二行目\"},{\"id\":\"L2.Text\",\"value\":\"덮어쓰면 안 됨\"}]",
        };
        await env.Vm.PreTranslateCommand.ExecuteAsync(null);
        Assert.Equal(1, env.Runner.Calls);
        Assert.Equal("サブ", env.Doc.GetRootAttribute("subTitle_Ja"));
        Assert.Equal("放送PD", env.Line(2).Find("Name_Ja"));
        Assert.Equal("一行目\\n二行目", env.Line(3).Find("Text_Ja"));
        Assert.Equal("こんにちは", env.Line(1).Find("Text_Ja"));          // 요청하지 않은 칸은 그대로
        Assert.Contains("가번역 4칸", env.Vm.StatusText);
        Assert.False(env.Vm.IsTranslating);

        Assert.Equal("가번역 4칸", env.Doc.Undo.UndoLabel);
        env.Doc.UndoLast();                                                // 한 번에 모두
        Assert.Null(env.Doc.GetRootAttribute("subTitle_Ja"));
        Assert.Null(env.Line(2).Find("Name_Ja"));
        Assert.Null(env.Line(3).Find("Text_Ja"));
    }

    [Fact]
    public async Task PreTranslate_KeepsWhatUserTypedMeanwhile()
    {
        using var env = new Env();
        env.Runner.Before = () => env.Vm.Rows[2].Second.Value = "直接入力";   // 실행 중에 3번 대사를 직접 입력
        env.Runner.Result = env.Runner.Result with
        {
            Text = "[{\"id\":\"L3.Text\",\"value\":\"キュー！\"},{\"id\":\"L4.Text\",\"value\":\"一行目\\\\n二行目\"}]",
        };
        await env.Vm.PreTranslateCommand.ExecuteAsync(null);
        Assert.Equal("直接入力", env.Line(2).Find("Text_Ja"));
        Assert.Equal("一行目\\n二行目", env.Line(3).Find("Text_Ja"));
        Assert.Contains("입력한 칸 1개", env.Vm.StatusText);
        Assert.Contains("답이 없던 칸 2", env.Vm.StatusText);             // 제목·3번 이름
    }

    [Fact]
    public async Task PreTranslate_FailureChangesNothing()
    {
        using var env = new Env();
        env.Runner.Result = new(false, null, "취소했습니다", null, TimeSpan.FromSeconds(1));
        await env.Vm.PreTranslateCommand.ExecuteAsync(null);
        Assert.Contains("가번역 실패: 취소했습니다", env.Vm.StatusText);
        Assert.Null(env.Doc.GetRootAttribute("subTitle_Ja"));

        env.Runner.Result = new(true, "번역할 수 없습니다", null, null, TimeSpan.FromSeconds(1));
        await env.Vm.PreTranslateCommand.ExecuteAsync(null);
        Assert.Contains("읽지 못했습니다", env.Vm.StatusText);
        Assert.Null(env.Doc.GetRootAttribute("subTitle_Ja"));
    }

    [Fact]
    public async Task PreTranslate_SendsGuideFile_WhenPresent()
    {
        using var env = new Env();
        await env.Vm.PreTranslateCommand.ExecuteAsync(null);
        Assert.DoesNotContain("# 번역 지침", env.Runner.System);
        env.Guides.Save("ja", "あさひは～っす");
        await env.Vm.PreTranslateCommand.ExecuteAsync(null);
        Assert.Contains("あさひは～っす", env.Runner.System);
    }

    [Fact]
    public async Task PreTranslate_NothingToDo_DoesNotRun()
    {
        using var env = new Env();
        env.Vm.Rows[0].Second.Value = "サブ";
        env.Vm.Rows[2].First.Value = "PD";
        env.Vm.Rows[2].Second.Value = "キュー";
        env.Vm.Rows[3].Second.Value = "一\\n二";
        await env.Vm.PreTranslateCommand.ExecuteAsync(null);
        Assert.Equal(0, env.Runner.Calls);
        Assert.Contains("빈 칸이 없습니다", env.Vm.StatusText);
    }

    [Fact]
    public void NewlineMismatch_RedWarning()
    {
        using var env = new Env();
        var cell = env.Vm.Rows[3].Second;                                  // 원문 \n 1개
        cell.Value = "一行";
        Assert.True(cell.NewlineMismatch);
        Assert.True(cell.Warning);
        Assert.Contains("원문 1개, 번역 0개", cell.ToolTip);
        cell.Value = "一\\n二";
        Assert.False(cell.Warning);
        Assert.False(env.Vm.Rows[2].Second.Warning);                       // 빈 칸은 경고 없음
    }

    [Fact]
    public void GuideEditor_SaveCreatesFile()
    {
        using var env = new Env();
        var g = env.Vm.CreateGuideEditor()!;
        Assert.False(g.Exists);
        Assert.False(g.IsDirty);
        g.Text = "# 지침";
        Assert.True(g.IsDirty);
        Assert.True(g.Save());
        Assert.False(g.IsDirty);
        Assert.Equal("# 지침", env.Guides.Load("ja"));
        Assert.EndsWith("TranslationGuide_ja.md", g.FilePath);
        Assert.Equal("# 지침", env.Vm.CreateGuideEditor()!.Text);
    }

    [Fact]
    public void ClaudeParse_ReadsResultAndCost()
    {
        var ok = ClaudeCli.Parse(0, "{\"type\":\"result\",\"is_error\":false,\"result\":\"[]\",\"total_cost_usd\":0.0123}", "", TimeSpan.Zero);
        Assert.True(ok.Ok);
        Assert.Equal("[]", ok.Text);
        Assert.Equal(0.0123, ok.CostUsd);
        var err = ClaudeCli.Parse(1, "{\"is_error\":true,\"result\":\"Not logged in\"}", "", TimeSpan.Zero);
        Assert.False(err.Ok);
        Assert.Contains("Not logged in", err.Error);
        var bad = ClaudeCli.Parse(1, "", "claude: command failed", TimeSpan.Zero);
        Assert.False(bad.Ok);
        Assert.Contains("command failed", bad.Error);
    }

    [Fact]
    public async Task ClaudeCli_MissingExe_ClearError()
    {
        using var temp = new TempFolder();
        var cli = new ClaudeCli(() => temp.File("없음\\claude.exe"), new AppLog());
        var r = await cli.RunAsync("s", "i", TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("찾지 못했습니다", r.Error);
    }
}
