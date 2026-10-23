using System.Globalization;
using SCTheaterEditor.Script;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.Stage;

/// <summary>추가(8회차): F5로 맞춘 줄에서 재생할 소리 1개 (Length = 바 길이에 쓰는 초, 재생 시간이 있으면 그 초)</summary>
public sealed record LineSound(string Channel, string RelativePath, double Volume, double Length, double StopAt);

/// <summary>추가(8회차): F5 줄 재생 계획 — 그 줄의 Spine(CG 키)과 소리. VoiceSeconds = 보이스 중 가장 긴 길이 (립 길이)</summary>
public sealed record LinePlan(int LineNumber, string Type, string? CgKey, IReadOnlyList<LineSound> Sounds, double VoiceSeconds, IReadOnlyList<string> Missing)
{
    /// <summary>소리 중 가장 긴 길이 (없으면 0)</summary>
    public double SoundLength => Sounds.Count == 0 ? 0 : Sounds.Max(s => s.Length);

    /// <summary>재생 바에 묶을 것이 있는지 (없으면 무대 전체가 재생 대상)</summary>
    public bool HasContent => CgKey != null || Sounds.Count > 0;
}

/// <summary>
/// 추가(8회차): 줄 하나의 정보만으로 재생 계획 (직전·직후 줄은 보지 않음 — 순차 재생은 별도 기능).
/// - TEXT: CG(그 줄의 Spine) + Voice(공백·쉼표로 여러 개 = 동시 재생, 채널 line0, line1 …)
/// - SETCG: CG
/// - SE: Track (Volume · Duration = 재생 시간)
/// - AUDIO: Track (Value가 bgm이면 BGM 폴더, 그 밖은 SE 폴더, Volume). Effect=stop이면 없음. 반복(loop)은 하지 않고 한 번
/// - 그 밖: 없음
/// </summary>
public static class LinePlayback
{
    public const string ChannelPrefix = "line";

    public static LinePlan Build(ScriptLine line, int lineNumber, string? voiceGroup, string? resources)
    {
        string type = line.Get("Type").Trim().ToUpperInvariant();
        var sounds = new List<LineSound>();
        var missing = new List<string>();
        double voiceSeconds = 0;
        string? cg = null;

        void Add(AudioKind kind, string key, double volume, double stopAt, string label)
        {
            if (AudioLibrary.Resolve(resources, kind, key, voiceGroup) is not { } rel)
            {
                missing.Add($"{label} {key}");
                return;
            }
            double length = AudioLibrary.DurationOf(resources!, rel) ?? 0;
            if (stopAt > 0) length = length > 0 ? Math.Min(length, stopAt) : stopAt;
            sounds.Add(new LineSound(ChannelPrefix + sounds.Count, rel, volume, length, stopAt));
            if (kind == AudioKind.Voice) voiceSeconds = Math.Max(voiceSeconds, length);
        }

        switch (type)
        {
            case "TEXT":
                cg = CgOf(line);
                foreach (var key in line.Get("Voice").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
                    Add(AudioKind.Voice, key, 1, 0, "보이스");
                break;
            case "SETCG":
                cg = CgOf(line);
                break;
            case "SE":
                if (line.Get("Track").Trim() is { Length: > 0 } se) Add(AudioKind.SE, se, Volume(line), Number(line.Get("Duration")), "SE");
                break;
            case "AUDIO":
                if (line.Get("Effect").Trim().Equals("stop", StringComparison.OrdinalIgnoreCase)) break;
                if (line.Get("Track").Trim() is { Length: > 0 } track)
                {
                    bool bgm = line.Get("Value").Trim().Equals("bgm", StringComparison.OrdinalIgnoreCase);
                    Add(bgm ? AudioKind.BGM : AudioKind.SE, track, Volume(line), 0, bgm ? "BGM" : "SE");
                }
                break;
        }
        return new LinePlan(lineNumber, type, cg, sounds, voiceSeconds, missing);
    }

    /// <summary>CG 키 (비었거나 none이면 null)</summary>
    private static string? CgOf(ScriptLine line)
    {
        string v = line.Get("CG").Trim();
        return v.Length == 0 || v.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : v;
    }

    /// <summary>Volume (없거나 잘못되면 1, 0~1)</summary>
    private static double Volume(ScriptLine line) =>
        double.TryParse(line.Get("Volume").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? Math.Clamp(v, 0, 1) : 1;

    private static double Number(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : 0;
}
