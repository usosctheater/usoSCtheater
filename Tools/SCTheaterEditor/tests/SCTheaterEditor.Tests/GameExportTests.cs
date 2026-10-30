using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>추가(2026-10-16): TE 세트 → 게임 Resources/Spine 내보내기</summary>
public class GameExportTests
{
    private const string Atlas = "\nstand_3000180060.png\nsize: 1024,1024\nformat: RGBA8888\nfilter: Linear,Linear\nrepeat: none\narm_L\n  rotate: false\n";

    /// <summary>원본 세트 1개를 SpineData 흉내 폴더에 만들고 SpineAsset을 돌려줌</summary>
    private static (SpineLibrary Lib, SpineAsset Asset) MakeSet(TempFolder t, string nick = "fuyuko", string baseName = "stand_3000180060",
                                                              string? atlas = null, int pages = 1)
    {
        string root = Path.Combine(t.Path, "SpineData");
        string rel = Path.Combine("Special", "【FAN感謝祭】黛冬優子");
        Directory.CreateDirectory(Path.Combine(root, rel));
        var images = Enumerable.Range(1, pages).Select(i => i == 1 ? $"{baseName}.png" : $"{baseName}_{i}.png").ToList();
        File.WriteAllText(Path.Combine(root, rel, baseName + ".json"), "{\"skeleton\":{}}");
        File.WriteAllText(Path.Combine(root, rel, baseName + ".atlas.txt"), atlas ?? Atlas.Replace("stand_3000180060", baseName));
        for (int i = 0; i < images.Count; i++) File.WriteAllBytes(Path.Combine(root, rel, images[i]), [1, 2, 3, (byte)i]);

        var asset = new SpineAsset(SpineLibrary.MakeCgKey(nick, baseName), "spine/x/", baseName, "idols", "stand", 18, "黛冬優子", nick, 17,
                                   "Special", 9, "【FAN感謝祭】黛冬優子", "3000180060", rel, baseName + ".json", baseName + ".atlas.txt",
                                   images, new DateTime(2026, 10, 16));
        return (new SpineLibrary(root, new SpineManifest()), asset);
    }

    private static string GameFolder(TempFolder t) => Path.Combine(t.Path, "Game", "Spine");

    [Fact]
    public void RewriteAtlas_ChangesOnlyPageLines_KeepsCrLf()
    {
        string src = "\r\nstand_1.png\r\nsize: 1,1\r\nstand_1_2.png\r\nregion\r\n";
        var pages = new Dictionary<string, string> { ["stand_1.png"] = "k_stand_1.png", ["stand_1_2.png"] = "k_stand_1_2.png" };
        Assert.Equal("\r\nk_stand_1.png\r\nsize: 1,1\r\nk_stand_1_2.png\r\nregion\r\n", GameSpineExporter.RewriteAtlas(src, pages));
    }

    [Fact]
    public void Export_CopiesWithKeyNames_AndRewritesAtlas()
    {
        using var t = new TempFolder();
        var (lib, a) = MakeSet(t);
        var r = new GameSpineExporter(lib).Export(GameFolder(t), a, (_, _) => throw new InvalidOperationException("묻지 않아야 함"));

        Assert.Equal(GameExportStatus.Copied, r.Status);
        string dir = Path.Combine(GameFolder(t), "fuyuko");
        Assert.True(File.Exists(Path.Combine(dir, "fuyuko_stand_3000180060.json")));
        Assert.True(File.Exists(Path.Combine(dir, "fuyuko_stand_3000180060.png")));
        string atlas = File.ReadAllText(Path.Combine(dir, "fuyuko_stand_3000180060.atlas.txt"));
        Assert.StartsWith("\nfuyuko_stand_3000180060.png\nsize: 1024,1024", atlas);
        Assert.True(GameSpineExporter.ExistsInGame(GameFolder(t), a));
    }

    [Fact]
    public void Export_Again_IsSame_WithoutAsking()
    {
        using var t = new TempFolder();
        var (lib, a) = MakeSet(t);
        var exporter = new GameSpineExporter(lib);
        exporter.Export(GameFolder(t), a, (_, _) => true);
        var r = exporter.Export(GameFolder(t), a, (_, _) => throw new InvalidOperationException("묻지 않아야 함"));
        Assert.Equal(GameExportStatus.Same, r.Status);
    }

    /// <summary>추가(5회차): git 체크아웃이 atlas 줄바꿈을 CRLF로 바꿔 둔 경우 — 같음으로 보고 파일을 건드리지 않음. 글자가 다르면 그대로 "다름"</summary>
    [Fact]
    public void Export_AtlasCrlfOnly_IsSame_ButRealChangeDiffers()
    {
        using var t = new TempFolder();
        var (lib, a) = MakeSet(t);
        var exporter = new GameSpineExporter(lib);
        exporter.Export(GameFolder(t), a, (_, _) => true);
        string atlas = Path.Combine(GameFolder(t), "fuyuko", "fuyuko_stand_3000180060.atlas.txt");
        string crlf = File.ReadAllText(atlas).Replace("\n", "\r\n");
        File.WriteAllText(atlas, crlf);

        var r = exporter.Export(GameFolder(t), a, (_, _) => throw new InvalidOperationException("묻지 않아야 함"));
        Assert.Equal(GameExportStatus.Same, r.Status);
        Assert.Equal(crlf, File.ReadAllText(atlas));                     // 덮어쓰지 않음

        File.WriteAllText(atlas, crlf.Replace("size: 1024,1024", "size: 512,512"));
        IReadOnlyList<string>? asked = null;
        exporter.Export(GameFolder(t), a, (_, files) => { asked = files; return false; });
        Assert.Equal(new[] { "fuyuko_stand_3000180060.atlas.txt" }, asked);
    }

