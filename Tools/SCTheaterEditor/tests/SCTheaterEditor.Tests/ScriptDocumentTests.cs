using System.Text;
using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>4-1: 막 XML 읽기·쓰기, 문서 편집·되돌리기, 백업·목록, 스크립트 표 ViewModel</summary>
public class ActXmlFormatTests
{
    // 기존 막 파일과 같은 모양: CRLF, 마지막 줄바꿈 없음, 루트 <Scene … >(> 앞 공백), 탭 + 한 줄
    internal static readonly string[] SampleLines =
    [
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>",
        "<Scene MainTitle=\"InertiaLucis\" SubTitle=\"힘의 법칙\" >",
        "\t<Line Type=\"AUDIO\" Value=\"bgm\" Track=\"BGM_2\" Volume=\"0.4\"/>",
        "\t<Line Type=\"BG\" Key=\"bg_office_day\"/>",
        "\t<Line Type=\"TEXT\" Name=\"아사히\" Text=\"무리임다\\n그런 거~\" Voice=\"s001085\" CG=\"asahi_idol_3\" Position=\"right\" Animation=\"anger1 face_anger\" SpeakerType=\"1\"/>",
        "\t<Line Type=\"TRANSITION\" Effect=\"wipe_left\" SE=\"se_trans_out|se_trans_in\"/>",
        "</Scene>",
    ];

    internal static string Sample => string.Join("\r\n", SampleLines);

    [Fact]
    public void RoundTrip_ChangesOnlyRootLine()
    {
        var doc = ActXmlFormat.Parse(Sample);
        Assert.True(doc.PreservesLayout);
        Assert.Equal("Scene", doc.SourceRootName);
        Assert.Equal("InertiaLucis", doc.MainTitle);
        Assert.Equal("힘의 법칙", doc.SubTitle);
        Assert.Equal(4, doc.Lines.Count);

        var expected = SampleLines.ToArray();
        expected[1] = "<Act mainTitle=\"InertiaLucis\" subTitle=\"힘의 법칙\">";
        expected[^1] = "</Act>";
        Assert.Equal(string.Join("\r\n", expected), ActXmlFormat.ToText(doc));
    }

    [Fact]
    public void TextKeepsBackslashN_AsWritten()
    {
        var doc = ActXmlFormat.Parse(Sample);
        Assert.Equal("무리임다\\n그런 거~", doc.Lines[2].Get("Text"));   // 게임이 읽을 때 줄바꿈으로 바꿈
    }

    [Fact]
    public void LfAndBomAndTrailingNewline_Preserved()
    {
        string text = string.Join("\n", SampleLines) + "\n";
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(text)];
        var doc = ActXmlFormat.Parse(bytes);
        byte[] saved = ActXmlFormat.ToBytes(doc);
        Assert.True(saved.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        string s = Encoding.UTF8.GetString(saved, 3, saved.Length - 3);
        Assert.DoesNotContain("\r\n", s);
        Assert.EndsWith("</Act>\n", s);
    }

    [Fact]
    public void UnknownAttributesTypesAndComments_Preserved()
    {
        string text = string.Join("\r\n",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<Act mainTitle=\"t\" subTitle=\"s\" Extra=\"1\">",
            "\t<!-- 메모 -->",
            "",
            "\t<Line Type=\"FUTURE\" Foo=\"bar\"/>",
            "\t<Line Type=\"TEXT\" Name=\"a\" Text=\"b\" Zoom=\"2\"/>",
            "\t<!-- 끝 -->",
            "</Act>");
        var doc = ActXmlFormat.Parse(text);
        Assert.True(doc.PreservesLayout);
        Assert.Equal(text, ActXmlFormat.ToText(doc));

        // 라인을 고쳐도 주석·모르는 속성은 그대로
        doc.SetAttribute(doc.Lines[1], "Name", "c");
        string saved = ActXmlFormat.ToText(doc);
        Assert.Contains("\t<!-- 메모 -->\r\n\r\n\t<Line Type=\"FUTURE\" Foo=\"bar\"/>", saved);
        Assert.Contains("\t<Line Type=\"TEXT\" Name=\"c\" Text=\"b\" Zoom=\"2\"/>", saved);
        Assert.Contains("Extra=\"1\"", saved);
    }

