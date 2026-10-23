using System.Collections.Concurrent;
using System.IO;

namespace SCTheaterEditor.Services;

/// <summary>추가(6회차): 오디오 목록 구분 (게임 Resources\Audio\SE · BGM · Voice)</summary>
public enum AudioKind { SE, BGM, Voice }

/// <summary>오디오 파일 1개. Key = 파일 이름(확장자 제외, 게임이 Resources.Load로 찾는 키), Folder = Voice 하위 폴더(그 밖은 "")</summary>
public sealed record AudioFile(AudioKind Kind, string Folder, string Key, string RelativePath, double? Duration)
{
    /// <summary>메모 키 — 보이스는 폴더 간 파일 이름이 겹칠 수 있어 "IL/s001001"</summary>
    public string MemoKey => Kind == AudioKind.Voice ? $"{Folder}/{Key}" : Key;
}

/// <summary>
/// 추가(6회차): 게임 Resources\Audio 훑기 (게임 AudioManager와 같은 위치 규칙: Audio/SE · Audio/BGM · Audio/Voice/{시나리오}).
/// 읽기만 하고 폴더를 만들지 않음. 길이는 파일 크기·수정 시각이 같으면 다시 읽지 않음
/// </summary>
public static class AudioLibrary
{
    /// <summary>같은 키가 여러 확장자로 있으면 앞쪽 (Unity는 키만으로 찾으므로 실제로는 하나만 두어야 함)</summary>
    public static readonly IReadOnlyList<string> Extensions = [".mp3", ".wav", ".ogg"];

    private static readonly ConcurrentDictionary<string, (long Length, DateTime Modified, double? Duration)> DurationCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static string SubFolder(AudioKind kind) => kind switch
    {
        AudioKind.SE => @"Audio\SE",
        AudioKind.BGM => @"Audio\BGM",
        _ => @"Audio\Voice",
    };

    /// <summary>Voice 하위 폴더 이름 (자연 정렬, 없으면 빈 목록)</summary>
    public static IReadOnlyList<string> VoiceFolders(string? resources)
    {
        if (string.IsNullOrWhiteSpace(resources)) return [];
        string dir = Path.Combine(resources, SubFolder(AudioKind.Voice));
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateDirectories(dir).Select(Path.GetFileName).OfType<string>()
                        .Order(NaturalComparer.Instance).ToList();
    }

    /// <summary>그 구분(보이스는 그 하위 폴더)의 파일 목록 — 자연 정렬 (BGM_2가 BGM_10보다 앞)</summary>
    public static IReadOnlyList<AudioFile> Scan(string? resources, AudioKind kind, string? voiceFolder = null)
    {
        if (string.IsNullOrWhiteSpace(resources)) return [];
        if (kind == AudioKind.Voice && string.IsNullOrWhiteSpace(voiceFolder)) return [];
        string folder = kind == AudioKind.Voice ? voiceFolder! : "";
        string dir = Path.Combine(resources, SubFolder(kind), folder);
        if (!Directory.Exists(dir)) return [];

        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(dir)
                     .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                     .OrderBy(f => Extensions.ToList().FindIndex(e => e.Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase))))
            byKey.TryAdd(Path.GetFileNameWithoutExtension(path), path);

        return byKey.OrderBy(p => p.Key, NaturalComparer.Instance)
                    .Select(p => new AudioFile(kind, folder, p.Key, Path.GetRelativePath(resources, p.Value).Replace('\\', '/'), DurationOf(p.Value)))
                    .ToList();
    }

    /// <summary>
    /// 추가(8회차): 키 → Resources 기준 상대 경로 (없으면 null). 게임 AudioManager와 같은 규칙:
    /// 보이스 = Voice/{시나리오}/{키} → Voice/{키} ("OT/OT001"처럼 폴더를 붙인 키는 루트 쪽에서 찾힘), SE·BGM = 그 폴더/{키}
    /// </summary>
    public static string? Resolve(string? resources, AudioKind kind, string key, string? voiceGroup = null)
    {
        key = key.Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(resources) || key.Length == 0) return null;
        var dirs = new List<string>();
        if (kind == AudioKind.Voice && !string.IsNullOrWhiteSpace(voiceGroup)) dirs.Add(Path.Combine(SubFolder(kind), voiceGroup));
        dirs.Add(SubFolder(kind));
        foreach (var dir in dirs)
            foreach (var ext in Extensions)
            {
                string full = Path.Combine(resources, dir, key + ext);
                if (File.Exists(full)) return Path.GetRelativePath(resources, full).Replace('\\', '/');
            }
        return null;
    }

    /// <summary>추가(8회차): 상대 경로 파일의 길이(초, 캐시)</summary>
    public static double? DurationOf(string resources, string relativePath) => DurationOf(Path.Combine(resources, relativePath));

    private static double? DurationOf(string path)
    {
        var info = new FileInfo(path);
        if (DurationCache.TryGetValue(path, out var c) && c.Length == info.Length && c.Modified == info.LastWriteTimeUtc) return c.Duration;
        double? d = AudioDuration.Read(path);
        DurationCache[path] = (info.Length, info.LastWriteTimeUtc, d);
        return d;
    }

    /// <summary>길이 표시: 1분 미만 "2.6초", 그 이상 "10:30", 모르면 "-"</summary>
    public static string FormatDuration(double? seconds) => seconds switch
    {
        null => "-",
        < 60 => $"{seconds.Value:0.0}초",
        _ => $"{(int)(seconds.Value / 60)}:{(int)(seconds.Value % 60):00}",
    };
}

/// <summary>추가(6회차): 숫자 부분은 숫자로 비교 (BGM_2 &lt; BGM_10), 나머지는 대소문자 무시</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                string a = x[si..i].TrimStart('0'), b = y[sj..j].TrimStart('0');
                int c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
        }
        int rest = (x.Length - i).CompareTo(y.Length - j);
        return rest != 0 ? rest : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
