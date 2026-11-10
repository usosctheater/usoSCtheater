using UsoSCTheater.Shared;

namespace SCTheaterEditor.Tests;

/// <summary>추가(무대 배치 재정립 2회차): 공유 무대 규칙 — 공식·Pos 해석·StageData 해석·자리 규칙·줌 기준점</summary>
public class SharedStageTests
{
    // 시험용 표 = StageData 첫 값 (3·5명 = 이전 CGManager 값, 1·2·4·6명 = 임시값)
    private static readonly StageTable Table = new(new[]
    {
        new StageRow(1, 1440, 100, -180), new StageRow(2, 960, 100, -180), new StageRow(3, 1440, 100, -180),
        new StageRow(4, 1920, 70, -180), new StageRow(5, 1920, 60, -180), new StageRow(6, 1920, 50, -180),
    }, new StageGuide(0, -381, 1344, 259));

    [Theory]
    [InlineData(3, 1, -480)] [InlineData(3, 2, 0)] [InlineData(3, 3, 480)]
    [InlineData(5, 1, -768)] [InlineData(5, 2, -384)] [InlineData(5, 3, 0)] [InlineData(5, 4, 384)] [InlineData(5, 5, 768)]
    [InlineData(1, 1, 0)] [InlineData(2, 1, -240)] [InlineData(4, 1, -720)] [InlineData(6, 1, -800)] [InlineData(6, 6, 800)]
    public void SlotX(int n, int i, double x)
    {
        Assert.True(Table.TryGetRow(n, out var row));
        Assert.Equal(x, row.SlotX(i), 6);
    }

    [Theory]
    [InlineData("3-1", 3, 1)] [InlineData(" 5-3 ", 5, 3)] [InlineData("3-4", 3, 4)] [InlineData("1000-1", 1000, 1)]   // 범위·표는 Planner
    public void TryParsePos_Format(string text, int n, int i)
    {
        Assert.True(StageLayout.TryParsePos(text, out int cn, out int ci));
        Assert.Equal((n, i), (cn, ci));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("left")] [InlineData("3")] [InlineData("3-")] [InlineData("-1")]
    [InlineData("3-1-2")] [InlineData("a-1")] [InlineData("3 - 1")] [InlineData("+3-1")]
    public void TryParsePos_Invalid(string? text) => Assert.False(StageLayout.TryParsePos(text, out _, out _));

    [Fact]
    public void Parse_ReadsRowsAndGuide()
    {
        var w = new List<string>();
        var t = StageTable.Parse("""
            <StageData>
              <Row Count="3" Width="1440" Scale="100" BaseY="-180" />
              <Row Count="6" Width="1920" Scale="50" BaseY="-180" />
              <DialogGuide X="0" Y="-381" Width="1344" Height="259" />
            </StageData>
            """, w);
        Assert.Empty(w);
        Assert.Equal(new[] { 3, 6 }, t.Rows.Select(r => r.Count));
        Assert.Equal(50, t.Rows[1].Scale);
        Assert.Equal(-381, t.DialogGuide!.Y);
    }

    [Fact]
    public void Parse_Problems_WarnInDetail()
    {
        var w = new List<string>();
        Assert.Empty(StageTable.Parse(null, w).Rows);
        Assert.Empty(StageTable.Parse("<StageData>", w).Rows);
        var t = StageTable.Parse("""
            <StageData>
              <Row Count="3" Width="x" Scale="100" BaseY="-180" />
              <Row Count="2" Width="960" Scale="100" BaseY="-180" />
              <Row Count="2" Width="1" Scale="1" BaseY="1" />
            </StageData>
            """, w);
        Assert.Equal(new[] { 2 }, t.Rows.Select(r => r.Count));
        Assert.Equal(960, t.Rows[0].Width);
        Assert.Null(t.DialogGuide);
        Assert.Equal(5, w.Count);   // 빈 문서 · XML 오류 · Row 값 오류 · 중복 · DialogGuide 없음
    }

    private static (StagePlanner p, List<string> w) Planner() => (new StagePlanner(Table), new List<string>());
    private static string Pos(StagePlanner p, string key) => p.TryGetPlacement(key, out var pl) ? pl.Pos : "(없음)";
    private static bool Line(StagePlanner p, List<string> w, string key, string? pos) { p.StartLine(); return p.Show(key, pos, w); }

    // 명시는 인원과 관계없이 그대로 (1명이어도 5-3 = 5분할 크기 정중앙)
    [Fact]
    public void Explicit_KeptAsWritten()
    {
        var (p, w) = Planner();
        Line(p, w, "a", "5-3");
        Assert.True(p.TryGetPlacement("a", out var a));
        Assert.Equal((0d, 60d, -180d), (a.X, a.Scale, a.BaseY));
        Line(p, w, "b", "5-1");
        Assert.Equal("5-3", Pos(p, "a"));
        Assert.Empty(w);
    }

    // 3-1·3-3이 차 있으면 자동은 3-2
    [Fact]
    public void Auto_FillsGapBetweenExplicit()
    {
        var (p, w) = Planner();
        Line(p, w, "a", "3-1");
        Line(p, w, "b", "3-3");
        Line(p, w, "c", "");
        Assert.Equal("3-2", Pos(p, "c"));
        Assert.Empty(w);
    }

    // 다른 N 명시는 건너뜀 + 경고 / 혼자 명시된 캐릭터의 분할 변경은 허용
    [Fact]
    public void DifferentSplit_SkippedUnlessAlone()
    {
        var (p, w) = Planner();
        Line(p, w, "a", "3-1");
        Assert.False(Line(p, w, "b", "5-5"));
        Assert.False(p.Contains("b"));
        Assert.Single(w);
        Assert.True(Line(p, w, "a", "5-3"));
        Assert.Equal("5-3", Pos(p, "a"));
        Assert.Single(w);
    }