    [Fact]
    public void NewAttribute_GoesToFileOrder()
    {
        var doc = ActXmlFormat.Parse(Sample);
        var line = ScriptLine.Create(("Type", "TEXT"), ("Name", "A"), ("Text", "t"), ("Position", "left"), ("SpeakerType", "1"));
        doc.InsertLines(0, [line]);
        doc.SetAttribute(line, "CG", "x");
        doc.SetAttribute(line, "Voice", "v1");
        Assert.Equal("\t<Line Type=\"TEXT\" Name=\"A\" Text=\"t\" Voice=\"v1\" CG=\"x\" Position=\"left\" SpeakerType=\"1\"/>", line.ToXmlLine());
    }

    [Fact]
    public void Escape_RoundTrips()
    {
        var doc = ActXmlFormat.Parse(Sample);
        doc.SetAttribute(doc.Lines[2], "Text", "a & \"b\" <c>");
        string saved = ActXmlFormat.ToText(doc);
        Assert.Contains("Text=\"a &amp; &quot;b&quot; &lt;c&gt;\"", saved);
        var again = ActXmlFormat.Parse(saved);
        Assert.Equal("a & \"b\" <c>", again.Lines[2].Get("Text"));
    }

    [Fact]
    public void MultiLineElement_FallsBackToStandardLayout()
    {
        string text = "<?xml version=\"1.0\"?>\r\n<Scene MainTitle=\"t\" SubTitle=\"s\">\r\n\t<Line Type=\"TEXT\"\r\n\t      Name=\"a\"/>\r\n\t<!-- c -->\r\n</Scene>";
        var doc = ActXmlFormat.Parse(text);
        Assert.False(doc.PreservesLayout);
        Assert.Single(doc.Lines);
        Assert.Equal(string.Join("\r\n",
            ActXmlFormat.DefaultDeclaration,
            "<Act mainTitle=\"t\" subTitle=\"s\">",
            "\t<Line Type=\"TEXT\" Name=\"a\"/>",
            "\t<!-- c -->",
            "</Act>"), ActXmlFormat.ToText(doc));
    }

    [Fact]
    public void BadXml_Throws()
    {
        Assert.Throws<ActFormatException>(() => ActXmlFormat.Parse("<Act><Line Type=\"TEXT\"></Act>"));
        Assert.Throws<ActFormatException>(() => ActXmlFormat.Parse("<Root/>"));
    }
}

public class ActDocumentTests
{
    [Fact]
    public void AttributeEdit_UndoRestoresRawLine_AndClean()
    {
        var doc = ActXmlFormat.Parse(ActXmlFormatTests.Sample);
        var line = doc.Lines[2];
        string raw = line.ToXmlLine();
        Assert.False(doc.IsDirty);

        doc.SetAttribute(line, "CG", "");          // 지우기 = 속성 삭제
        Assert.Null(line.Find("CG"));
        Assert.True(doc.IsDirty);

        doc.UndoLast();
        Assert.Equal("asahi_idol_3", line.Get("CG"));
        Assert.False(line.IsModified);             // 원래 자리로 돌아옴 → 원래 줄 그대로
        Assert.Equal(raw, line.ToXmlLine());
        Assert.False(doc.IsDirty);

        doc.RedoNext();
        Assert.Null(line.Find("CG"));
    }

    [Fact]
    public void EmptyToEmpty_IsNoChange()
    {
        var doc = new ActDocument();
        var line = ScriptLine.Create(("Type", "TEXT"), ("Name", ""));
        doc.Lines.Add(line);
        doc.SetAttribute(line, "Name", "");
        doc.SetAttribute(line, "Voice", null);
        Assert.False(doc.Undo.CanUndo);
        Assert.Equal("", line.Find("Name"));
    }

