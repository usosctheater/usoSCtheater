using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>추가(리소스 탭 막 사용 표시): 스크립트 표 UsedCgKeys — 막 열기·편집·되돌리기 때 바뀌고 알림</summary>
public class ActUsedKeysTests
{
    private const string Act =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<Act mainTitle=\"t\">\r\n" +
        "\t<Line Type=\"TEXT\" CharId=\"asahi\" CG=\"asahi_stand_1\" Text=\"a\"/>\r\n" +
        "\t<Line Type=\"SETCG\" CG=\"fuyuko_stand_2\"/>\r\n" +
        "\t<Line Type=\"CGGROUP\" GroupId=\"grp\" CG=\"mei_stand_3\"/>\r\n" +
        "\t<Line Type=\"TEXT\" CG=\"grp\" Text=\"b\"/>\r\n" +
        "\t<Line Type=\"TEXT\" CG=\"none\" Text=\"c\"/>\r\n" +
        "\t<Line Type=\"BG\" CG=\"not_counted_9\"/>\r\n" +
        "</Act>\r\n";

    [Fact]
    public void UsedCgKeys_FollowDocument()
    {
        using var temp = new TempFolder();
        string dir = temp.File("IL");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "IL01.xml");
        File.WriteAllText(path, Act);
        var lib = new SpineLibrary(temp.File("SpineData"), new SpineManifest());
        lib.Rebuild();
        var editor = new ActEditorViewModel(new AppSettings(), lib, new FakeDialogs(), new ImmediateDispatcher(), () => { }, new AppLog(), temp.File("Backups"));
        int changed = 0;
        editor.UsedCgKeysChanged += () => changed++;
        Assert.True(editor.Open(path));
        Assert.Equal(new[] { "asahi_stand_1", "fuyuko_stand_2", "mei_stand_3" }, editor.UsedCgKeys.Order(StringComparer.Ordinal));   // 그룹 이름·none·CG 아닌 줄 제외
        Assert.True(changed >= 1);

        var doc = editor.Document!;
        int before = changed;
        doc.SetValuesMany([(doc.Lines[1], "CG", "luca_stand_4")], "시험");
        Assert.Contains("luca_stand_4", editor.UsedCgKeys);
        Assert.DoesNotContain("fuyuko_stand_2", editor.UsedCgKeys);
        Assert.True(changed > before);
        doc.UndoLast();
        Assert.Contains("fuyuko_stand_2", editor.UsedCgKeys);
    }
}