    // 자동만 있으면 인원 수 기준 — 등장과 함께 기존 자동 캐릭터도 바로 이동 / 퇴장 때는 옮기지 않음
    [Fact]
    public void AutoOnly_RelayoutOnEnter_NotOnLeave()
    {
        var (p, w) = Planner();
        Line(p, w, "a", null);
        Assert.Equal("1-1", Pos(p, "a"));
        Line(p, w, "b", null);
        Assert.Equal(("2-1", "2-2"), (Pos(p, "a"), Pos(p, "b")));
        Line(p, w, "c", null);
        Assert.Equal(("3-1", "3-2", "3-3"), (Pos(p, "a"), Pos(p, "b"), Pos(p, "c")));
        p.Remove("b");
        Assert.Equal(("3-1", "3-3"), (Pos(p, "a"), Pos(p, "c")));
        Line(p, w, "d", null);   // 등장 순서대로 (예외 처리 없음) → c가 3-2로
        Assert.Equal(("3-1", "3-2", "3-3"), (Pos(p, "a"), Pos(p, "c"), Pos(p, "d")));
        Assert.Empty(w);
    }

    // 이미 무대에 있는 캐릭터의 빈 Pos = 자리 유지, 앞으로만
    [Fact]
    public void Existing_EmptyPos_KeepsSlot_MovesToFront()
    {
        var (p, w) = Planner();
        Line(p, w, "a", "3-1");
        Line(p, w, "b", "3-3");
        Assert.True(Line(p, w, "a", ""));
        Assert.Equal("3-1", Pos(p, "a"));
        Assert.Equal(new[] { "b", "a" }, p.Placements().Select(x => x.Key));   // 뒤 → 앞
    }

    // 자동만 있는 무대에 명시가 오면 명시 쪽 N으로 자동 캐릭터가 옮김
    [Fact]
    public void ExplicitJoinsAutos_AutosRefit()
    {
        var (p, w) = Planner();
        Line(p, w, "a", null);
        Line(p, w, "b", null);
        Line(p, w, "c", "3-2");
        Assert.Equal(("3-1", "3-3"), (Pos(p, "a"), Pos(p, "b")));
        Assert.Empty(w);
    }

    // CGGROUP 멤버(같은 줄)는 같은 앞뒤 값, TRANSITION(Clear) 뒤에는 처음부터
    [Fact]
    public void SameLine_SameOrder_ClearResets()
    {
        var (p, w) = Planner();
        p.StartLine();
        p.Show("a", "3-1", w);
        p.Show("b", "3-3", w);
        Assert.Single(p.Placements().Select(x => x.Order).Distinct());
        p.Clear();
        Line(p, w, "c", "5-5");
        Assert.Equal("5-5", Pos(p, "c"));
        Assert.Empty(w);
    }

    // Pos 오류는 경고 후 건너뜀 (옛 표기 포함)
    [Theory]
    [InlineData("left")] [InlineData("3-4")] [InlineData("3-0")] [InlineData("9-1")] [InlineData("x")]
    public void InvalidPos_Skipped(string pos)
    {
        var (p, w) = Planner();
        Assert.False(Line(p, w, "a", pos));
        Assert.False(p.Contains("a"));
        Assert.Single(w);
    }

    // 빈 칸이 없으면 N을 넘는 칸(화면 밖) + 경고 / 같은 칸 명시 두 명은 경고 없음
    [Fact]
    public void Overflow_OffScreen_SameSlotSilent()
    {
        var (p, w) = Planner();
        Line(p, w, "a", "3-1");
        Line(p, w, "b", "3-2");
        Line(p, w, "c", "3-3");
        Line(p, w, "d", "");
        Assert.True(p.TryGetPlacement("d", out var d));
        Assert.Equal(4, d.Index);
        Assert.Equal(960, d.X, 6);
        Assert.Single(w);
        Line(p, w, "e", "3-2");
        Assert.Equal(("3-2", "3-2"), (Pos(p, "b"), Pos(p, "e")));
    }

    // 자동만 7명 → 가장 큰 행(6분할) + 7번째는 화면 밖
    [Fact]
    public void AutoBeyondTable_UsesLargestRow()
    {
        var (p, w) = Planner();
        foreach (var k in new[] { "a", "b", "c", "d", "e", "f", "g" }) Line(p, w, k, null);
        Assert.Equal(("6-1", "6-6", "6-7"), (Pos(p, "a"), Pos(p, "f"), Pos(p, "g")));
        Assert.Equal(2, w.Count);   // 7명 행 없음 · 빈 칸 없음
    }

    [Fact]
    public void EmptyTable_NothingShown()
    {
        var p = new StagePlanner(new StageTable(Array.Empty<StageRow>(), null));
        var w = new List<string>();
        Assert.False(Line(p, w, "a", null));
        Assert.Single(w);
    }

    // 기준점 300에서 1.5배 → 자식을 −150 옮기면 기준점 위치 = −150 + 300 × 1.5 = 300
    [Fact]
    public void ZoomOffset_KeepsPivot()
    {
        Assert.Equal(-150, StageLayout.ZoomOffset(300, 1.5), 6);
        Assert.Equal(0, StageLayout.ZoomOffset(300, 1), 6);
        Assert.Equal(300, StageLayout.ZoomOffset(300, 1.5) + 300 * 1.5, 6);
    }
}
