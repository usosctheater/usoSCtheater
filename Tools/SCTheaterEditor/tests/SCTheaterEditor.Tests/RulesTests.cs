using SCTheaterEditor.Services;

namespace SCTheaterEditor.Tests;

/// <summary>명명 규칙 (2026-10-08 확정 규칙 고정)</summary>
public class SpineNamingTests
{
    [Theory]
    [InlineData("idols", "cb", "1040210050", "cb_1040210050")]
    [InlineData("awake_idols", "cb_costume", "1040210050", "awake_cb_costume_1040210050")]
    [InlineData("support_idols", "stand", "1040210050", "support_stand_1040210050")]
    [InlineData("idol_evolution_skins", "stand", "104021005001", "evo_stand_104021005001")]
    [InlineData("new_group", "cb", "1", "new_group_cb_1")]   // 처음 보는 그룹은 그룹명_
    public void BaseName(string group, string type, string id, string expected) =>
        Assert.Equal(expected, SpineNaming.BaseName(group, type, id));

    [Theory]
    [InlineData("1040210050", "spine/idols/cb/1040210050/", "1040210050")]
    [InlineData("1040210050", "spine/idol_evolution_skins/stand/104021005003/", "104021005003")]
    [InlineData("1040210050", "spine/sub_characters/stand/luca/", "1040210050")]   // 경로에 enzaId가 없으면 enzaId
    public void FileId(string enzaId, string path, string expected) =>
        Assert.Equal(expected, SpineNaming.FileId(enzaId, path));

    [Theory]
    [InlineData("【セツナ/n】", "【セツナn】")]
    [InlineData("【UNCHARTE:D】", "【UNCHARTED】")]
    [InlineData("con", "_con")]
    [InlineData("abc. ", "abc")]
    [InlineData("???", "_")]
    public void CleanName(string input, string expected) =>
        Assert.Equal(expected, SpineNaming.CleanName(input));
}

/// <summary>트랙 배치 ↔ Animation 문자열 (CGManager와 같은 규칙)</summary>
public class AnimationScriptTests
{
    [Theory]
    [InlineData("smile1", 0)]
    [InlineData("eye_close", 0)]   // eye_는 아직 트랙 0 (프로젝트와 함께 분리 예정)
    [InlineData("face_anger", 1)]
    [InlineData("LIP_a", 2)]
    [InlineData("arm_down", 3)]
    public void TrackIndex(string name, int expected) =>
        Assert.Equal(expected, AnimationScript.TrackIndex(name));

    [Fact]
    public void Build_SkipsEmptyTracks() =>
        Assert.Equal("anger1 lip_a", AnimationScript.Build(new[] { "anger1", null, "lip_a", "" }));   // 변경(4-1): 공백 구분

    [Fact]
    public void Parse_SpacesAndCommas_LastWinsOnSameTrack()
    {
        var tracks = AnimationScript.Parse("think1 face_serious,lip_sad  think2");
        Assert.Equal(new string?[] { "think2", "face_serious", "lip_sad", null }, tracks);
    }

    [Fact]
    public void Mismatches_FindsWrongTrack()
    {
        var m = AnimationScript.Mismatches(new[] { "face_x", null, null, "arm_y" });
        Assert.Single(m);
        Assert.Equal(("face_x", 0), m[0]);
    }
}

/// <summary>립 결정 규칙 (CGManager.LipSyncCoroutine 이식, 내장 DefaultLipData.xml 사용)</summary>
public class LipRulesTests
{
    private static readonly LipRules Rules = LipRules.LoadEmbedded();

    private static Func<string, bool> Has(params string[] names)
    {
        var set = names.ToHashSet();
        return set.Contains;
    }

    [Fact]
    public void NoLipAnims_UsesIdolCategory() =>
        Assert.Equal(("lip_anger", "lip_anger_s"), Rules.Resolve(new[] { "anger1" }, "asahi", Has()));

    [Fact]
    public void CandidateExists_StillFromCategory() =>
        // lip_surp는 데이터에 있고 lip_surp_s는 없음 → 멈춤 립만 아사히 Surp 기본값
        Assert.Equal(("lip_surp", "lip_serious_s"), Rules.Resolve(new[] { "surp2" }, "asahi", Has("lip_surp")));

    [Fact]
    public void ExplicitLip_StillFromIdolDefault() =>
        // 2026-10-12 실제 데이터로 확인한 사례: mei anger1 + lip_a → lip_a / lip_smile_s
        Assert.Equal(("lip_a", "lip_smile_s"), Rules.Resolve(new[] { "anger1", "lip_a" }, "mei", Has()));

    [Fact]
    public void UnknownIdol_FallsBackToLipWait() =>
        Assert.Equal(("lip_wait", "lip_wait_s"), Rules.Resolve(new[] { "smile1" }, "madoka", Has("lip_wait", "lip_wait_s")));

    [Theory]
    [InlineData("smile3", "smile")]
    [InlineData("anger12", "anger")]
    [InlineData(null, "wait")]
    [InlineData("", "wait")]
    public void ExtractLipKey(string? anim, string expected) =>
        Assert.Equal(expected, LipRules.ExtractLipKey(anim));
}

/// <summary>atlas 페이지 줄 찾기, 텍스처 형식 판별</summary>
public class AtlasAndTextureTests
{
    [Fact]
    public void AtlasPages_FindsPages()
    {
        string atlas = "\ndata.png\nsize: 2048,2048\nformat: RGBA8888\nhead\n  rotate: false\n\ndata2.png\nsize: 1024,1024\narm\n  rotate: true\n";
        var (lines, pages) = AtlasPages.Parse(atlas);
        Assert.Equal(new[] { "data.png", "data2.png" }, pages.Select(i => lines[i].Trim()));
    }

    [Fact]
    public void AtlasPages_RejectsBrokenFormat() =>
        Assert.Throws<RemoteFileException>(() => AtlasPages.Parse("size: 2048,2048\n"));

    [Fact]
    public void DetectFormat()
    {
        Assert.Equal("png", TextureConverter.DetectFormat(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
        Assert.Equal("webp", TextureConverter.DetectFormat("RIFF\0\0\0\0WEBP"u8.ToArray()));
        Assert.Equal("unknown", TextureConverter.DetectFormat(new byte[] { 1, 2, 3 }));
    }
}