    [Fact]
    public void InsertRemoveMove_WithUndo()
    {
        var doc = ActXmlFormat.Parse(ActXmlFormatTests.Sample);
        var original = doc.Lines.ToList();

        var added = ScriptLine.Create(("Type", "TEXT"));
        doc.InsertLines(1, [added]);
        Assert.Same(added, doc.Lines[1]);

        doc.RemoveLines([original[0], original[2]]);
        Assert.Equal(new[] { added, original[1], original[3] }, doc.Lines);

        Assert.True(doc.MoveLines([original[3]], -1));
        Assert.Equal(new[] { added, original[3], original[1] }, doc.Lines);
        Assert.False(doc.MoveLines([added], -1));   // 맨 위

        doc.UndoLast(); doc.UndoLast(); doc.UndoLast();
        Assert.Equal(original, doc.Lines);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void MoveSeparatedLines_EachOneStep()
    {
        var doc = ActXmlFormat.Parse(ActXmlFormatTests.Sample);
        var l = doc.Lines.ToList();
        doc.MoveLines([l[1], l[3]], -1);
        Assert.Equal(new[] { l[1], l[0], l[3], l[2] }, doc.Lines);
        doc.UndoLast();
        Assert.Equal(l, doc.Lines);
    }

    [Fact]
    public void Titles_UndoAndDirty()
    {
        var doc = ActXmlFormat.Parse(ActXmlFormatTests.Sample);
        doc.SetMainTitle("새 제목");
        Assert.Equal("새 제목", doc.MainTitle);
        Assert.True(doc.IsDirty);
        doc.UndoLast();
        Assert.Equal("InertiaLucis", doc.MainTitle);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void UndoStack_CapacityDropsOldest_AndSavePointLost()
    {
        var doc = new ActDocument();
        var line = ScriptLine.Create(("Type", "TEXT"));
        doc.Lines.Add(line);
        for (int i = 0; i < UndoStack.DefaultCapacity + 5; i++) doc.SetAttribute(line, "Name", "n" + i);
        Assert.Equal(UndoStack.DefaultCapacity, doc.Undo.Count);
        while (doc.Undo.CanUndo) doc.UndoLast();
        Assert.True(doc.IsDirty);   // 처음 상태(저장 시점)는 기록 밖으로 밀려남
    }
}

public class ActFilesTests
{
    [Fact]
    public void Scan_ListsGroupXmlOnly_Sorted()
    {
        using var t = new TempFolder();
        foreach (var f in new[] { @"NKS\NKS01.xml", @"IL\IL02.xml", @"IL\IL01.xml", @"IL\IL01.xml.meta", @"IL\note.txt" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(t.Path, f))!);
            File.WriteAllText(Path.Combine(t.Path, f), "x");
        }
        File.WriteAllText(Path.Combine(t.Path, "root.xml"), "x");   // 그룹 폴더 밖은 무시
        var list = ActFiles.Scan(t.Path);
        Assert.Equal(new[] { "IL / IL01", "IL / IL02", "NKS / NKS01" }, list.Select(e => e.Display));
    }

    [Fact]
    public void Backup_KeepsRecentN()
    {
        using var t = new TempFolder();
        string group = Path.Combine(t.Path, "game", "IL");
        Directory.CreateDirectory(group);
        string file = Path.Combine(group, "IL01.xml");
        File.WriteAllText(file, "v");
        string backups = Path.Combine(t.Path, "Backups");
        var now = new DateTime(2026, 10, 15, 12, 0, 0);
        for (int i = 0; i < 5; i++) ActFiles.Backup(file, backups, keep: 3, now.AddSeconds(i));
        File.WriteAllText(Path.Combine(backups, "IL", "IL01_x_20261015-120000-000.xml"), "other");   // 다른 막 이름은 건드리지 않음
        ActFiles.Backup(file, backups, keep: 3, now.AddSeconds(10));
        var names = Directory.GetFiles(Path.Combine(backups, "IL")).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(new[] { "IL01_20261015-120003-000.xml", "IL01_20261015-120004-000.xml", "IL01_20261015-120010-000.xml", "IL01_x_20261015-120000-000.xml" }, names);
    }
}

public class ActEditorViewModelTests
{
    private sealed class Env : IDisposable
    {
        public readonly TempFolder Temp = new();
        public readonly FakeDialogs Dialogs = new();
        public readonly AppSettings Settings = new();
        public readonly ActEditorViewModel Vm;
        public readonly string Scenario;
        public readonly string Backups;
        public readonly List<IReadOnlyList<ScriptRow>> Selected = new();

