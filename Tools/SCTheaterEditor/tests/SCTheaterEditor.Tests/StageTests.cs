using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Models;
using SCTheaterEditor.Services;
using SCTheaterEditor.Stage;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

public class StageLayoutTests
{
    [Fact]
    public void Positions_MatchCGManager()
    {
        Assert.Equal(-480, StageLayout.Get("left")!.X);
        Assert.Equal(768, StageLayout.Get("wide_far_right")!.X);
        Assert.Equal(0.6, StageLayout.Get("wide_center")!.Scale);
        Assert.Equal(1.0, StageLayout.Get("center")!.Scale);
    }

    [Fact]
    public void NextSlot_FillsThreeThenSwitchesToFive()
    {
        Assert.Equal("center", StageLayout.NextSlot([]).position);
        Assert.Equal("left", StageLayout.NextSlot(["center"]).position);
        Assert.Equal("right", StageLayout.NextSlot(["center", "left"]).position);

        var (remap, pos) = StageLayout.NextSlot(["center", "left", "right"]);
        Assert.Equal("wide_far_left", pos);
        Assert.Equal("wide_left", remap["left"]);
        Assert.Equal("wide_center", remap["center"]);
        Assert.Equal("wide_right", remap["right"]);

        Assert.Equal("wide_far_right", StageLayout.NextSlot(["wide_center", "wide_left", "wide_right", "wide_far_left"]).position);
        Assert.Null(StageLayout.NextSlot(["wide_center", "wide_left", "wide_right", "wide_far_left", "wide_far_right"]).position);
    }

    [Fact]
    public void ToSplit_FiveToThree_KeepsOrder_AndRejectsMoreThanThree()
    {
        var map = StageLayout.ToSplit(["wide_far_left", "wide_center", "wide_far_right"], 3)!;
        Assert.Equal("left", map["wide_far_left"]);
        Assert.Equal("center", map["wide_center"]);
        Assert.Equal("right", map["wide_far_right"]);

        var two = StageLayout.ToSplit(["wide_far_left", "wide_left"], 3)!;
        Assert.Equal("left", two["wide_far_left"]);
        Assert.Equal("center", two["wide_left"]);

        Assert.Null(StageLayout.ToSplit(["wide_far_left", "wide_left", "wide_center", "wide_right"], 3));
        Assert.Equal("wide_right", StageLayout.ToSplit(["right"], 5)!["right"]);
    }

    [Theory]
    [InlineData(-400, 3, "left")]
    [InlineData(200, 3, "center")]
    [InlineData(260, 3, "right")]
    [InlineData(-700, 5, "wide_far_left")]
    [InlineData(100, 5, "wide_center")]
    public void Snap(double x, int split, string expected) => Assert.Equal(expected, StageLayout.Snap(x, split));
}

public class StageMessageTests
{
    [Fact]
    public void Command_SerializesTypeAndCamelCase()
    {
        string json = new AddActor(3, "https://spinedata.scsm/P_SSR/", "a.json", "a.atlas.txt", "円香", true).ToJson();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("addActor", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("id").GetInt32());
        Assert.True(doc.RootElement.GetProperty("preview").GetBoolean());
        Assert.Equal("a.atlas.txt", doc.RootElement.GetProperty("atlas").GetString());

        string play = new PlayAnims(1, ["wait", "face_x"], new LipPlay("lip_wait", null, -1)).ToJson();
        Assert.Contains("\"lip\":{\"moving\":\"lip_wait\",\"still\":null,\"seconds\":-1}", play);
    }