    [Fact]
    public void Export_Different_AsksThenSkipsOrOverwrites()
    {
        using var t = new TempFolder();
        var (lib, a) = MakeSet(t);
        var exporter = new GameSpineExporter(lib);
        exporter.Export(GameFolder(t), a, (_, _) => true);
        string png = Path.Combine(GameFolder(t), "fuyuko", "fuyuko_stand_3000180060.png");
        File.WriteAllBytes(png, [9, 9]);

        IReadOnlyList<string>? asked = null;
        var skip = exporter.Export(GameFolder(t), a, (_, files) => { asked = files; return false; });
        Assert.Equal(GameExportStatus.Skipped, skip.Status);
        Assert.Equal(new[] { "fuyuko_stand_3000180060.png" }, asked);
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(png));   // 건너뛰면 그대로

        var over = exporter.Export(GameFolder(t), a, (_, _) => true);
        Assert.Equal(GameExportStatus.Overwritten, over.Status);
        Assert.Equal(new byte[] { 1, 2, 3, 0 }, File.ReadAllBytes(png));
    }

    [Fact]
    public void Export_MultiPage_RenamesEveryPage()
    {
        using var t = new TempFolder();
        string atlas = "\nsupport_picture_motion_1.png\nsize: 1,1\nr1\n\nsupport_picture_motion_1_2.png\nsize: 1,1\nr2\n";
        var (lib, a) = MakeSet(t, "asahi", "support_picture_motion_1", atlas, pages: 2);
        Assert.Equal(GameExportStatus.Copied, new GameSpineExporter(lib).Export(GameFolder(t), a, (_, _) => true).Status);

        string dir = Path.Combine(GameFolder(t), "asahi");
        Assert.True(File.Exists(Path.Combine(dir, "asahi_support_picture_motion_1_2.png")));
        string written = File.ReadAllText(Path.Combine(dir, "asahi_support_picture_motion_1.atlas.txt"));
        Assert.Contains("\nasahi_support_picture_motion_1.png\n", written);
        Assert.Contains("\nasahi_support_picture_motion_1_2.png\n", written);
    }

    [Fact]
    public void Export_NoNickName_Fails()
    {
        using var t = new TempFolder();
        var (lib, a) = MakeSet(t, nick: "");
        var r = new GameSpineExporter(lib).Export(GameFolder(t), a, (_, _) => true);
        Assert.Equal(GameExportStatus.Failed, r.Status);
        Assert.False(Directory.Exists(GameFolder(t)));
    }

    [Fact]
    public void CollectActSpines_SkipsNoneGroupsDuplicates_ReportsUnknown()
    {
        using var t = new TempFolder();
        var snap = Fixture.Snapshot();
        var plan = SpinePlanner.Build(snap);
        var m = Fixture.Downloaded(t.Path, plan, v1: false);
        m.UpdateIdols(snap.Idols);
        var lib = new SpineLibrary(t.Path, m);
        lib.Rebuild();

        string act = Path.Combine(t.Path, "IL01.xml");
        File.WriteAllText(act, """
            <?xml version="1.0" encoding="UTF-8"?>
            <Act mainTitle="m" subTitle="s">
            	<Line Type="TEXT" Name="円香" Text="a" CG="madoka_stand_1040210050" SpeakerType="1"/>
            	<Line Type="CGGROUP" GroupId="G1" CG="mano_stand_1040010010" Position="left"/>
            	<Line Type="TEXT" Name="円香" Text="b" CG="G1" SpeakerType="1"/>
            	<Line Type="TEXT" Name="円香" Text="c" CG="MADOKA_stand_1040210050" SpeakerType="1"/>
            	<Line Type="TEXT" Name="x" Text="d" CG="none" SpeakerType="1"/>
            	<Line Type="SETCG" CG="old_key_1"/>
            	<Line Type="BG" ResourceKey="bg_x"/>
            </Act>
            """);
        var vm = new ActEditorViewModel(new AppSettings(), lib, new FakeDialogs(), new ImmediateDispatcher(), () => { }, new AppLog(),
                                        Path.Combine(t.Path, "Backups"));
        Assert.True(vm.Open(act));

        var (assets, unknown) = vm.CollectActSpines();
        Assert.Equal(new[] { "madoka_stand_1040210050", "mano_stand_1040010010" }, assets.Select(x => x.CgKey));
        Assert.Equal(new[] { "old_key_1" }, unknown);
    }
}