        public Env()
        {
            Scenario = Path.Combine(Temp.Path, "Scenario");
            Backups = Path.Combine(Temp.Path, "Backups");
            Directory.CreateDirectory(Path.Combine(Scenario, "IL"));
            File.WriteAllText(Path.Combine(Scenario, "IL", "IL01.xml"), ActXmlFormatTests.Sample);
            File.WriteAllText(Path.Combine(Scenario, "IL", "IL02.xml"), ActXmlFormatTests.Sample);
            Settings.Editor.ScenarioFolder = Scenario;
            var lib = new SpineLibrary(Path.Combine(Temp.Path, "SpineData"), new SpineManifest());
            lib.Rebuild();
            Vm = new ActEditorViewModel(Settings, lib, Dialogs, new ImmediateDispatcher(), () => { }, new AppLog(), Backups,
                                        () => new DateTime(2026, 10, 15, 12, 0, 0));
            Vm.SelectionRequested += rows => Selected.Add(rows);
        }

        public string Path1 => Path.Combine(Scenario, "IL", "IL01.xml");
        public void Dispose() => Temp.Dispose();
    }

    [Fact]
    public void Initialize_ListsActs_AndReopensLast()
    {
        using var env = new Env();
        env.Settings.Editor.LastActPath = env.Path1;
        env.Settings.Editor.LastLine = 2;
        env.Vm.Initialize();
        Assert.Equal(2, env.Vm.ActEntries.Count);
        Assert.Equal("IL01", env.Vm.SelectedEntry?.Name);
        Assert.Equal(4, env.Vm.Rows.Count);
        Assert.Equal(3, env.Selected.Last().Single().Number);
        Assert.Equal(3, env.Vm.Selection.Single().Number);   // 추가: 표 화면이 나중에 생겨도 적용할 수 있게 기억
    }

    private static FieldCell Cell(ScriptRow row, string attr) => row.Fields.Single(f => f.Def.Attr == attr);

    [Fact]
    public void Fields_FollowTypePalette()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        var rows = env.Vm.Rows;
        Assert.Equal(new[] { "Value", "Track", "Volume", "Effect" }, rows[0].Fields.Select(f => f.Def.Attr));   // AUDIO
        Assert.Equal("BGM_2", Cell(rows[0], "Track").Value);
        Assert.Equal("", rows[0].Others);
        Assert.Equal("ResourceKey", rows[1].Fields[0].Def.Attr);                                      // BG
        Assert.Equal("Key=bg_office_day", rows[1].Others);                                             // 옛 Key는 팔레트에 없음 → 기타
        Assert.Equal(10, rows[2].Fields.Count);                                                        // TEXT
        Assert.Equal("anger1 face_anger", Cell(rows[2], "Animation").Value);
        Assert.True(Cell(rows[2], "CG").Warning);                                                      // 옛 CG 키 → 경고
        Assert.False(Cell(rows[2], "Name").Warning);
        Assert.Equal(1, env.Vm.UnknownCgCount);
        Assert.True(Cell(rows[2], "Effect").IsEmpty);
    }