    [Fact]
    public void Events_ParseFromJs()
    {
        var loaded = StageEvents.Parse(JsonDocument.Parse("""
            {"type":"actorLoaded","id":2,"version":"3.6.53","width":10,"height":20,"boneCount":5,"skins":["default","normal"],"skin":"normal",
             "animations":[{"name":"wait","duration":1.5,"loopStart":-1}]}
            """).RootElement);
        var a = Assert.IsType<ActorLoaded>(loaded);
        Assert.Equal("normal", a.Skin);
        Assert.Equal(1.5, a.Animations[0].Duration);

        var state = Assert.IsType<ActorState>(StageEvents.Parse(JsonDocument.Parse("""
            {"type":"state","id":2,"tracks":[{"name":"wait","time":0.5,"duration":1.5,"loop":true,"holding":false},null,null,null],"paused":false,"clock":0.5,"length":1.5,"lip":null,"bones":[{"name":"head","dr":5,"dx":0,"dy":0,"dsx":0,"dsy":0}]}
            """).RootElement));
        Assert.Null(state.Tracks[1]);
        Assert.Equal("wait", state.Tracks[0]!.Name);
        Assert.Equal(1.5, state.Length);
        Assert.Equal("head", state.Bones![0].Name);
        Assert.Equal(2.0, Assert.IsType<StageTime>(StageEvents.Parse(JsonDocument.Parse("""{"type":"stageTime","time":1,"length":2,"paused":true}""").RootElement)).Length);
        Assert.Contains("\"id\":null", new SetPaused(true).ToJson());   // 무대 전체

        Assert.Null(Assert.IsType<StageClicked>(StageEvents.Parse(JsonDocument.Parse("""{"type":"clicked","id":null}""").RootElement)).Id);
        Assert.Null(StageEvents.Parse(JsonDocument.Parse("""{"type":"unknown"}""").RootElement));
    }

    [Fact]
    public void Urls_EncodeSegments()
    {
        Assert.Equal("https://spinedata.scsm/P_SSR/%E3%80%90Merry%E3%80%91/", StageUrls.SpineFolder(@"P_SSR\【Merry】"));
        Assert.Equal("https://backgrounds.scsm/bg%20office.png", StageUrls.Background("bg office.png"));
    }
}

/// <summary>무대 흉내: 보낸 명령 기록, 이벤트를 직접 일으킴</summary>
internal sealed class FakeStage : IStageHost
{
    public List<StageCommand> Sent { get; } = new();
    public event Action<StageEvent>? EventReceived;
    public void Send(StageCommand command) => Sent.Add(command);
    public void Raise(StageEvent e) => EventReceived?.Invoke(e);
    public T Last<T>() where T : StageCommand => Sent.OfType<T>().Last();
}

public class ScriptViewModelTests
{
    private sealed class Ctx : IDisposable
    {
        public TempFolder Temp = new();
        public FakeStage Stage = new();
        public StrongReferenceMessenger Messenger = new();
        public List<AppTab> Tabs = new();
        public AppSettings Settings;
        public SpineLibrary Library;
        public ScriptViewModel Vm;

        public Ctx(int setCount = 6)
        {
            Settings = new AppSettings { SpineDataFolderSetting = Temp.Path };
            // 아이돌 1명, stand 세트 setCount개
            var snap = new CatalogSnapshot();
            snap.Idols.Add(new IdolInfo { IdolId = 21, IdolName = "樋口円香", NickName = "madoka" });
            snap.Dresses[21] = Enumerable.Range(0, setCount).Select(i => new DressInfo
            {
                Exist = true, IdolId = 21, EnzaId = $"10402100{i}0", DressName = $"【의상{i}】樋口円香", DressType = "P_SSR", DressTypeOrder = 1,
                DressUuid = $"u{i}",
                Assets = new() { ["idols"] = new() { new DressAsset { Path = $"spine/idols/stand/10402100{i}0/", Type = "stand" } } },
            }).ToList();
            var plan = SpinePlanner.Build(snap);
            var m = Fixture.Downloaded(Settings.SaveFolder, plan, v1: false);
            m.UpdateIdols(snap.Idols);
            Library = new SpineLibrary(Settings.SaveFolder, m);
            Library.Rebuild();
            Messenger.Register<ShowTabMessage>(Tabs, (r, msg) => ((List<AppTab>)r).Add(msg.Tab));
            var bg = new BackgroundLibrary(Path.Combine(Temp.Path, "Backgrounds"));
            Vm = new ScriptViewModel(Settings, Library, bg, Stage, LipRules.LoadEmbedded(), Messenger, () => { }, new AppLog());
            Stage.Raise(new StageReady());
        }

        public SpineAsset Asset(int i) => Library.Assets[i];

