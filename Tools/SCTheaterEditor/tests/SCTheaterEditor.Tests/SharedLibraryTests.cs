using UsoSCTheater.Shared;
using Xunit.Abstractions;

namespace SCTheaterEditor.Tests;

/// <summary>추가(공유 라이브러리): 공유 규칙 검사(게임에서만 쓰던 해석·짝 SD 포함) + 사본(src/Shared) = 원본(게임 패키지) 확인</summary>
public class SharedLibraryTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("fuyuko_cb_1050180010", "fuyuko", "cb", "1050180010", SpineKind.SD, false)]
    [InlineData("mei_stand_costume_1040190030", "mei", "stand_costume", "1040190030", SpineKind.LD, true)]
    [InlineData(" toru_awake_stand_1040210050 ", "toru", "awake_stand", "1040210050", SpineKind.LD, false)]
    public void TryParse_Parts(string key, string id, string type, string enza, SpineKind kind, bool costume)
    {
        Assert.True(SpineKeys.TryParse(key, out var info));
        Assert.Equal((key.Trim(), id, type, enza, kind, costume), (info.Key, info.CharacterId, info.Type, info.EnzaId, info.Kind, info.IsCostume));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("broken")] [InlineData("_stand_1")] [InlineData("mano__1")] [InlineData("mano_stand_abc")] [InlineData("mano_stand_")]
    public void TryParse_Invalid(string? key) => Assert.False(SpineKeys.TryParse(key, out _));

    [Theory]
    [InlineData("stand", "cb")] [InlineData("stand_costume", "cb_costume")] [InlineData("awake_stand", "awake_cb")]
    [InlineData("support_stand", null)] [InlineData("cb", null)]
    public void PairedSDType(string ldType, string? sdType) => Assert.Equal(sdType, SpineKeys.GetPairedSDType(ldType));

    // 원본이 있는 환경(이 저장소)에서만 비교 — 없으면 통과 + 한 줄 남김 (dotnet test 상세 출력에 보임)
    [Fact]
    public void SharedCopy_SameAsOriginal()
    {
        string teRoot = FindTeRoot();
        string copy = Path.Combine(teRoot, "src", "Shared");
        string original = Path.GetFullPath(Path.Combine(teRoot, "..", "..", "usoSCtheater", "Packages", "com.usosctheater.shared", "Runtime"));
        if (!Directory.Exists(original)) { output.WriteLine($"원본 패키지 없음 — 비교 건너뜀 ({original})"); return; }

        var a = CsFiles(original);
        var b = CsFiles(copy);
        Assert.True(a.Keys.SequenceEqual(b.Keys), $"파일 목록이 다름 → TE 폴더에서 node sync-shared.js\n원본: {string.Join(", ", a.Keys)}\n사본: {string.Join(", ", b.Keys)}");
        foreach (var f in a.Keys) Assert.True(a[f] == b[f], $"내용이 다름: {f} → TE 폴더에서 node sync-shared.js");
    }

    // .cs만, bin·obj 제외, 줄바꿈 통일 (BOM은 ReadAllText가 뗌)
    private static SortedDictionary<string, string> CsFiles(string dir) => new(
        Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Select(p => (rel: Path.GetRelativePath(dir, p).Replace('\\', '/'), p))
            .Where(x => !x.rel.StartsWith("bin/") && !x.rel.StartsWith("obj/"))
            .ToDictionary(x => x.rel, x => File.ReadAllText(x.p).Replace("\r\n", "\n")),
        StringComparer.Ordinal);

    private static string FindTeRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "SCTheaterEditor.slnx"))) return d.FullName;
        throw new InvalidOperationException("SCTheaterEditor.slnx를 찾지 못함");
    }
}