    [Fact]
    public void Hint_OnlyOnHeaderTypeRows_AndNoEmptyToolTip()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        var rows = env.Vm.Rows;
        env.Vm.SetSelection([rows[2]]);                       // TEXT 선택 → 제목 줄 TEXT
        Assert.True(Cell(rows[2], "Effect").ShowHint);         // 같은 종류의 빈 칸만 흐린 칸 이름
        Assert.False(Cell(rows[1], "Effect").ShowHint);        // BG 줄의 빈 칸은 표시 안 함
        env.Vm.SetSelection([rows[1]]);                       // BG 선택
        Assert.True(Cell(rows[1], "Effect").ShowHint);
        Assert.False(Cell(rows[2], "Effect").ShowHint);
        Assert.Null(rows[0].OthersToolTip);                    // 기타가 비면 툴팁 없음
        Assert.NotNull(rows[1].OthersToolTip);
    }

    [Fact]
    public void EditCell_ThroughUndo()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        var text = env.Vm.Rows[2];
        Cell(text, "Name").Value = "후유코";
        Cell(text, "Effect").Value = "zoom";
        Assert.Equal("후유코", text.Get("Name"));
        Assert.Equal("zoom", text.Get("Effect"));
        Cell(env.Vm.Rows[0], "Volume").Value = "";       // 지우면 속성 삭제
        Assert.Null(env.Vm.Rows[0].Line.Find("Volume"));
        env.Vm.UndoCommand.Execute(null);
        env.Vm.UndoCommand.Execute(null);
        env.Vm.UndoCommand.Execute(null);
        Assert.False(env.Vm.Document!.IsDirty);
    }

    [Fact]
    public void ChangeType_KeepsAttributes_ShowsOthers_AndUndo()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        var row = env.Vm.Rows[2];                         // TEXT
        var oldFields = row.Fields;
        row.Type = "SETCG";
        Assert.Equal("SETCG", row.Line.Type);
        Assert.NotSame(oldFields, row.Fields);
        Assert.Equal("asahi_idol_3", Cell(row, "CG").Value);                 // 새 팔레트에도 있는 속성은 그대로 칸에
        Assert.Contains("Name=아사히", row.Others);                          // 없는 속성은 기타로 (지우지 않음)
        Assert.Contains("Voice=s001085", row.Others);
        Assert.Equal(ScriptRowKind.Other, row.Kind);

        row.Type = "";                                    // 드롭다운이 잠깐 비는 경우는 무시
        Assert.Equal("SETCG", row.Line.Type);

        env.Vm.UndoCommand.Execute(null);
        Assert.Equal("TEXT", row.Line.Type);
        Assert.Equal("아사히", Cell(row, "Name").Value);
        Assert.False(env.Vm.Document!.IsDirty);
    }

    [Fact]
    public void UnknownType_AllAttributesInOthers()
    {
        using var env = new Env();
        File.WriteAllText(env.Path1, ActXmlFormatTests.Sample.Replace("Type=\"TRANSITION\"", "Type=\"FUTURE\""));
        env.Vm.Open(env.Path1);
        var row = env.Vm.Rows[3];
        Assert.Null(row.Def);
        Assert.Empty(row.Fields);
        Assert.Equal("Effect=wipe_left; SE=se_trans_out|se_trans_in", row.Others);
        Assert.Contains("FUTURE", row.TypeOptions);
    }

    [Fact]
    public void Header_FollowsSelection_AndWidthsSaved()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        env.Vm.SetSelection([env.Vm.Rows[0]]);
        Assert.Equal("AUDIO", env.Vm.HeaderTitle);
        var track = env.Vm.HeaderType!.Fields.Single(f => f.Attr == "Track");
        Assert.Same(track, Cell(env.Vm.Rows[0], "Track").Def);   // 제목 줄과 행이 같은 정의 → 폭이 함께 바뀜
        track.Width = 333;
        env.Vm.SaveFieldWidth(track);
        Assert.Equal(333, env.Settings.Editor.FieldWidths["AUDIO.Track"]);
        track.Width = 5;
        Assert.Equal(FieldDef.MinWidth, track.Width);

        // 다시 만들면 저장된 폭 적용
        var types = LineTypeCatalog.LoadEmbedded();
        types.ApplyWidths(env.Settings.Editor.FieldWidths);
        Assert.Equal(333, types.Find("audio")!.Fields.Single(f => f.Attr == "Track").Width);

        env.Vm.ResetFieldWidthsCommand.Execute(null);
        Assert.Empty(env.Settings.Editor.FieldWidths);
    }

    [Fact]
    public void AddText_InsertsBelowSelection()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        env.Vm.SetSelection([env.Vm.Rows[1]]);
        env.Vm.AddTextCommand.Execute(null);
        Assert.Equal(5, env.Vm.Rows.Count);
        Assert.Equal("TEXT", env.Vm.Rows[2].Type);
        Assert.Equal(3, env.Vm.Rows[2].Number);
        Assert.Equal(4, env.Vm.Rows[3].Number);
        Assert.Same(env.Vm.Rows[2], env.Selected.Last().Single());
        Assert.Equal("\t<Line Type=\"TEXT\" Name=\"\" Text=\"\" SpeakerType=\"1\"/>", env.Vm.Rows[2].Line.ToXmlLine());
    }

    [Fact]
    public void Save_WritesFile_BacksUpPrevious()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        Cell(env.Vm.Rows[2], "Text").Value = "바뀐 대사";
        Assert.True(env.Vm.TrySave());
        string saved = File.ReadAllText(env.Path1);
        Assert.StartsWith(ActXmlFormatTests.SampleLines[0] + "\r\n<Act mainTitle=\"InertiaLucis\"", saved);
        Assert.Contains("Text=\"바뀐 대사\"", saved);
        Assert.False(env.Vm.Document!.IsDirty);
        var backup = Directory.GetFiles(Path.Combine(env.Backups, "IL")).Single();
        Assert.Equal(ActXmlFormatTests.Sample, File.ReadAllText(backup));
        Assert.Empty(Directory.GetFiles(Path.Combine(env.Scenario, "IL"), "*.tmp"));
    }

    [Fact]
    public void Save_ExternalChange_AsksBeforeOverwrite()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        File.WriteAllText(env.Path1, ActXmlFormatTests.Sample + " ");
        env.Dialogs.ConfirmResult = false;
        Assert.False(env.Vm.TrySave());
        Assert.EndsWith(" ", File.ReadAllText(env.Path1));
        env.Dialogs.ConfirmResult = true;
        Assert.True(env.Vm.TrySave());
    }

    [Fact]
    public void SwitchAct_WithChanges_CancelKeepsDocument()
    {
        using var env = new Env();
        env.Vm.Initialize();
        env.Vm.Open(env.Path1);
        Cell(env.Vm.Rows[2], "Name").Value = "x";
        env.Dialogs.SaveChoiceResult = SaveChoice.Cancel;
        env.Vm.SelectedEntry = env.Vm.ActEntries[1];
        Assert.Equal(1, env.Dialogs.AskSaveCount);
        Assert.Equal("IL01", env.Vm.Document!.ActName);
        Assert.Equal("IL01", env.Vm.SelectedEntry?.Name);   // 목록 선택도 되돌림

        env.Dialogs.SaveChoiceResult = SaveChoice.Discard;
        env.Vm.SelectedEntry = env.Vm.ActEntries[1];
        Assert.Equal("IL02", env.Vm.Document!.ActName);
        Assert.DoesNotContain("Name=\"x\"", File.ReadAllText(env.Path1));
    }

    [Fact]
    public void Open_BadFile_ShowsError_KeepsCurrent()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        string bad = Path.Combine(env.Scenario, "IL", "BAD.xml");
        File.WriteAllText(bad, "<Act>");
        Assert.False(env.Vm.Open(bad));
        Assert.Single(env.Dialogs.Errors);
        Assert.Equal("IL01", env.Vm.Document!.ActName);
    }

    [Fact]
    public void DeleteAndMove_KeepSelection()
    {
        using var env = new Env();
        env.Vm.Open(env.Path1);
        var target = env.Vm.Rows[3];
        env.Vm.SetSelection([target]);
        env.Vm.MoveUpCommand.Execute(null);
        Assert.Equal(3, target.Number);
        Assert.Same(target, env.Selected.Last().Single());

        env.Vm.SetSelection([env.Vm.Rows[0], env.Vm.Rows[1]]);
        env.Vm.DeleteRowsCommand.Execute(null);
        Assert.Equal(2, env.Vm.Rows.Count);
        Assert.Equal(1, env.Vm.Rows[0].Number);
    }
}

public class LineTypeCatalogTests
{
    [Fact]
    public void Embedded_HasAllNineTypes_InOrder()
    {
        var c = LineTypeCatalog.LoadEmbedded();
        Assert.Equal(new[] { "TEXT", "BG", "AUDIO", "SE", "TRANSITION", "BREAK", "SETCG", "CGGROUP", "IMAGE" }, c.Ids);
        Assert.Equal(new[] { "Name", "Text", "Voice", "CG", "Position", "Animation", "Effect", "Value", "Duration", "SpeakerType" },
                     c.Find("TEXT")!.Fields.Select(f => f.Attr));
        Assert.Equal(new[] { "Track", "Volume", "Duration" }, c.Find("se")!.Fields.Select(f => f.Attr));
        Assert.All(c.Types.SelectMany(t => t.Fields), f => Assert.False(string.IsNullOrEmpty(f.Label)));
    }
}
