using System.IO;
using System.Text;

namespace SCTheaterEditor.Services;

/// <summary>
/// 안전한 파일 저장: 임시 파일(.tmp)에 끝까지 쓴 뒤 한 번에 교체한다.
/// 쓰는 도중 프로그램이 꺼지거나 실패해도 원래 파일은 그대로 남는다.
/// keepBackup이면 교체 직전 파일을 .bak으로 남긴다 (설정·매니페스트처럼 손상되면 곤란한 기록용).
/// </summary>
public static class SafeFile
{
    public const string TempSuffix = ".tmp";
    public const string BackupSuffix = ".bak";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public static void WriteAllText(string path, string text, bool keepBackup = false) =>
        WriteAllBytes(path, Utf8NoBom.GetBytes(text), keepBackup);

    public static void WriteAllBytes(string path, byte[] bytes, bool keepBackup = false)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string tmp = full + TempSuffix;

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);   // 디스크까지 기록한 뒤 교체 (전원 차단 대비)
        }

        if (keepBackup && File.Exists(full))
            File.Replace(tmp, full, full + BackupSuffix, ignoreMetadataErrors: true);
        else
            File.Move(tmp, full, overwrite: true);
    }

    /// <summary>
    /// 읽기 실패(손상)에 대비한 읽기: 원본을 읽지 못하면 .bak을 시도한다.
    /// 둘 다 실패하면 원본을 "{이름}.corrupt-{시각}"으로 옮겨 두고(덮어쓰기 방지) 예외를 다시 던진다.
    /// </summary>
    /// <returns>(읽은 값, 백업에서 복구했으면 true)</returns>
    public static (T value, bool fromBackup) ReadWithBackup<T>(string path, Func<string, T> parse)
    {
        try
        {
            return (parse(File.ReadAllText(path, Encoding.UTF8)), false);
        }
        catch (Exception first) when (first is not FileNotFoundException)
        {
            string bak = path + BackupSuffix;
            if (File.Exists(bak))
            {
                try { return (parse(File.ReadAllText(bak, Encoding.UTF8)), true); }
                catch { /* 백업도 손상 — 아래에서 원본을 옮기고 처음 예외를 알림 */ }
            }
            MoveAside(path);
            throw;
        }
    }

    /// <summary>손상된 파일을 지우지 않고 옆으로 옮김 (원인 확인용으로 남김)</summary>
    public static string? MoveAside(string path)
    {
        if (!File.Exists(path)) return null;
        string target = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Move(path, target, overwrite: true);
        return target;
    }
}
