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
        // 추가(6회차): 게임 Resources 오디오 (경로 조각마다 인코딩, \ 와 / 모두)
        Assert.Equal("https://resources.scsm/Audio/Voice/IL/s001%20001.wav", StageUrls.Resource(@"Audio\Voice\IL\s001 001.wav"));
        Assert.Equal("https://resources.scsm/Audio/SE/se_baton.mp3", StageUrls.Resource("/Audio/SE/se_baton.mp3"));
    }

    /// <summary>추가(6회차): 오디오 메시지 직렬화·해석</summary>
    [Fact]
    public void AudioMessages_SerializeAndParse()
    {
        using var play = JsonDocument.Parse(new PlayAudio("bgm", "https://resources.scsm/Audio/BGM/a.mp3", 0.5, true).ToJson());
        var p = play.RootElement;
        Assert.Equal("audioPlay", p.GetProperty("type").GetString());
        Assert.Equal("bgm", p.GetProperty("channel").GetString());
        Assert.Equal(0.5, p.GetProperty("volume").GetDouble());
        Assert.True(p.GetProperty("loop").GetBoolean());
        Assert.Equal(0, p.GetProperty("start").GetDouble());
        using var stop = JsonDocument.Parse(new StopAudio(null).ToJson());
        Assert.Equal(JsonValueKind.Null, stop.RootElement.GetProperty("channel").ValueKind);   // null = 전부
        Assert.Equal("audioSeek", JsonDocument.Parse(new SeekAudio("bgm", 12.5).ToJson()).RootElement.GetProperty("type").GetString());

        var started = Assert.IsType<AudioStarted>(StageEvents.Parse(JsonDocument.Parse("""{"type":"audioStarted","channel":"bgm","url":"u","duration":123.4}""").RootElement));
        Assert.Equal(123.4, started.Duration);
        Assert.Equal(60.0, Assert.IsType<AudioSeeked>(StageEvents.Parse(JsonDocument.Parse("""{"type":"audioSeeked","channel":"bgm","time":60}""").RootElement)).Time);
        Assert.Equal("se", Assert.IsType<AudioEnded>(StageEvents.Parse(JsonDocument.Parse("""{"type":"audioEnded","channel":"se"}""").RootElement)).Channel);
        Assert.Equal("code 4", Assert.IsType<AudioError>(StageEvents.Parse(JsonDocument.Parse("""{"type":"audioError","channel":"v","url":"u","message":"code 4"}""").RootElement)).Message);

        // 추가(8회차): 재생 시간(stopAt) · 일시정지 · 끝 상태 · 시간 알림
        Assert.Equal(0.4, JsonDocument.Parse(new PlayAudio("line0", "u", 1, false, 0, 0.4).ToJson()).RootElement.GetProperty("stopAt").GetDouble());
        var pause = JsonDocument.Parse(new PauseAudio("line0", true).ToJson()).RootElement;
        Assert.Equal(("audioPause", "line0", true), (pause.GetProperty("type").GetString(), pause.GetProperty("channel").GetString(), pause.GetProperty("paused").GetBoolean()));
        var end = JsonDocument.Parse(new SeekEnd(7).ToJson()).RootElement;
        Assert.Equal(("seekEnd", 7), (end.GetProperty("type").GetString(), end.GetProperty("id").GetInt32()));
        Assert.Equal(new AudioTime("line0", 1.5, 3, true),
            StageEvents.Parse(JsonDocument.Parse("""{"type":"audioTime","channel":"line0","time":1.5,"duration":3,"paused":true}""").RootElement));
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
        Assert.True(a.Paused && b.Paused);         // 변경(8회차): 리소스 탭에서 연 캐릭터는 멈춘 채

        c.Vm.SelectedActor = a;
        c.Vm.HandleKey(" ");
        Assert.False(a.Paused);
        Assert.True(b.Paused);
        Assert.Equal(new SetPaused(false, a.Id), c.Stage.Last<SetPaused>());
        Assert.False(c.Vm.Paused);

        c.Vm.SelectedActor = null;
        Assert.False(c.Vm.Paused);                 // a가 재생 중
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
        c.Vm.HandleKey(" ");                        // 변경(8회차): 연 캐릭터는 멈춘 채 → 무대 전체 재생
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

    // ---------------------------------------------------------------- 추가(8회차): 통합 재생 바 · F5 줄 재생

    private static LinePlan Line(string? cg, double voiceSeconds, params LineSound[] sounds) =>
        new(3, "TEXT", cg, sounds, voiceSeconds, []);

    private static LineSound Voice(int i, double length) => new($"line{i}", $"Audio/Voice/IL/v{i}.wav", 1, length, 0);

    [Fact]
    public void LineF5_PlaysLineSpineWithVoiceLip_AfterLoad_OthersSeekEnd()
    {
        using var c = new Ctx();
        string k0 = c.Asset(0).CgKey, k1 = c.Asset(1).CgKey;
        c.Vm.ApplySnapshot(Snap((k0, "left", "wait"), (k1, "right", "wait")), Line(k0, 2.5, Voice(0, 2.5), Voice(1, 1)));
        var (line, other) = (c.Vm.Actors[0], c.Vm.Actors[1]);
        Assert.Same(line, c.Vm.LineActor);
        Assert.Same(line, c.Vm.SelectedActor);
        Assert.Equal(PlayTarget.Line, c.Vm.Target);
        Assert.Empty(c.Stage.Sent.OfType<PlayAudio>());          // Spine을 다 불러온 뒤 함께 시작
        Assert.True(other.SeekEndOnLoad);

        c.Load(other);
        Assert.Equal(new SeekEnd(other.Id), c.Stage.Last<SeekEnd>());
        Assert.False(other.Paused);
        c.Load(line);
        var lineplay = c.Stage.Sent.OfType<PlayAnims>().Last(p => p.Id == line.Id);
        Assert.Equal(2.5, lineplay.Lip!.Seconds);                // 립 = 가장 긴 보이스
        Assert.Equal(new[] { "line0", "line1" }, c.Stage.Sent.OfType<PlayAudio>().Select(p => p.Channel));
        Assert.Equal("https://resources.scsm/Audio/Voice/IL/v0.wav", c.Stage.Sent.OfType<PlayAudio>().First().Url);
        Assert.DoesNotContain(c.Stage.Sent.OfType<SeekEnd>(), s => s.Id == line.Id);

        // 바 길이 = 애니메이션(1초)과 소리(2.5초) 중 긴 것
        c.Stage.Raise(new ActorState(line.Id, [null, null, null, null], false, 0.5, 1, null));
        Assert.Equal("0.50 / 2.50s", c.Vm.TimeText);
        Assert.Equal(0.2, c.Vm.TimeValue, 3);

        // 다시 F5: 바뀌지 않은 다른 캐릭터는 그대로, 이전 줄 소리는 끔
        c.Stage.Sent.Clear();
        c.Vm.ApplySnapshot(Snap((k0, "left", null), (k1, "right", null)), Line(k0, 0));
        Assert.Empty(c.Stage.Sent.OfType<SeekEnd>());
        Assert.Equal(new[] { "line0", "line1" }, c.Stage.Sent.OfType<StopAudio>().Select(s => s.Channel));
        Assert.Contains(c.Stage.Sent.OfType<PlayAnims>(), p => p.Id == line.Id);   // 줄의 Spine은 늘 처음부터
        Assert.Equal(3.0, c.Stage.Sent.OfType<PlayAnims>().Last().Lip!.Seconds);    // 보이스 없음 → 설정대로
    }

    [Fact]
    public void LineToggle_PausesAndAlignsSoundsToBar_RestartsAtEnd()
    {
        using var c = new Ctx();
        string k0 = c.Asset(0).CgKey;
        c.Vm.ApplySnapshot(Snap((k0, "center", "wait")), Line(k0, 2, Voice(0, 2), Voice(1, 1)));
        var a = c.Vm.Actors[0];
        c.Load(a);
        c.Stage.Raise(new ActorState(a.Id, [null, null, null, null], false, 0.5, 1, null));

        c.Stage.Sent.Clear();
        c.Vm.HandleKey(" ");                                     // 일시정지: Spine + 소리 모두
        Assert.True(c.Vm.Paused);
        Assert.Equal(new SetPaused(true, a.Id), c.Stage.Last<SetPaused>());
        Assert.Equal(2, c.Stage.Sent.OfType<PauseAudio>().Count(p => p.Paused));

        c.Vm.TimeValue = 0.75;                                   // 1.5초로 이동
        Assert.Equal(new Seek(a.Id, 1.5), c.Stage.Last<Seek>());
        Assert.Equal(new SeekAudio("line0", 1.5), c.Stage.Sent.OfType<SeekAudio>().First());
        Assert.Equal(new SeekAudio("line1", 1), c.Stage.Sent.OfType<SeekAudio>().Last());

        c.Stage.Sent.Clear();
        c.Vm.HandleKey(" ");                                     // 이어 재생: 바 시점으로 맞춘 뒤 (끝난 line1은 그대로)
        Assert.False(c.Vm.Paused);
        Assert.Equal(new SeekAudio("line0", 1.5), Assert.Single(c.Stage.Sent.OfType<SeekAudio>()));
        Assert.Equal(new PauseAudio("line0", false), Assert.Single(c.Stage.Sent.OfType<PauseAudio>()));

        c.Stage.Raise(new ActorState(a.Id, [null, null, null, null], false, 5, 1, null));   // 끝 (바는 2초에서 멈춤)
        c.Stage.Sent.Clear();
        c.Vm.HandleKey(" ");                                     // 끝난 뒤 = 처음부터
        Assert.Equal(new Seek(a.Id, 0), c.Stage.Last<Seek>());
        Assert.Equal(2, c.Stage.Sent.OfType<PlayAudio>().Count());
    }

    [Fact]
    public void LineWithoutSpine_AudioDrivesBar_OtherTypesTargetStage()
    {
        using var c = new Ctx();
        var se = new LinePlan(5, "SE", null, [new LineSound("line0", "Audio/SE/se.mp3", 0.5, 0.4, 0.4)], 0, []);
        c.Vm.ApplySnapshot(Snap(), se);
        Assert.Equal(PlayTarget.Line, c.Vm.Target);
        Assert.Null(c.Vm.SelectedActor);
        var play = c.Stage.Last<PlayAudio>();
        Assert.Equal((0.5, 0.4), (play.Volume, play.StopAt));
        c.Stage.Raise(new AudioTime("line0", 0.2, 0.4, false));
        Assert.Equal(0.5, c.Vm.TimeValue, 3);
        Assert.Contains("5번 줄", c.Vm.TargetText);

        c.Vm.ApplySnapshot(Snap(), new LinePlan(6, "BG", null, [], 0, []));
        Assert.Equal(PlayTarget.Stage, c.Vm.Target);
        Assert.Equal("무대 전체", c.Vm.TargetText);
    }

    [Fact]
    public void Target_FollowsLastAction_OpenPaused_AnimListPlays()
    {
        using var c = new Ctx();
        c.Vm.Open(c.Asset(0), immediate: true);
        var a = c.Vm.Actors[0];
        c.Load(a);
        Assert.Equal(PlayTarget.Actor, c.Vm.Target);
        Assert.True(a.Paused);                                    // 리소스 탭에서 열면 자동 재생 안 함
        Assert.Equal(new SetPaused(true, a.Id), c.Stage.Last<SetPaused>());

        c.Vm.ApplyAnimCommand.Execute(c.Vm.AnimItems[1]);         // 애니메이션 목록에서 고르면 재생
        Assert.False(a.Paused);
        Assert.Equal(new SetPaused(false, a.Id), c.Stage.Last<SetPaused>());

        c.Stage.Raise(new StageClicked(null));                    // 빈 곳 클릭 = 무대 전체
        Assert.Equal(PlayTarget.Stage, c.Vm.Target);
        c.Stage.Raise(new StageClicked(a.Id));
        Assert.Equal(PlayTarget.Actor, c.Vm.Target);
    }

    // ---------------------------------------------------------------- 추가(9회차): CGGROUP 줄 F5 재생

    [Fact]
    public void Calculator_LineActors_GroupMembers_SurviveTransition()
    {
        SCTheaterEditor.Script.ScriptLine L(params (string, string)[] a) => SCTheaterEditor.Script.ScriptLine.Create(a);
        var lines = new List<SCTheaterEditor.Script.ScriptLine>
        {
            L(("Type", "CGGROUP"), ("GroupId", "duo"), ("CG", "a_stand_1"), ("Position", "left")),
            L(("Type", "CGGROUP"), ("GroupId", "duo"), ("CG", "b_stand_1"), ("Position", "right")),
            L(("Type", "TEXT"), ("Name", "둘"), ("CG", "duo")),
            L(("Type", "TEXT"), ("Name", "A"), ("CG", "a_stand_1")),
            L(("Type", "TEXT"), ("Name", "A"), ("Text", "CG 없음")),
            L(("Type", "TRANSITION")),
            L(("Type", "TEXT"), ("Name", "둘"), ("CG", "duo")),
        };
        Assert.Equal(new[] { "a_stand_1", "b_stand_1" }, SCTheaterEditor.Script.StageStateCalculator.Compute(lines, 2).LineActors);
        Assert.Equal(new[] { "a_stand_1" }, SCTheaterEditor.Script.StageStateCalculator.Compute(lines, 3).LineActors);
        Assert.Empty(SCTheaterEditor.Script.StageStateCalculator.Compute(lines, 4).LineActors!);
        Assert.Equal(new[] { "a_stand_1", "b_stand_1" }, SCTheaterEditor.Script.StageStateCalculator.Compute(lines, 6).LineActors);   // 게임과 같이 그룹은 막 동안 유지 (미결 #7)
    }

    [Fact]
    public void GroupLineF5_AllMembersPlayWithLip_SoundsAfterAllLoaded_ToggleAll()
    {
        using var c = new Ctx();
        string k0 = c.Asset(0).CgKey, k1 = c.Asset(1).CgKey, k2 = c.Asset(2).CgKey;
        var snap = new SCTheaterEditor.Script.StageSnapshot(0, null,
            [new(k2, "center", "wait"), new(k0, "left", "wait"), new(k1, "right", "wait")], [], [k1, k0]);   // 그룹 순서 k1 → k0
        c.Vm.ApplySnapshot(snap, new LinePlan(7, "TEXT", "duo", [Voice(0, 2)], 2, []));
        var (a0, a1, a2) = (c.Vm.Actors.Single(a => a.Asset.CgKey == k0), c.Vm.Actors.Single(a => a.Asset.CgKey == k1), c.Vm.Actors.Single(a => a.Asset.CgKey == k2));
        Assert.Same(a1, c.Vm.LineActor);                          // 그룹 순서의 첫 멤버가 바 기준 · 선택
        Assert.Same(a1, c.Vm.SelectedActor);
        Assert.Contains("그룹 duo 2명", c.Vm.TargetText);
        Assert.True(a2.SeekEndOnLoad);                            // 그룹 밖 캐릭터는 end-loop
        Assert.False(a0.SeekEndOnLoad || a1.SeekEndOnLoad);

        c.Load(a1);
        Assert.Empty(c.Stage.Sent.OfType<PlayAudio>());          // 멤버를 모두 불러온 뒤 소리 시작
        c.Load(a0);
        Assert.Single(c.Stage.Sent.OfType<PlayAudio>());
        Assert.All(new[] { a0, a1 }, m => Assert.Equal(2, c.Stage.Sent.OfType<PlayAnims>().Last(p => p.Id == m.Id).Lip!.Seconds));

        c.Stage.Sent.Clear();
        c.Vm.HandleKey(" ");                                     // 일시정지: 멤버 전원 + 소리
        Assert.Equal(new[] { a1.Id, a0.Id }, c.Stage.Sent.OfType<SetPaused>().Select(p => p.Id!.Value));
        Assert.True(a0.Paused && a1.Paused && !a2.Paused);
    }
}
