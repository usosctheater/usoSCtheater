using System.IO;
using System.Text.RegularExpressions;

namespace SCTheaterEditor.Script;

/// <summary>막 목록의 한 항목: 시나리오 폴더의 그룹 폴더 안 막 XML</summary>
public sealed record ActEntry(string Group, string Name, string Path)
{
    public string Display => $"{Group} / {Name}";
}

/// <summary>
/// 막 파일 찾기·백업.
/// - 목록: 지정 폴더(게임 Assets\Resources\Scenario) 바로 아래 그룹 폴더 → 그 안의 *.xml (하위 폴더는 보지 않음 — 게임도 시나리오 폴더 안 하위 폴더 금지)
/// - 백업: 저장 직전 파일을 Backups\{그룹}\{막}_{시각}.xml로 복사, 막마다 최근 N개만 남김
/// </summary>
public static class ActFiles
{
    /// <summary>기본 백업 폴더 = exe 옆 Backups (git에서는 /Tools/**/Backups/ 제외)</summary>
    public static string DefaultBackupFolder => Path.Combine(AppContext.BaseDirectory, "Backups");

    public static List<ActEntry> Scan(string folder)
    {
        var list = new List<ActEntry>();
        if (!Directory.Exists(folder)) return list;
        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            string group = Path.GetFileName(dir);
            foreach (var file in Directory.EnumerateFiles(dir))
                if (Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                    list.Add(new ActEntry(group, Path.GetFileNameWithoutExtension(file), Path.GetFullPath(file)));
        }
        list.Sort((a, b) =>
        {
            int c = string.Compare(a.Group, b.Group, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    /// <summary>파일을 백업 폴더로 복사하고 오래된 백업 정리. 복사한 경로 반환</summary>
    public static string Backup(string file, string backupRoot, int keep, DateTime now)
    {
        string full = Path.GetFullPath(file);
        string group = Path.GetFileName(Path.GetDirectoryName(full)) ?? "";
        if (group.Length == 0) group = "_";
        string name = Path.GetFileNameWithoutExtension(full);
        string dir = Path.Combine(backupRoot, group);
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, $"{name}_{now:yyyyMMdd-HHmmss-fff}.xml");
        File.Copy(full, target, overwrite: true);
        Prune(dir, name, keep);
        return target;
    }

    /// <summary>{막}_{시각}.xml 중 최근 keep개(최소 1)만 남김</summary>
    public static void Prune(string dir, string name, int keep)
    {
        var re = new Regex("^" + Regex.Escape(name) + @"_\d{8}-\d{6}-\d{3}\.xml$", RegexOptions.IgnoreCase);
        var files = Directory.EnumerateFiles(dir)
                             .Where(f => re.IsMatch(Path.GetFileName(f)))
                             .OrderByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                             .ToList();
        foreach (var old in files.Skip(Math.Max(1, keep))) File.Delete(old);
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
