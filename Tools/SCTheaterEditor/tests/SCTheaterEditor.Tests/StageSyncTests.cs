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

    //[스테이지 레이아웃] 변경: 자리는 공유 StagePlanner ("N-i") — 아래 테스트의 left·right·center는 3-1·3-3·3-2로
    private static StageSnapshot Compute(IReadOnlyList<ScriptLine> lines, int index) => StageStateCalculator.Compute(lines, index, TestData.Stage());

    private static ScriptLine Zoom(string cg, string value, string pos = "") =>
        L(("Type", "TEXT"), ("Name", ""), ("Text", "…"), ("CG", cg), ("Position", pos), ("Effect", "zoom"), ("Value", value));

    [Fact]
    public void Transition_ClearsVisible_KeepsAnimation_ReEnterIsAutoPlaced()
    {
        var lines = new[]
        {
            L(("Type", "BG"), ("ResourceKey", "bg_office")),
            Text("a", "3-1", "smile1 face_smile"),
            Text("b", "3-3", "wait"),
            L(("Type", "TRANSITION"), ("Effect", "fade")),
            Text("a"),   // 위치·애니메이션 비움 → 애니메이션은 기억, 자리는 새 등장이라 자동 배치 (게임과 같음)
        };
        var before = Compute(lines, 2);
        Assert.Equal("bg_office", before.Background);
        Assert.Equal(new[] { "a", "b" }, before.Actors.Select(a => a.CgKey));

        var s = Compute(lines, 4);
        Assert.Null(s.Background);
        var a = Assert.Single(s.Actors);
        Assert.Equal(new StageActorState("a", "1-1", "smile1 face_smile"), a);
        Assert.Contains("Position 없음", Assert.Single(s.Notes));
    }

    [Fact]
    public void None_And_Clean()
    {
        var lines = new[]
        {
            Text("a", "3-1"), Text("b", "3-3"),
            L(("Type", "SETCG"), ("CG", "a"), ("Effect", "clean")),
            Text("none"),
        };
        Assert.Equal(new[] { "b" }, Compute(lines, 2).Actors.Select(a => a.CgKey));
        Assert.Empty(Compute(lines, 3).Actors);
    }

    [Fact]
    public void Group_ShowsMembers_AndSurvivesTransition()
    {
        var lines = new[]
        {
            L(("Type", "CGGROUP"), ("GroupId", "g1"), ("CG", "a"), ("Position", "3-1"), ("Animation", "wait")),
            L(("Type", "CGGROUP"), ("GroupId", "g1"), ("CG", "b"), ("Position", "3-3"), ("Animation", "wait")),
            Text("g1"),
            L(("Type", "TRANSITION")),
            Text("g1"),
        };
        var s = Compute(lines, 2);
        Assert.Equal(new[] { ("a", "3-1"), ("b", "3-3") }, s.Actors.Select(a => (a.CgKey, a.Position)));

        // 게임과 같이 그룹은 막 동안 유지 → TRANSITION 뒤에도 g1 = 멤버 a·b (미결 #7)
        var after = Compute(lines, 4);
        Assert.Equal(new[] { ("a", "3-1"), ("b", "3-3") }, after.Actors.Select(a => (a.CgKey, a.Position)));
        Assert.Equal(new[] { "a", "b" }, after.LineActors);
        Assert.Empty(after.Notes);
    }

    [Fact]
    public void EmptyPosition_AutoPlaced_InvalidPosition_Skipped()   //[스테이지 레이아웃] 변경: 이전 'center 대체 + 안내'
    {
        var lines = new[] { Text("a"), Text("b", "nowhere") };
        var s = Compute(lines, 1);
        Assert.Equal(new StageActorState("a", "1-1", null), Assert.Single(s.Actors));   // b는 Pos 오류 → 건너뜀
        Assert.Equal(2, s.Notes.Count);
        Assert.Contains("Position 없음", s.Notes[0]);
        Assert.Contains("\"nowhere\" 오류", s.Notes[1]);
        Assert.Empty(s.LineActors!);
    }

    [Fact]
    public void Existing_InvalidPosition_StaysInPlace()   //[스테이지 레이아웃] 변경: 이전 '모르는 위치 = 기억한 위치'
    {
        var lines = new[] { Text("a", "3-1"), Text("a", "nowhere", "smile1") };
        var a = Assert.Single(Compute(lines, 1).Actors);
        Assert.Equal(new StageActorState("a", "3-1", null), a);   // 줄 전체를 건너뜀 (애니메이션도) — 게임과 같음
    }

    [Fact]
    public void Order_LatestCgIsFront()   //[스테이지 레이아웃] 추가: Actors = 뒤 → 앞
    {
        var lines = new[] { Text("a", "3-1"), Text("b", "3-3"), Text("a") };
        Assert.Equal(new[] { "a", "b" }, Compute(lines, 1).Actors.Select(a => a.CgKey));
        Assert.Equal(new[] { "b", "a" }, Compute(lines, 2).Actors.Select(a => a.CgKey));
    }

    [Fact]
    public void Zoom_KeptOnNextCgLine_ResetByCleanTransitionAndValueOne()   //[스테이지 레이아웃] 추가
    {
        var lines = new[]
        {
            Text("a", "3-1"),
            Zoom("a", "1.7"),
            Text("a", "", "smile1"),                                   // 다음 CG 줄 — 줌 유지
            Zoom("a", "1"),                                            // Value = 1로 되돌림
            Zoom("a", "1.5"),
            L(("Type", "SETCG"), ("CG", "a"), ("Effect", "clean")),
            Text("a", "3-1"),                                          // 다시 올림 → 1
            Zoom("a", "2"),
            L(("Type", "TRANSITION")),
            Text("a", "3-1"),
        };
        Assert.Equal(1.7, Compute(lines, 1).Actors[0].Zoom);
        Assert.Equal(1.7, Compute(lines, 2).Actors[0].Zoom);
        Assert.Equal(1, Compute(lines, 3).Actors[0].Zoom);
        Assert.Equal(1, Compute(lines, 6).Actors[0].Zoom);
        Assert.Equal(2, Compute(lines, 7).Actors[0].Zoom);
        Assert.Equal(1, Compute(lines, 9).Actors[0].Zoom);
    }

    [Fact]
    public void Zoom_OnGroup_Ignored_WithNote()   //[스테이지 레이아웃] 추가: 게임처럼 반영하지 않음 (미결 #8)
    {
        var lines = new[]
        {
            L(("Type", "CGGROUP"), ("GroupId", "g1"), ("CG", "a"), ("Position", "3-1")),
            L(("Type", "CGGROUP"), ("GroupId", "g1"), ("CG", "b"), ("Position", "3-3")),
            Zoom("g1", "2"),
        };
        var s = Compute(lines, 2);
        Assert.All(s.Actors, a => Assert.Equal(1, a.Zoom));
        Assert.Contains(s.Notes, n => n.Contains("CGGROUP 줌"));
    }

    [Fact]
    public void CharacterData_Copy()   // 변경(번역 모드 2회차): 이름 (이전 CharacterData_Embedded)
    {
        var data = TestData.Characters();   // 변경(번역 모드 2회차): LoadEmbedded → 시험용 복사본
        Assert.Equal(30, data.Count);                        // 변경(다국어): producer 추가
        Assert.Equal("아사히", data.Find("ASAHI")?.Name);     // 변경(다국어): .Ko → .Name
        Assert.Equal("あさひ", data.Find("asahi")?.NameJa);   // 추가(다국어): 게임 원본 복사본 (짧은 이름)
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
                                        Path.Combine(Temp.Path, "Backups"), () => new DateTime(2026, 10, 15, 12, 0, 0),
                                        characters: TestData.Characters());   // 추가(번역 모드 2회차): 내장 복사본이 없어져 시험용 복사본을 넘김
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
        Assert.Equal("", added.Get("CharId"));
        env.Vm.SetSelection([added]);
        env.Vm.ApplyStamp(Madoka);
        Assert.Equal("madoka", added.Get("CharId"));   // 변경(다국어): 이름 대신 화자(CharId)
        Assert.Equal("", added.Get("Name"));
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
        Assert.Equal("madoka", added.Get("CharId"));   // 변경(다국어): 이름 대신 화자(CharId)
        Assert.Equal((added, "Text"), env.Focus.Last());
        Assert.Equal(added, env.Vm.Selection.Single());
    }
}
