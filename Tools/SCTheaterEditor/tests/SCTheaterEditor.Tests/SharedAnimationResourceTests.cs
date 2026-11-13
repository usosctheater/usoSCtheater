using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using SCTheaterEditor.Stage;
using UsoSCTheater.Shared;

namespace SCTheaterEditor.Tests;

/// <summary>[공유 코드 통합] 5회차: 공유 애니메이션 규칙·소리 규칙·리소스 폴더 — 게임 CGManager·AudioManager와 TE가 같은 결과</summary>
public class SharedAnimationResourceTests
{
    [Fact]
    public void SplitList_SpacesCommas()
    {
        Assert.Equal(["think1", "face_serious", "lip_sad", "think2"], ActFormat.SplitList("think1 face_serious,lip_sad  think2"));
        Assert.Empty(ActFormat.SplitList(" , "));
        Assert.Empty(ActFormat.SplitList(null));
    }

    [Theory]
    [InlineData("wait", false, SpineAnimMode.Loop)]
    [InlineData("blank", false, SpineAnimMode.Loop)]
    [InlineData("wait", true, SpineAnimMode.EndLoop)]      // loop_start 이벤트가 우선
    [InlineData("anger1", false, SpineAnimMode.Hold)]
    [InlineData("Wait", false, SpineAnimMode.Hold)]        // 강제 반복 이름은 정확히 (Spine 이름)
    public void ModeOf(string name, bool hasLoopStart, SpineAnimMode expected) =>
        Assert.Equal(expected, AnimationRules.ModeOf(name, hasLoopStart));

    [Theory]
    [InlineData("FACE_x", 1)]
    [InlineData(" lip_a", 2)]
    [InlineData("arm_up", 3)]
    [InlineData("eye_close", 0)]
    public void TrackOf(string name, int track) => Assert.Equal(track, AnimationRules.TrackOf(name));

    [Fact]
    public void AudioSlots()
    {
        Assert.Equal(["bgm", "1", "2", "3"], AudioRules.Slots);
        Assert.True(AudioRules.IsSlot(" BGM "));
        Assert.False(AudioRules.IsSlot("4"));
        Assert.False(AudioRules.IsSlot(""));
        Assert.Equal(ResourceFolders.AudioBGM, AudioRules.SlotFolder("Bgm"));
        Assert.Equal(ResourceFolders.AudioSE, AudioRules.SlotFolder("2"));
    }

    [Fact]
    public void VoicePaths_ScenarioFirstThenRoot()
    {
        Assert.Equal(["Audio/Voice/IL/s001", "Audio/Voice/s001"], AudioRules.VoicePaths(" IL/ ", " s001 "));
        Assert.Equal(["Audio/Voice/OT/OT001"], AudioRules.VoicePaths(null, @"OT\OT001"));
        Assert.Equal("Spine/asahi", ResourceFolders.SpineFolder("asahi"));
    }

    [Fact]
    public void CheckLine_UnknownAudioSlot()
    {
        var issues = new List<ActIssue>();
        var langs = new List<LanguageInfo> { new("ko", "한국어", true) };
        ActScript.CheckLine(new ActAttrs([new("Type", "AUDIO"), new("Value", "se"), new("Track", "x")]), false, langs, _ => true, issues);
        Assert.Equal("Value", Assert.Single(issues).Attr);
        issues.Clear();
        ActScript.CheckLine(new ActAttrs([new("Type", "AUDIO"), new("Value", "1"), new("Track", "x")]), false, langs, _ => true, issues);
        Assert.Empty(issues);
    }

    [Fact]
    public void LinePlayback_UnknownSlotNotPlayed()
    {
        var line = ScriptLine.Create(("Type", "AUDIO"), ("Value", "4"), ("Track", "bgm_x"));
        var plan = LinePlayback.Build(line, 1, null, null);
        Assert.Empty(plan.Sounds);
        Assert.Contains(plan.Missing, m => m.Contains("없는 AUDIO 슬롯 '4'"));
    }

    [Fact]
    public void Backgrounds_NoWebp() => Assert.DoesNotContain(".webp", BackgroundLibrary.Extensions);
}