        public void Load(StageActor a, params string[] anims) =>
            Stage.Raise(new ActorLoaded(a.Id, "3.6.53", 100, 200, 10, ["default", "normal"], "normal",
                (anims.Length == 0 ? new[] { "wait", "smile1", "face_anger", "lip_wait", "lip_wait_s", "arm_down" } : anims)
                    .Select(n => new AnimInfo(n, 1.0, -1)).ToList()));

        public void Dispose() => Temp.Dispose();
    }

    [Fact]
    public void Preview_ThenEnter_AddsAtCenter()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: false);

        var a = Assert.Single(c.Vm.Actors);
        Assert.True(a.IsPreview);
        Assert.Equal("center", a.Position);
        Assert.Same(a, c.Vm.Preview);
        Assert.Same(a, c.Vm.SelectedActor);
        Assert.Contains(AppTab.Script, c.Tabs);
        Assert.True(c.Stage.Last<AddActor>().Preview);

        Assert.True(c.Vm.HandleKey("Enter"));
        Assert.False(a.IsPreview);
        Assert.Null(c.Vm.Preview);
        Assert.False(c.Stage.Last<SetPreview>().Preview);
    }

    [Fact]
    public void Preview_Escape_RestoresStageAndGoesBack()
    {
        using var c = new Ctx();
        for (int i = 0; i < 3; i++) c.Vm.Open(c.Asset(i), immediate: true);
        Assert.Equal(new[] { "center", "left", "right" }, c.Vm.Actors.Select(a => a.Position));

        c.Vm.Open(c.Asset(3), immediate: false);   // 4명째 → 5분할 전환 + wide_far_left
        Assert.Equal(5, c.Vm.Split);
        Assert.Equal(new[] { "wide_center", "wide_left", "wide_right", "wide_far_left" }, c.Vm.Actors.Select(a => a.Position));

        Assert.True(c.Vm.HandleKey("Escape"));
        Assert.Equal(3, c.Vm.Actors.Count);
        Assert.Equal(new[] { "center", "left", "right" }, c.Vm.Actors.Select(a => a.Position));
        Assert.Equal(3, c.Vm.Split);
        Assert.Equal(AppTab.Resource, c.Tabs.Last());
    }

    [Fact]
    public void Full_PreviewReplacesSelected_OnConfirm()
    {
        using var c = new Ctx();
        for (int i = 0; i < 5; i++) c.Vm.Open(c.Asset(i), immediate: true);
        var target = c.Vm.Actors[1];
        c.Vm.SelectedActor = target;

        c.Vm.Open(c.Asset(5), immediate: false);
        Assert.Equal(target.Position, c.Vm.Preview!.Position);
        Assert.False(c.Stage.Last<ShowActor>().Visible);

        c.Vm.ConfirmPreviewCommand.Execute(null);
        Assert.Equal(5, c.Vm.Actors.Count);
        Assert.DoesNotContain(target, c.Vm.Actors);
        Assert.Equal(target.Id, c.Stage.Last<RemoveActor>().Id);
    }

    [Fact]
    public void SameSet_IsSelectedNotDuplicated()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: true);
        c.Vm.Open(c.Asset(1), immediate: true);
        c.Vm.Open(c.Asset(0), immediate: false);
        Assert.Equal(2, c.Vm.Actors.Count);
        Assert.Equal(c.Asset(0).SourcePath, c.Vm.SelectedActor!.Asset.SourcePath);
    }

    [Fact]
    public void Drop_SwapsWithOccupant()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: true);   // center
        c.Vm.Open(c.Asset(1), immediate: true);   // left
        var first = c.Vm.Actors[0];
        c.Stage.Raise(new ActorDropped(first.Id, -470));   // left 근처에 놓음
        Assert.Equal("left", first.Position);
        Assert.Equal("center", c.Vm.Actors[1].Position);
    }

    [Fact]
    public void SplitToggle_AndRemove()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: true);
        c.Vm.Open(c.Asset(1), immediate: true);
        c.Vm.SetSplitCommand.Execute("5");
        Assert.Equal(new[] { "wide_center", "wide_left" }, c.Vm.Actors.Select(a => a.Position));
        c.Vm.SetSplitCommand.Execute("3");
        Assert.Equal(new[] { "center", "left" }, c.Vm.Actors.Select(a => a.Position));

        c.Vm.SelectedActor = c.Vm.Actors[0];
        Assert.True(c.Vm.HandleKey("Delete"));
        Assert.Single(c.Vm.Actors);
        Assert.Null(c.Vm.SelectedActor);
    }

    [Fact]
    public void Loaded_PlaysWaitWithLip_AndAnimListFollowsTrack()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: true);
        var a = c.Vm.Actors[0];
        c.Load(a);

        var play = c.Stage.Last<PlayAnims>();
        Assert.Equal(new[] { "wait" }, play.Anims);
        Assert.Equal("lip_wait", play.Lip!.Moving);           // madoka: DefaultLipData에 없음 → lip_wait 안전망 (데이터에 있음)
        Assert.Equal(3.0, play.Lip.Seconds);

        Assert.Equal("wait", c.Vm.SelectedAnimItem!.Name);
        c.Vm.SelectedTrack = 1;
        Assert.Equal("face_anger", c.Vm.AnimItems[1].Name);    // (None) 다음 우선 그룹

        // 게임 방식: face_ 애니메이션은 트랙 1로, 전체 다시 재생
        c.Vm.ApplyAnimCommand.Execute(c.Vm.AnimItems[1]);
        Assert.Equal(new[] { "wait", "face_anger" }, c.Stage.Last<PlayAnims>().Anims);
        Assert.Equal("wait face_anger", a.AnimationText);
    }

    [Fact]
    public void PageReload_ResyncsEverything()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: true);
        c.Stage.Sent.Clear();
        c.Stage.Raise(new StageReady());
        Assert.Single(c.Stage.Sent.OfType<StageConfig>());
        Assert.Single(c.Stage.Sent.OfType<AddActor>());
        Assert.Single(c.Stage.Sent.OfType<PlaceActor>());
    }

    // ---------------------------------------------------------------- 추가 (3회차 확인 후 수정)

    [Fact]
    public void SplitRefused_RestoresRadio()
    {
        using var c = new Ctx();
        for (int i = 0; i < 4; i++) c.Vm.Open(c.Asset(i), immediate: true);
        var changed = new List<string?>();
        c.Vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        c.Vm.SetSplitCommand.Execute("3");
        Assert.Equal(5, c.Vm.Split);
        Assert.Contains(nameof(ScriptViewModel.Split), changed);   // 라디오 버튼 표시를 5분할로 되돌림
    }

    [Fact]
    public void Pause_SelectedOnly_ElseWholeStage()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: true);
        c.Vm.Open(c.Asset(1), immediate: true);
        var (a, b) = (c.Vm.Actors[0], c.Vm.Actors[1]);

        c.Vm.SelectedActor = a;
        c.Vm.HandleKey(" ");
        Assert.True(a.Paused);
        Assert.False(b.Paused);
        Assert.Equal(new SetPaused(true, a.Id), c.Stage.Last<SetPaused>());
        Assert.True(c.Vm.Paused);

        c.Vm.SelectedActor = null;
        Assert.False(c.Vm.Paused);                 // b가 재생 중
        c.Vm.HandleKey(" ");                       // 하나라도 재생 중 → 모두 멈춤
        Assert.True(a.Paused && b.Paused);
        Assert.Equal(new SetPaused(true), c.Stage.Last<SetPaused>());
        c.Vm.HandleKey(" ");                       // 모두 멈춤 → 모두 재생 (무대가 드래그 바 시점으로 맞춤)
        Assert.False(a.Paused || b.Paused);
        Assert.Equal(new SetPaused(false), c.Stage.Last<SetPaused>());
    }

    [Fact]
    public void Seek_UsesSeconds_SelectedOrWholeStage()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: true);
        var a = c.Vm.Actors[0];
        c.Load(a);
        c.Vm.SelectedActor = null;
        c.Stage.Raise(new StageTime(1, 4, false));
        Assert.Equal(0.25, c.Vm.TimeValue, 3);
        Assert.Equal("1.00 / 4.00s", c.Vm.TimeText);

        c.Vm.HandleKey(" ");                        // 무대 전체 일시정지
        c.Vm.TimeValue = 0.5;
        Assert.Equal(new Seek(null, 2), c.Stage.Last<Seek>());

        c.Vm.SelectedActor = a;
        c.Stage.Raise(new ActorState(a.Id, [null, null, null, null], true, 0.5, 2, null));
        c.Vm.TimeValue = 0.75;
        Assert.Equal(new Seek(a.Id, 1.5), c.Stage.Last<Seek>());
    }

    [Fact]
    public void Bones_WatchedWhileExpanded()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: true);
        var a = c.Vm.Actors[0];
        c.Load(a);
        c.Vm.BonesExpanded = true;
        Assert.False(c.Stage.Last<WatchBones>().On);   // 변경: 설정 영역이 접혀 있으면 보이지 않으므로 갱신 안 함
        c.Vm.SettingsExpanded = true;
        Assert.True(c.Stage.Last<WatchBones>().On);
        c.Stage.Raise(new ActorState(a.Id, [null, null, null, null], false, 0.5, 2, null, [new BoneDelta("head", 5, 0, 0, 0, 0)]));
        Assert.Single(c.Vm.Bones);
        Assert.StartsWith("head", c.Vm.Bones[0]);
    }

    // ---------------------------------------------------------------- 추가(4-2): 스크립트 → 무대

    private static SCTheaterEditor.Script.StageSnapshot Snap(params (string Cg, string Pos, string? Anim)[] actors) =>
        new(0, null, actors.Select(a => new SCTheaterEditor.Script.StageActorState(a.Cg, a.Pos, a.Anim)).ToList(), []);

    [Fact]
    public void ApplySnapshot_HidesAndReshowsWithoutReloading()
    {
        using var c = new Ctx();
        string k0 = c.Asset(0).CgKey, k1 = c.Asset(1).CgKey;
        c.Vm.ApplySnapshot(Snap((k0, "left", "smile1 face_anger"), (k1, "right", null)));
        Assert.Equal(2, c.Stage.Sent.OfType<AddActor>().Count());
        Assert.Equal(new[] { "left", "right" }, c.Vm.Actors.Select(a => a.Position));
        Assert.Equal("smile1 face_anger", c.Vm.Actors[0].AnimationText);
        var first = c.Vm.Actors[0];

        c.Vm.ApplySnapshot(Snap((k1, "center", null)));             // k0은 숨겨 둠
        Assert.Equal(k1, Assert.Single(c.Vm.Actors).Asset.CgKey);
        Assert.Equal(1, c.Vm.HiddenCount);
        Assert.Equal(new ShowActor(first.Id, false), c.Stage.Last<ShowActor>());
        Assert.Empty(c.Stage.Sent.OfType<RemoveActor>());

        c.Vm.ApplySnapshot(Snap((k1, "left", null), (k0, "right", null)));   // 다시 올림 → 새로 불러오지 않음
        Assert.Equal(2, c.Stage.Sent.OfType<AddActor>().Count());
        Assert.Same(first, c.Vm.Actors[1]);
        Assert.Equal(new ShowActor(first.Id, true), c.Stage.Last<ShowActor>());
        Assert.Equal(0, c.Vm.HiddenCount);
    }

    [Fact]
    public void ApplySnapshot_EvictsOldestHidden_AndReportsUnknown()
    {
        using var c = new Ctx(setCount: 12);
        for (int i = 0; i < 12; i++) c.Vm.ApplySnapshot(Snap((c.Asset(i).CgKey, "center", null)));
        Assert.Equal(ScriptViewModel.HiddenCacheSize, c.Vm.HiddenCount);   // 11명 숨김 → 가장 오래된 1명은 지움
        Assert.Single(c.Stage.Sent.OfType<RemoveActor>());

        c.Vm.ApplySnapshot(Snap(("no_such_key", "left", null)));
        Assert.Empty(c.Vm.Actors);
        Assert.Contains("올리지 못함: no_such_key", c.Vm.StatusText);
    }
}
