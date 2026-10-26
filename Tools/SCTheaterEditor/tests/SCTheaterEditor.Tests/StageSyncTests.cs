using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>4-2: 스크립트 → 무대 계산 (게임 규칙), 캐릭터 이름 데이터</summary>
public class StageStateCalculatorTests
{
    private static ScriptLine L(params (string Name, string Value)[] attrs) => ScriptLine.Create(attrs);

    private static ScriptLine Text(string cg, string pos = "", string anim = "") =>
        L(("Type", "TEXT"), ("Name", ""), ("Text", "…"), ("CG", cg), ("Position", pos), ("Animation", anim));

    [Fact]
    public void Transition_ClearsVisible_ButKeepsMemory()
    {
        var lines = new[]
        {
            L(("Type", "BG"), ("ResourceKey", "bg_office")),
            Text("a", "left", "smile1 face_smile"),
            Text("b", "right", "wait"),
            L(("Type", "TRANSITION"), ("Effect", "fade")),
            Text("a"),   // 위치·애니메이션 비움 → 기억한 값
        };
        var before = StageStateCalculator.Compute(lines, 2);
        Assert.Equal("bg_office", before.Background);
        Assert.Equal(new[] { "a", "b" }, before.Actors.Select(a => a.CgKey));

        var s = StageStateCalculator.Compute(lines, 4);
        Assert.Null(s.Background);
        var a = Assert.Single(s.Actors);
        Assert.Equal(new StageActorState("a", "left", "smile1 face_smile"), a);
        Assert.Empty(s.Notes);
    }

    [Fact]
    public void None_And_Clean()
    {
        var lines = new[]
        {
            Text("a", "left"), Text("b", "right"),
            L(("Type", "SETCG"), ("CG", "a"), ("Effect", "clean")),
            Text("none"),
        };
        Assert.Equal(new[] { "b" }, StageStateCalculator.Compute(lines, 2).Actors.Select(a => a.CgKey));
        Assert.Empty(StageStateCalculator.Compute(lines, 3).Actors);
    }

    [Fact]
    public void Group_ShowsMembers_AndSurvivesTransition()
    {
        var lines = new[]
        {
            L(("Type", "CGGROUP"), ("Name", "g1"), ("CG", "a"), ("Position", "left"), ("Animation", "wait")),
            L(("Type", "CGGROUP"), ("Name", "g1"), ("CG", "b"), ("Position", "right"), ("Animation", "wait")),
            Text("g1"),
            L(("Type", "TRANSITION")),
            Text("g1"),
        };
        var s = StageStateCalculator.Compute(lines, 2);
        Assert.Equal(new[] { ("a", "left"), ("b", "right") }, s.Actors.Select(a => (a.CgKey, a.Position)));

        // 게임과 같이 그룹은 막 동안 유지 → TRANSITION 뒤에도 g1 = 멤버 a·b (미결 #7)
        var after = StageStateCalculator.Compute(lines, 4);
        Assert.Equal(new[] { ("a", "left"), ("b", "right") }, after.Actors.Select(a => (a.CgKey, a.Position)));
        Assert.Equal(new[] { "a", "b" }, after.LineActors);
        Assert.Empty(after.Notes);
    }

    [Fact]
    public void MissingOrUnknownPosition_FallsBackToCenter_WithNote()
    {
        var lines = new[] { Text("a"), Text("b", "nowhere") };
        var s = StageStateCalculator.Compute(lines, 1);
        Assert.All(s.Actors, a => Assert.Equal("center", a.Position));
        Assert.Null(s.Actors[0].Animation);
        Assert.Equal(2, s.Notes.Count);
        Assert.Contains("위치 없음", s.Notes[0]);
        Assert.Contains("모르는 위치 'nowhere'", s.Notes[1]);
    }

    [Fact]
    public void UnknownPosition_UsesRememberedPosition()
    {
        var lines = new[] { Text("a", "left"), Text("a", "nowhere", "smile1") };
        var a = Assert.Single(StageStateCalculator.Compute(lines, 1).Actors);
        Assert.Equal(new StageActorState("a", "left", "smile1"), a);
    }

