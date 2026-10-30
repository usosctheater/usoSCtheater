using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>추가: 조합키 줄 삽입 (Ctrl+숫자) — 슬롯 정리, 종류별 삽입, 설정 탭에서 슬롯 변경</summary>
public class LineSlotTests
{
    private sealed class Env : IDisposable
    {
        public readonly TempFolder Temp = new();
        public readonly AppSettings Settings = new();
        public readonly FakeDialogs Dialogs = new();
        public readonly StrongReferenceMessenger Messenger = new();
        public readonly ActEditorViewModel Editor;
        public readonly List<(ScriptRow Row, string Attr)> Focus = new();

        public Env()
        {
            string file = Path.Combine(Temp.Path, "IL", "IL01.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, ActXmlFormatTests.Sample);   // AUDIO · BG · TEXT · TRANSITION
            var lib = new SpineLibrary(Path.Combine(Temp.Path, "SpineData"), new SpineManifest());
            lib.Rebuild();
            Editor = new ActEditorViewModel(Settings, lib, Dialogs, new ImmediateDispatcher(), () => { }, new AppLog(),
                                            Path.Combine(Temp.Path, "Backups"), messenger: Messenger);
            Editor.FocusFieldRequested += (row, attr) => Focus.Add((row, attr));
            Assert.True(Editor.Open(file));
        }

        public void Dispose() => Temp.Dispose();
    }

    [Fact]
    public void Normalize_FillsDefaults_EmptyAndUnknownBecomeEmpty()
    {
        var types = LineTypeCatalog.LoadEmbedded();
        Assert.Equal(types.Ids.Take(9), LineSlots.Normalize(null, types));
        Assert.Equal(types.Ids.Take(9), LineSlots.Normalize([], types));
        var slots = LineSlots.Normalize(["SE", "", "FUTURE", "text"], types);
        Assert.Equal(new[] { "SE", "", "", "TEXT" }, slots.Take(4));            // 빈 슬롯 · 모르는 종류 → 빈 슬롯 · 표기 맞춤
        Assert.Equal(types.Ids.Skip(4).Take(5), slots.Skip(4));                  // 나머지는 기본값
    }

    [Fact]
    public void InsertSlot_InsertsTypeBelowSelection_FocusFirstField_EachUndoable()
    {
        using var env = new Env();
        var ed = env.Editor;
        ed.SetSelection([ed.Rows[1]]);                                          // BG
        Assert.Equal("2번 줄 아래에 삽입", ed.InsertPositionText);

        Assert.True(ed.InsertSlot(5));                                          // 기본 5번 = TRANSITION
        Assert.Equal("\t<Line Type=\"TRANSITION\"/>", ed.Rows[2].Line.ToXmlLine());
        Assert.Equal(("Effect", 3), (env.Focus[^1].Attr, env.Focus[^1].Row.Number));   // 그 종류의 첫 칸
        Assert.Equal("3번 줄 아래에 삽입", ed.InsertPositionText);

        Assert.True(ed.InsertSlot(1));                                          // 이어서 넣으면 방금 넣은 줄 아래
        Assert.Equal("\t<Line Type=\"TEXT\" CharId=\"\" Text=\"\" SpeakerType=\"1\"/>", ed.Rows[3].Line.ToXmlLine());   // 변경(다국어)
        Assert.Equal("Text", env.Focus[^1].Attr);                               // TEXT는 대사 칸

        ed.UndoCommand.Execute(null);                                           // 한 줄씩 되돌리기
        Assert.Equal(new[] { "AUDIO", "BG", "TRANSITION", "TEXT", "TRANSITION" }, ed.Rows.Select(r => r.Type));
        ed.UndoCommand.Execute(null);
        Assert.Equal(4, ed.Rows.Count);
        Assert.False(ed.Document!.IsDirty);
    }

    [Fact]
    public void InsertSlot_StampFillsCgLines_CgGroupKeepsNameEmpty()
    {
        using var env = new Env();
        var ed = env.Editor;
        ed.StampProvider = () => new StageStamp("fuyuko_stand_1", "left", "wait3 face_smile", "fuyuko");
        ed.SetSelection([ed.Rows[3]]);
        ed.InsertSlot(7);                                                       // SETCG
        Assert.Equal("\t<Line Type=\"SETCG\" CG=\"fuyuko_stand_1\" Position=\"left\" Animation=\"wait3 face_smile\"/>", ed.Rows[4].Line.ToXmlLine());
        Assert.Equal("CG", env.Focus[^1].Attr);
        ed.InsertSlot(8);                                                       // CGGROUP — 그룹명(Name)은 비워 둠
        Assert.Equal("\t<Line Type=\"CGGROUP\" CG=\"fuyuko_stand_1\" Position=\"left\" Animation=\"wait3 face_smile\"/>", ed.Rows[5].Line.ToXmlLine());
        ed.InsertSlot(2);                                                       // BG — 종류만
        Assert.Equal("\t<Line Type=\"BG\"/>", ed.Rows[6].Line.ToXmlLine());
        Assert.Equal("ResourceKey", env.Focus[^1].Attr);
    }

    [Fact]
    public void SettingsTab_ChangesSlots_EditorFollows_EmptySlotOnlyInforms_Reset()
    {
        using var env = new Env();
        var vm = new SettingsViewModel(env.Settings, env.Dialogs, () => { }, env.Messenger, new AppLog());
        Assert.Equal("TEXT", vm.Slots[0].Selected);

        vm.Slots[0].Selected = "SE";
        vm.Slots[1].Selected = SettingsViewModel.NoSlot;
        Assert.Equal("SE", env.Settings.Editor.LineSlots[0]);
        Assert.Equal("", env.Settings.Editor.LineSlots[1]);
        Assert.Equal("SE", env.Editor.GuideSlots[0].Type);                      // 알림을 받아 가이드 슬롯 다시 만듦
        Assert.True(env.Editor.GuideSlots[1].IsEmpty);
        Assert.Equal("—", env.Editor.GuideSlots[1].Label);

        int count = env.Editor.Rows.Count;
        Assert.True(env.Editor.InsertSlot(2));                                  // 빈 슬롯 → 안내만
        Assert.Equal(count, env.Editor.Rows.Count);
        Assert.Contains("비어 있습니다", env.Editor.StatusText);

        vm.ResetLineSlotsCommand.Execute(null);
        Assert.Empty(env.Settings.Editor.LineSlots);                            // 비우면 기본값
        Assert.Equal("TEXT", vm.Slots[0].Selected);
        Assert.Equal("BG", env.Editor.GuideSlots[1].Type);
    }
}
