using SCTheaterEditor.Models;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.Tests;

/// <summary>목록 스냅샷 → 다운로드 계획 (건너뛰기·공유 경로·이름 충돌)</summary>
public class SpinePlannerTests
{
    private static DressInfo Dress(int idol, string enza, string name, string type, params (string group, string? path, string spineType)[] assets) => new()
    {
        Exist = true, IdolId = idol, EnzaId = enza, DressName = name, DressType = type, DressUuid = enza + name,
        Assets = assets.GroupBy(a => a.group).ToDictionary(g => g.Key,
            g => g.Select(a => new DressAsset { Path = a.path, Type = a.spineType }).ToList()),
    };

    private static CatalogSnapshot Snapshot()
    {
        var snap = new CatalogSnapshot();
        snap.Idols.Add(new IdolInfo { IdolId = 21, IdolName = "樋口円香", NickName = "madoka" });
        snap.Idols.Add(new IdolInfo { IdolId = 3, IdolName = "八宮めぐる", NickName = "meguru" });

        snap.Dresses[21] = new()
        {
            Dress(21, "1040210050", "【Merry】樋口円香", "P_SSR",
                ("idols", "spine/idols/cb/1040210050/", "cb"),
                ("idols", "spine/idols/stand/1040210050/", "stand"),
                ("awake_idols", "spine/awake_idols/stand_costume/1040210050/", "stand_costume"),
                ("idols", null, "cb_costume")),                                   // path 없음 → 건너뜀
            new DressInfo { Exist = false, IdolId = 21, EnzaId = "1", DressName = "없는 의상", DressType = "P_SR" },   // exist:false
        };
        snap.Dresses[3] = new()
        {
            // 같은 서버 경로를 두 의상이 공유 → 1세트 + 복사 1
            Dress(3, "1040030370", "【S.T.E.P.】八宮めぐる", "Special", ("idols", "spine/idols/stand/1040030370/", "stand")),
            Dress(3, "1040030370", "【エモーショナルユニフォーム】八宮めぐる", "Special", ("idols", "spine/idols/stand/1040030370/", "stand")),
        };
        return snap;
    }

    [Fact]
    public void Build_CountsSetsSkipsAndCopies()
    {
        var plan = SpinePlanner.Build(Snapshot());

        Assert.Equal(4, plan.DressCount);
        Assert.Equal(4, plan.Sets.Count);                 // 円香 3 + めぐる 1
        Assert.Equal(1, plan.CopyCount);                  // めぐる 공유 경로
        Assert.Equal(2, plan.Skipped.Count);              // exist:false 1 + path 없음 1
        Assert.Empty(plan.Conflicts);

        var awake = plan.Sets.Single(s => s.Group == "awake_idols");
        Assert.Equal("awake_stand_costume_1040210050", awake.BaseName);
        Assert.Equal(Path.Combine("P_SSR", "【Merry】樋口円香"), awake.Targets[0].RelativeFolder);
    }
}
