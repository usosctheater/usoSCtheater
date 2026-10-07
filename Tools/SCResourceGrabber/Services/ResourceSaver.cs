using System.IO;
using System.Text.RegularExpressions;
using SCResourceGrabber.Models;

namespace SCResourceGrabber.Services;

/// <summary>
/// 캐시 파일을 저장 폴더로 복사한다. 경로 규칙은 설정(KeepUrlPath)에 따른다.
/// </summary>
public static class ResourceSaver
{
    public enum Outcome { Saved, Skipped, Failed }

    public static string BuildTargetPath(CapturedResource res, AppSettings settings)
    {
        var uri = new Uri(res.Url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(Uri.UnescapeDataString)
                                       .ToList();

        string name = segments.Count > 0 ? segments[^1] : "index";
        if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);

        // 확장자가 없거나 내용과 다르면 판정된 확장자를 붙인다
        if (!Regex.IsMatch(name, @"\.[A-Za-z0-9]{1,6}$"))
            name += "." + res.Extension;

        var parts = new List<string> { settings.SaveFolder };
        if (settings.KeepUrlPath)
        {
            parts.Add(Sanitize(uri.Host));
            parts.AddRange(segments.Select(Sanitize));
        }
        else
        {
            parts.Add(CapturedResource.CategoryLabels[res.Category]);
        }
        parts.Add(Sanitize(name));
        return Path.Combine(parts.ToArray());
    }

    public static (Outcome outcome, string path, string? error) Save(CapturedResource res, AppSettings settings)
    {
        string target = BuildTargetPath(res, settings);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && !settings.OverwriteExisting)
            {
                // 내용이 같으면 건너뛰고, 다르면 번호를 붙여 저장
                if (new FileInfo(target).Length == res.Size && FilesEqual(target, res.CachePath))
                    return (Outcome.Skipped, target, null);
                target = UniquePath(target);
            }
            File.Copy(res.CachePath, target, overwrite: true);
            return (Outcome.Saved, target, null);
        }
        catch (Exception ex)
        {
            return (Outcome.Failed, target, ex.Message);
        }
    }

    private static bool FilesEqual(string a, string b)
    {
        var x = File.ReadAllBytes(a);
        var y = File.ReadAllBytes(b);
        return x.AsSpan().SequenceEqual(y);
    }

    private static string UniquePath(string path)
    {
        string dir = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    public static string Sanitize(string s)
    {
        var chars = s.Select(c => Invalid.Contains(c) ? '_' : c).ToArray();
        string r = new string(chars).TrimEnd('.', ' ');
        if (r.Length > 120) r = r[..120];
        if (Regex.IsMatch(r, @"^(con|prn|aux|nul|com\d|lpt\d)(\..*)?$", RegexOptions.IgnoreCase)) r = "_" + r;
        return r.Length == 0 ? "_" : r;
    }
}