    [Fact]
    public void CharacterData_Embedded()
    {
        var data = CharacterData.LoadEmbedded();
        Assert.Equal(29, data.Count);
        Assert.Equal("아사히", data.Find("ASAHI")?.Ko);
        Assert.Equal("토오루", data.KoreanName("toru"));
        Assert.Null(data.KoreanName("nobody"));
    }
}

/// <summary>4-2: 라인에 적용 · 줄 추가 (스크립트 표 쪽)</summary>
public class StampTests
{
    private sealed class Env : IDisposable
    {
        public readonly TempFolder Temp = new();
        public readonly ActEditorViewModel Vm;
        public readonly List<(ScriptRow Row, string Attr)> Focus = new();

        public Env()
        {
            var settings = new AppSettings();
            var path = Path.Combine(Temp.Path, "IL01.xml");
            File.WriteAllText(path, ActXmlFormatTests.Sample);
            var lib = new SpineLibrary(Path.Combine(Temp.Path, "SpineData"), new SpineManifest());
            lib.Rebuild();
            Vm = new ActEditorViewModel(settings, lib, new FakeDialogs(), new ImmediateDispatcher(), () => { }, new AppLog(),
                                        Path.Combine(Temp.Path, "Backups"), () => new DateTime(2026, 10, 15, 12, 0, 0));
            Vm.FocusFieldRequested += (row, attr) => Focus.Add((row, attr));
            Vm.Open(path);
        }

        public void Dispose() => Temp.Dispose();
    }

    private static readonly StageStamp Madoka = new("madoka_x", "left", "wait face_smile", "madoka");

    [Fact]
    public void ApplyStamp_Text_KeepsExistingName_OneUndo()
    {
        using var env = new Env();
        var row = env.Vm.Rows[2];   // TEXT Name=아사히
        env.Vm.SetSelection([row]);
        env.Vm.ApplyStamp(Madoka);

        Assert.Equal("아사히", row.Get("Name"));
        Assert.Equal("madoka_x", row.Get("CG"));
        Assert.Equal("left", row.Get("Position"));
        Assert.Equal("wait face_smile", row.Get("Animation"));
        Assert.Equal((row, "Text"), env.Focus.Single());

        env.Vm.UndoCommand.Execute(null);
        Assert.Equal("asahi_idol_3", env.Vm.Rows[2].Get("CG"));
        Assert.Equal("right", env.Vm.Rows[2].Get("Position"));
    }

    [Fact]
    public void ApplyStamp_FillsEmptyName_AndRejectsOtherTypes()
    {
        using var env = new Env();
        env.Vm.SetSelection([env.Vm.Rows[1]]);   // BG
        env.Vm.ApplyStamp(Madoka);
        Assert.Contains("적용할 수 없습니다", env.Vm.StatusText);
        Assert.Equal("", env.Vm.Rows[1].Get("CG"));

        env.Vm.AddTextCommand.Execute(null);   // 무대 선택 없음 → 빈 TEXT 줄
        var added = env.Vm.Rows[2];
        Assert.Equal("", added.Get("Name"));
        env.Vm.SetSelection([added]);
        env.Vm.ApplyStamp(Madoka);
        Assert.Equal(CharacterData.LoadEmbedded().KoreanName("madoka"), added.Get("Name"));
    }

    [Fact]
    public void AddText_WithStamp_PrefillsAndFocusesText()
    {
        using var env = new Env();
        env.Vm.StampProvider = () => Madoka;
        env.Vm.SetSelection([env.Vm.Rows[2]]);
        env.Vm.AddTextCommand.Execute(null);

        var added = env.Vm.Rows[3];
        Assert.Equal("TEXT", added.Type);
        Assert.Equal("madoka_x", added.Get("CG"));
        Assert.Equal("left", added.Get("Position"));
        Assert.Equal("wait face_smile", added.Get("Animation"));
        Assert.Equal("1", added.Get("SpeakerType"));
        Assert.NotEqual("", added.Get("Name"));
        Assert.Equal((added, "Text"), env.Focus.Last());
        Assert.Equal(added, env.Vm.Selection.Single());
    }
}
