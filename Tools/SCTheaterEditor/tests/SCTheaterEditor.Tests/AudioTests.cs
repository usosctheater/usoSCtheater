using System.Buffers.Binary;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Models;
using SCTheaterEditor.Services;
using SCTheaterEditor.Stage;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>추가(6회차): 오디오 목록 — 길이 읽기 · 목록 · 메모 · 미리 듣기 · 줄에 넣기</summary>
public class AudioTests
{
    // ------------------------------------------------------------------ 테스트용 파일

    /// <summary>PCM wav: 초당 바이트 byteRate, 데이터 dataBytes (+ 앞에 LIST 청크로 청크 건너뛰기 확인)</summary>
    internal static byte[] Wav(int byteRate, int dataBytes)
    {
        var ms = new MemoryStream();
        void U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); ms.Write(b); }
        void U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); ms.Write(b); }
        ms.Write("RIFF"u8); U32((uint)(4 + 8 + 16 + 8 + 3 + 1 + 8 + dataBytes)); ms.Write("WAVE"u8);
        ms.Write("fmt "u8); U32(16); U16(1); U16(2); U32((uint)(byteRate / 4)); U32((uint)byteRate); U16(4); U16(16);
        ms.Write("LIST"u8); U32(3); ms.Write("abc"u8); ms.WriteByte(0);   // 홀수 크기 청크 + 맞춤 바이트
        ms.Write("data"u8); U32((uint)dataBytes); ms.Write(new byte[dataBytes]);
        return ms.ToArray();
    }

    /// <summary>MPEG1 Layer III 44.1kHz 스테레오 128kbps 프레임 머리 (+ Xing이면 프레임 수)</summary>
    internal static byte[] Mp3(int totalBytes, uint? xingFrames = null, bool id3 = false)
    {
        var data = new byte[totalBytes];
        int at = 0;
        if (id3)
        {
            "ID3"u8.CopyTo(data);
            data[3] = 3;
            data[9] = 100;   // 태그 크기 100 (syncsafe)
            at = 110;
        }
        data[at] = 0xFF; data[at + 1] = 0xFB; data[at + 2] = 0x90; data[at + 3] = 0x00;   // 128kbps · 44100 · 스테레오
        if (xingFrames is { } frames)
        {
            int x = at + 4 + 32;
            "Xing"u8.CopyTo(data.AsSpan(x));
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(x + 4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(x + 8), frames);
        }
        return data;
    }

    [Fact]
    public void Duration_Wav_Mp3Cbr_Mp3Xing()
    {
        Assert.Equal(2.5, AudioDuration.Wav(new MemoryStream(Wav(176400, 441000))));
        // 고정 비트레이트: (파일 - 태그) × 8 / 128000
        Assert.Equal(16000 * 8 / 128000.0, AudioDuration.Mp3(new MemoryStream(Mp3(16000))));
        Assert.Equal(16000 * 8 / 128000.0, AudioDuration.Mp3(new MemoryStream(Mp3(16110, id3: true))));
        // Xing 프레임 수 × 1152 / 44100
        Assert.Equal(1000 * 1152 / 44100.0, AudioDuration.Mp3(new MemoryStream(Mp3(4000, xingFrames: 1000)))!.Value, 6);
        Assert.Null(AudioDuration.Mp3(new MemoryStream(new byte[100])));
        Assert.Null(AudioDuration.Wav(new MemoryStream("RIFFxxxxWAVX"u8.ToArray())));
        Assert.Equal("2.6초", AudioLibrary.FormatDuration(2.56));
        Assert.Equal("10:30", AudioLibrary.FormatDuration(630.3));
        Assert.Equal("-", AudioLibrary.FormatDuration(null));
    }

    /// <summary>게임 Resources 흉내: Audio\SE · BGM · Voice\IL · Voice\OT, Scenario\IL\IL01.xml</summary>
    private static string MakeResources(string parent)
    {
        string res = Path.Combine(parent, "Resources");
        foreach (var d in new[] { @"Audio\SE", @"Audio\BGM", @"Audio\Voice\IL", @"Audio\Voice\OT", @"Scenario\IL" })
            Directory.CreateDirectory(Path.Combine(res, d));
        File.WriteAllBytes(Path.Combine(res, @"Audio\SE\se_b.mp3"), Mp3(16000));
        File.WriteAllBytes(Path.Combine(res, @"Audio\SE\se_a.wav"), Wav(176400, 17640));
        File.WriteAllBytes(Path.Combine(res, @"Audio\SE\se_a.mp3"), Mp3(16000));   // 같은 키 → mp3가 앞
        File.WriteAllText(Path.Combine(res, @"Audio\SE\readme.txt"), "x");         // 오디오 아님
        File.WriteAllBytes(Path.Combine(res, @"Audio\BGM\BGM_10.mp3"), Mp3(16000));
        File.WriteAllBytes(Path.Combine(res, @"Audio\BGM\BGM_2.mp3"), Mp3(16000));
        File.WriteAllBytes(Path.Combine(res, @"Audio\Voice\IL\s001001.wav"), Wav(176400, 441000));
        File.WriteAllBytes(Path.Combine(res, @"Audio\Voice\OT\OT001.wav"), Wav(176400, 176400));
        File.WriteAllText(Path.Combine(res, @"Scenario\IL\IL01.xml"), ActXmlFormatTests.Sample);
        return res;
    }

    [Fact]
    public void Library_Scan_NaturalOrder_ExtensionPriority_VoiceFolders()
    {
        using var t = new TempFolder();
        string res = MakeResources(t.Path);
        Assert.Equal(new[] { "BGM_2", "BGM_10" }, AudioLibrary.Scan(res, AudioKind.BGM).Select(f => f.Key));
        var se = AudioLibrary.Scan(res, AudioKind.SE);
        Assert.Equal(new[] { "se_a", "se_b" }, se.Select(f => f.Key));
        Assert.Equal("Audio/SE/se_a.mp3", se[0].RelativePath);
        Assert.Equal(new[] { "IL", "OT" }, AudioLibrary.VoiceFolders(res));
        var voice = Assert.Single(AudioLibrary.Scan(res, AudioKind.Voice, "OT"));
        Assert.Equal("OT/OT001", voice.MemoKey);
        Assert.Equal(1.0, voice.Duration);
        Assert.Empty(AudioLibrary.Scan(null, AudioKind.SE));
        Assert.Empty(AudioLibrary.Scan(res, AudioKind.Voice, null));
        Assert.Empty(AudioLibrary.VoiceFolders(Path.Combine(t.Path, "없음")));
        Assert.True(NaturalComparer.Instance.Compare("s001002", "s001010") < 0);
    }

    // ------------------------------------------------------------------ 추가(8회차): F5 줄 재생 계획

    [Fact]
    public void LinePlayback_Text_VoicesFromGroupThenRoot_Missing()
    {
        using var t = new TempFolder();
        string res = MakeResources(t.Path);
        var line = SCTheaterEditor.Script.ScriptLine.Create(("Type", "TEXT"), ("CG", "cg1"), ("Voice", "s001001 OT/OT001, nope"));
        var plan = LinePlayback.Build(line, 3, "IL", res);
        Assert.Equal("cg1", plan.CgKey);
        Assert.Equal(new[] { "Audio/Voice/IL/s001001.wav", "Audio/Voice/OT/OT001.wav" }, plan.Sounds.Select(s => s.RelativePath));
        Assert.Equal(new[] { "line0", "line1" }, plan.Sounds.Select(s => s.Channel));
        Assert.Equal(2.5, plan.VoiceSeconds);
        Assert.Equal(2.5, plan.SoundLength);
        Assert.Equal("보이스 nope", Assert.Single(plan.Missing));

        var none = LinePlayback.Build(SCTheaterEditor.Script.ScriptLine.Create(("Type", "TEXT"), ("CG", "none")), 1, "IL", res);
        Assert.Null(none.CgKey);
        Assert.False(none.HasContent);
    }

    [Fact]
    public void LinePlayback_Se_Audio_VolumeDurationFolderStop()
    {
        using var t = new TempFolder();
        string res = MakeResources(t.Path);
        SCTheaterEditor.Script.ScriptLine L(params (string, string)[] a) => SCTheaterEditor.Script.ScriptLine.Create(a);

        var se = Assert.Single(LinePlayback.Build(L(("Type", "SE"), ("Track", "se_a"), ("Volume", "0.5"), ("Duration", "0.4")), 1, "IL", res).Sounds);
        Assert.Equal("Audio/SE/se_a.mp3", se.RelativePath);
        Assert.Equal((0.5, 0.4, 0.4), (se.Volume, se.StopAt, se.Length));   // 길이 1초 → 재생 시간 0.4초

        var bgm = Assert.Single(LinePlayback.Build(L(("Type", "AUDIO"), ("Value", "bgm"), ("Track", "BGM_2")), 1, "IL", res).Sounds);
        Assert.Equal("Audio/BGM/BGM_2.mp3", bgm.RelativePath);
        Assert.Equal((1.0, 0.0, 1.0), (bgm.Volume, bgm.StopAt, bgm.Length));
        var slot = Assert.Single(LinePlayback.Build(L(("Type", "AUDIO"), ("Value", "1"), ("Track", "se_b"), ("Effect", "loop")), 1, "IL", res).Sounds);
        Assert.Equal("Audio/SE/se_b.mp3", slot.RelativePath);   // 슬롯 → SE 폴더, 반복은 하지 않음
        Assert.Empty(LinePlayback.Build(L(("Type", "AUDIO"), ("Value", "bgm"), ("Track", "BGM_2"), ("Effect", "stop")), 1, "IL", res).Sounds);
        Assert.Empty(LinePlayback.Build(L(("Type", "BG"), ("Track", "se_a")), 1, "IL", res).Sounds);
    }

    [Fact]
    public void MemoStore_SetGetRemove_SaveLoad()
    {
        using var t = new TempFolder();
        string path = Path.Combine(t.Path, AudioMemoStore.FileName);
        var store = new AudioMemoStore(path);
        Assert.True(store.Set(AudioKind.BGM, "BGM_121", "  경쾌한 일상 "));
        Assert.False(store.Set(AudioKind.BGM, "bgm_121", "경쾌한 일상"));   // 같은 값(키 대소문자 무시) → 저장 안 함
        store.Set(AudioKind.Voice, "IL/s001001", "첫 대사");
        store.Set(AudioKind.SE, "se_a", "문");
        store.Set(AudioKind.SE, "se_a", "");                                // 빈 글자 = 지움

        var loaded = new AudioMemoStore(path);
        Assert.Null(loaded.LoadNotice);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("경쾌한 일상", loaded.Get(AudioKind.BGM, "BGM_121"));
        Assert.Equal("첫 대사", loaded.Get(AudioKind.Voice, "IL/s001001"));
        Assert.Equal("", loaded.Get(AudioKind.SE, "se_a"));
        Assert.Equal("", loaded.Get(AudioKind.Voice, "OT/s001001"));          // 다른 폴더는 다른 메모

        File.WriteAllText(path, "<깨진");
        var broken = new AudioMemoStore(path);
        Assert.NotNull(broken.LoadNotice);
        Assert.Equal(0, broken.Count);
    }

    // ------------------------------------------------------------------ 목록 ViewModel + 줄에 넣기

    private sealed class Env : IDisposable
    {
        public readonly TempFolder Temp = new();
        public readonly AppSettings Settings = new();
        public readonly List<StageCommand> Sent = new();
        public readonly StrongReferenceMessenger Messenger = new();
        public readonly FakeDialogs Dialogs = new();
        public readonly ActEditorViewModel Editor;
        public readonly AudioListViewModel Audio;
        public readonly string Memo;

        public Env()
        {
            Settings.Editor.GameResourcesFolder = MakeResources(Temp.Path);
            Settings.Editor.LastActPath = Path.Combine(Settings.Editor.ScenarioFolder!, "IL", "IL01.xml");
            var lib = new SpineLibrary(Path.Combine(Temp.Path, "SpineData"), new SpineManifest());
            lib.Rebuild();
            Editor = new ActEditorViewModel(Settings, lib, Dialogs, new ImmediateDispatcher(), () => { }, new AppLog(),
                                            Path.Combine(Temp.Path, "Backups"), messenger: Messenger);
            Memo = Path.Combine(Temp.Path, AudioMemoStore.FileName);
            Audio = new AudioListViewModel(Settings, new AudioMemoStore(Memo), Sent.Add, Messenger, new AppLog());
            Audio.AttachEditor(Editor);
            Editor.Initialize();   // IL01 열기 (줄: 1 AUDIO bgm · 2 BG · 3 TEXT · 4 TRANSITION)
        }

        public void SelectLine(int number) => Editor.SetSelection([Editor.Rows[number - 1]]);
        public AudioRow Row(string key) => Audio.Items.Single(r => r.Key == key);
        public void Dispose() => Temp.Dispose();
    }

    [Fact]
    public void List_KindsAndVoiceFolderFollowsAct()
    {
        using var env = new Env();
        Assert.Equal(new[] { "se_a", "se_b" }, env.Audio.Items.Select(r => r.Key));
        env.Audio.Kind = AudioKind.BGM;
        Assert.Equal(new[] { "BGM_2", "BGM_10" }, env.Audio.Items.Select(r => r.Key));
        env.Audio.Kind = AudioKind.Voice;
        Assert.Equal("IL", env.Audio.VoiceFolder);                         // 연 막의 그룹
        Assert.Equal("s001001", Assert.Single(env.Audio.Items).Key);
        Assert.Equal("2.5초", env.Audio.Items[0].LengthText);
        env.Audio.VoiceFolder = "OT";
        Assert.Equal("OT001", Assert.Single(env.Audio.Items).Key);
    }

    [Fact]
    public void Play_Toggle_DoubleClickRestart_EndedClears()
    {
        using var env = new Env();
        env.Audio.TogglePlayCommand.Execute(null);                         // 선택 없음 → 안내만
        Assert.Empty(env.Sent);
        env.Audio.SelectedItem = env.Row("se_a");
        env.Audio.TogglePlayCommand.Execute(null);
        var play = Assert.IsType<PlayAudio>(env.Sent.Last());
        Assert.Equal(AudioListViewModel.PreviewChannel, play.Channel);
        Assert.Equal("https://resources.scsm/Audio/SE/se_a.mp3", play.Url);
        Assert.True(env.Audio.IsPlaying && env.Row("se_a").IsPlaying);
        env.Audio.TogglePlayCommand.Execute(null);                         // 변경(8회차): 다시 = 일시정지 (항목 표시는 유지)
        Assert.Equal(new PauseAudio(AudioListViewModel.PreviewChannel, true), env.Sent.Last());
        Assert.True(env.Audio.IsPaused);
        Assert.False(env.Audio.IsPlaying);
        Assert.True(env.Row("se_a").IsPlaying);
        env.Audio.Seek(0.5);
        Assert.Equal(new SeekAudio(AudioListViewModel.PreviewChannel, 0.5), env.Sent.Last());
        env.Audio.TogglePlayCommand.Execute(null);                         // 이어 재생
        Assert.Equal(new PauseAudio(AudioListViewModel.PreviewChannel, false), env.Sent.Last());
        Assert.True(env.Audio.IsPlaying);
        env.Audio.SelectedItem = env.Row("se_b");
        env.Audio.TogglePlayCommand.Execute(null);                         // 다른 항목을 고르면 그 항목을 처음부터
        Assert.EndsWith("se_b.mp3", Assert.IsType<PlayAudio>(env.Sent.Last()).Url);
        Assert.False(env.Row("se_a").IsPlaying);
        env.Audio.Stop();
        Assert.IsType<StopAudio>(env.Sent.Last());
        Assert.False(env.Audio.IsPlaying || env.Row("se_b").IsPlaying);

        env.Audio.PlayFromStart(env.Row("se_b"));                          // 더블클릭
        Assert.Equal("se_b", env.Audio.SelectedItem?.Key);
        env.Audio.OnStageEvent(new AudioEnded("other"));                   // 다른 채널은 무시
        Assert.True(env.Audio.IsPlaying);
        env.Audio.OnStageEvent(new AudioEnded(AudioListViewModel.PreviewChannel));
        Assert.False(env.Audio.IsPlaying || env.Row("se_b").IsPlaying);
    }

    [Fact]
    public void Memo_EditCommitCancel_Saved()
    {
        using var env = new Env();
        var row = env.Row("se_a");
        Assert.Equal("(메모 추가)", row.MemoLabel);
        row.BeginEdit();
        row.EditText = "문 두드림";
        row.CancelEdit();
        Assert.False(row.HasMemo);
        row.BeginEdit();
        row.EditText = " 문 두드림 ";
        row.CommitEdit();
        Assert.Equal("(문 두드림)", row.MemoLabel);
        Assert.Equal("문 두드림", new AudioMemoStore(env.Memo).Get(AudioKind.SE, "se_a"));
        env.Audio.Kind = AudioKind.BGM;
        env.Audio.Kind = AudioKind.SE;                                     // 다시 읽어도 메모 유지
        Assert.Equal("문 두드림", env.Row("se_a").Memo);
    }

    [Fact]
    public void Insert_IntoMatchingField_ReplaceOrAppend_UndoOneStep()
    {
        using var env = new Env();
        // 보이스 → TEXT(3번 줄) Voice: 바꿔 넣기 / 이어 붙이기 (같은 폴더 = 키만, 다른 폴더 = 폴더/키)
        env.Audio.Kind = AudioKind.Voice;
        env.SelectLine(3);
        env.Audio.SelectedItem = env.Row("s001001");
        env.Audio.InsertSelected(append: false);
        Assert.Equal("s001001", env.Editor.Rows[2].Get("Voice"));
        env.Audio.VoiceFolder = "OT";
        env.Audio.SelectedItem = env.Row("OT001");
        env.Audio.InsertSelected(append: true);
        Assert.Equal("s001001 OT/OT001", env.Editor.Rows[2].Get("Voice"));
        env.Editor.UndoCommand.Execute(null);
        Assert.Equal("s001001", env.Editor.Rows[2].Get("Voice"));

        // BGM → AUDIO bgm 줄(1번) Track, 이어 붙이기는 보이스만
        env.Audio.Kind = AudioKind.BGM;
        env.SelectLine(1);
        env.Audio.SelectedItem = env.Row("BGM_10");
        env.Audio.InsertSelected(append: true);
        Assert.Equal("BGM_10", env.Editor.Rows[0].Get("Track"));
        Assert.Equal(4, env.Editor.Rows.Count);
    }

    // ------------------------------------------------------------------ 추가(9회차): TEXT에 보이스 일괄 추가

    private static SCTheaterEditor.Script.ScriptLine Text(string name, string voice = "") =>
        SCTheaterEditor.Script.ScriptLine.Create(("Type", "TEXT"), ("Name", name), ("Text", "대사"), ("Voice", voice));

    [Fact]
    public void VoiceBatch_Plan_OverwriteEmptyOnly_SkipsNarrationAndNonText_Short()
    {
        var lines = new List<(SCTheaterEditor.Script.ScriptLine, int)>
        {
            (Text("A"), 5), (SCTheaterEditor.Script.ScriptLine.Create(("Type", "SE"), ("Track", "x")), 2),
            (Text(""), 6), (Text("B", "old"), 7), (Text("C"), 8),
        };
        var over = SCTheaterEditor.Script.VoiceBatch.Plan(lines, ["v1", "v2"], SCTheaterEditor.Script.VoiceBatchMode.Overwrite);
        Assert.Equal(new[] { 5, 6, 7, 8 }, over.Select(i => i.Number));                 // TEXT만, 막 순서
        Assert.Equal(new string?[] { "v1", null, "v2", null }, over.Select(i => i.NewVoice));
        Assert.Equal(SCTheaterEditor.Script.VoiceBatch.NoteNoName, over[1].Note);       // 나레이션은 보이스를 쓰지 않음
        Assert.Equal(SCTheaterEditor.Script.VoiceBatch.NoteShort, over[3].Note);

        var empty = SCTheaterEditor.Script.VoiceBatch.Plan(lines, ["v1", "v2"], SCTheaterEditor.Script.VoiceBatchMode.EmptyOnly);
        Assert.Equal(new string?[] { "v1", null, null, "v2" }, empty.Select(i => i.NewVoice));
        Assert.Equal(SCTheaterEditor.Script.VoiceBatch.NoteHasVoice, empty[2].Note);
    }

    [Fact]
    public void VoiceBatch_WindowModeSwitch_ApplyOneUndo_CancelKeeps()
    {
        using var env = new Env();
        var doc = env.Editor.Document!;
        doc.InsertLines(3, [Text("A"), Text(""), Text("B", "old")]);   // 4 A · 5 나레이션 · 6 B(old)
        env.Editor.SetSelection([env.Editor.Rows[3], env.Editor.Rows[4], env.Editor.Rows[5]]);

        env.Editor.OpenVoiceBatch(["v1", "v2", "v3"], "IL");                    // 창에서 취소 → 그대로
        Assert.Equal("", env.Editor.Rows[3].Get("Voice"));
        Assert.Equal(2, env.Dialogs.LastVoiceBatch!.Items.Count(i => i.NewVoice != null));

        env.Dialogs.VoiceBatchAction = b => { b.IsEmptyOnly = true; return true; };   // 2번(빈 줄에만)으로 바꿔 적용
        env.Editor.OpenVoiceBatch(["v1", "v2", "v3"], "IL");
        Assert.Equal(("v1", "", "old"), (env.Editor.Rows[3].Get("Voice"), env.Editor.Rows[4].Get("Voice"), env.Editor.Rows[5].Get("Voice")));

        env.Dialogs.VoiceBatchAction = b => true;                                 // 1번(덮어쓰기)
        env.Editor.OpenVoiceBatch(["v7", "v8"], "IL");
        Assert.Equal(("v7", "", "v8"), (env.Editor.Rows[3].Get("Voice"), env.Editor.Rows[4].Get("Voice"), env.Editor.Rows[5].Get("Voice")));
        env.Editor.UndoCommand.Execute(null);                                     // 한 번에 되돌림
        Assert.Equal(("v1", "old"), (env.Editor.Rows[3].Get("Voice"), env.Editor.Rows[5].Get("Voice")));
    }

    // 추가: Delete 칸 비우기
    [Fact]
    public void ClearField_SelectedRowsSameField_OneUndo_OutsideSelectionOnlyThatRow()
    {
        using var env = new Env();
        ScriptRow R(int i) => env.Editor.Rows[i];
        env.Editor.Document!.InsertLines(3, [Text("A", "v1"), Text("B", "v2"), Text("C", "v3"),
            SCTheaterEditor.Script.ScriptLine.Create(("Type", "SE"), ("Track", "x"), ("Voice", "keep"))]);   // 4~6 TEXT · 7 SE(Voice는 기타)
        env.Editor.SetSelection([R(3), R(4), R(6)]);

        env.Editor.ClearField(R(3), "Voice");                    // 선택 안의 줄 → 선택한 줄 중 Voice 칸이 있는 줄 모두
        Assert.Equal(("", "", "v3", "keep"), (R(3).Get("Voice"), R(4).Get("Voice"), R(5).Get("Voice"), R(6).Get("Voice")));
        env.Editor.UndoCommand.Execute(null);                    // 한 번에 되돌림
        Assert.Equal(("v1", "v2"), (R(3).Get("Voice"), R(4).Get("Voice")));

        env.Editor.ClearField(R(5), "Voice");                    // 선택 밖의 줄 → 그 줄만
        Assert.Equal(("v1", "v2", ""), (R(3).Get("Voice"), R(4).Get("Voice"), R(5).Get("Voice")));
    }

    [Fact]
    public void ApplyButton_VoiceOpensBatchFromSelected_SeInsertsKey()
    {
        using var env = new Env();
        env.Dialogs.VoiceBatchAction = b => true;
        env.SelectLine(3);
        env.Audio.Kind = AudioKind.Voice;
        env.Audio.VoiceFolder = "OT";
        env.Audio.SelectedItem = env.Row("OT001");
        env.Audio.ApplyCommand.Execute(null);
        Assert.Contains("OT001부터 1개", env.Dialogs.LastVoiceBatch!.FolderText);
        Assert.Equal("OT/OT001", env.Editor.Rows[2].Get("Voice"));               // 다른 폴더 = 폴더/키

        env.Audio.Kind = AudioKind.SE;                                            // SE · BGM = 키 1개 넣기 (Ctrl+Enter와 같음)
        env.SelectLine(1);
        env.Audio.SelectedItem = env.Row("se_a");
        int before = env.Editor.Rows.Count;
        env.Audio.ApplyCommand.Execute(null);
        Assert.Equal(before + 1, env.Editor.Rows.Count);
        Assert.Equal(("SE", "se_a"), (env.Editor.Rows[1].Type, env.Editor.Rows[1].Get("Track")));
    }

    [Fact]
    public void Insert_NoMatchingField_AddsLineBelow()
    {
        using var env = new Env();
        // SE를 AUDIO bgm 줄(1번)에 → 넣을 칸 없음 → 2번에 SE 줄
        env.SelectLine(1);
        env.Audio.SelectedItem = env.Row("se_b");
        env.Audio.InsertSelected(append: false);
        Assert.Equal(5, env.Editor.Rows.Count);
        Assert.Equal("SE", env.Editor.Rows[1].Type);
        Assert.Equal("se_b", env.Editor.Rows[1].Get("Track"));
        Assert.Equal(2, env.Editor.Selection.Single().Number);             // 새 줄 선택

        // 보이스를 TRANSITION 줄(5번)에 → 6번에 Voice만 넣은 TEXT 줄
        env.Audio.Kind = AudioKind.Voice;
        env.SelectLine(5);
        env.Audio.SelectedItem = env.Row("s001001");
        env.Audio.InsertSelected(append: true);
        Assert.Equal("TEXT", env.Editor.Rows[5].Type);
        Assert.Equal("s001001", env.Editor.Rows[5].Get("Voice"));
        Assert.Equal("1", env.Editor.Rows[5].Get("SpeakerType"));

        // BGM을 TEXT 줄에 → 아래에 Value=bgm AUDIO 줄
        env.Audio.Kind = AudioKind.BGM;
        env.SelectLine(6);
        env.Audio.SelectedItem = env.Row("BGM_2");
        env.Audio.InsertSelected(append: false);
        Assert.Equal("AUDIO", env.Editor.Rows[6].Type);
        Assert.Equal("bgm", env.Editor.Rows[6].Get("Value"));
        Assert.Equal("BGM_2", env.Editor.Rows[6].Get("Track"));

        // SE를 AUDIO 슬롯 1 줄에 → Track (SE 폴더 슬롯)
        env.Editor.Document!.SetAttributes(env.Editor.Rows[6].Line, [("Value", "1")]);
        env.Audio.Kind = AudioKind.SE;
        env.SelectLine(7);
        env.Audio.SelectedItem = env.Row("se_a");
        env.Audio.InsertSelected(append: false);
        Assert.Equal("se_a", env.Editor.Rows[6].Get("Track"));
        Assert.Equal(7, env.Editor.Rows.Count);
    }

    /// <summary>추가(6회차 확인 후): 트랙 분류가 빈 AUDIO 줄 — BGM은 bgm으로 채움, SE는 '루프 SE?' 예 = 1·loop / 아니오 = SE 줄, bgm 줄에 SE는 묻지 않고 SE 줄</summary>
    [Fact]
    public void Insert_EmptyAudioLine_BgmFills_SeAsksLoop()
    {
        using var env = new Env();
        var doc = env.Editor.Document!;
        doc.InsertLines(1, [Script.ScriptLine.Create(("Type", "AUDIO"))]);   // 2번 = 트랙 분류가 빈 AUDIO 줄

        // BGM → 묻지 않고 bgm + 파일 키
        env.Audio.Kind = AudioKind.BGM;
        env.SelectLine(2);
        env.Audio.SelectedItem = env.Row("BGM_2");
        env.Audio.InsertSelected(append: false);
        Assert.Equal(("bgm", "BGM_2"), (env.Editor.Rows[1].Get("Value"), env.Editor.Rows[1].Get("Track")));
        Assert.Equal(0, env.Dialogs.YesNoCount);
        env.Editor.UndoCommand.Execute(null);                                // 한 단계로 되돌아감
        Assert.Equal("", env.Editor.Rows[1].Get("Value"));

        // SE + 아니오 → 아래에 SE 줄
        env.Audio.Kind = AudioKind.SE;
        env.SelectLine(2);
        env.Audio.SelectedItem = env.Row("se_a");
        env.Dialogs.YesNoResult = false;
        env.Audio.InsertSelected(append: false);
        Assert.Equal(1, env.Dialogs.YesNoCount);
        Assert.Equal("", env.Editor.Rows[1].Get("Track"));
        Assert.Equal(("SE", "se_a"), (env.Editor.Rows[2].Type, env.Editor.Rows[2].Get("Track")));

        // SE + 예 → 트랙 1 · loop
        env.SelectLine(2);
        env.Dialogs.YesNoResult = true;
        env.Audio.InsertSelected(append: false);
        Assert.Equal(("1", "se_a", "loop"), (env.Editor.Rows[1].Get("Value"), env.Editor.Rows[1].Get("Track"), env.Editor.Rows[1].Get("Effect")));

        // bgm 줄(1번)에 SE → 묻지 않고 SE 줄
        int asked = env.Dialogs.YesNoCount;
        env.SelectLine(1);
        env.Audio.InsertSelected(append: false);
        Assert.Equal(asked, env.Dialogs.YesNoCount);
        Assert.Equal("SE", env.Editor.Rows[1].Type);
    }

    [Fact]
    public void Insert_WithoutSelectionOrAct()
    {
        using var env = new Env();
        env.Audio.InsertSelected(append: false);                           // 오디오 선택 없음
        Assert.Equal(4, env.Editor.Rows.Count);
        env.Editor.SetSelection([]);
        env.Audio.SelectedItem = env.Row("se_a");
        env.Audio.InsertSelected(append: false);                           // 줄 선택 없음 → 맨 끝
        Assert.Equal("SE", env.Editor.Rows[^1].Type);
    }
}
