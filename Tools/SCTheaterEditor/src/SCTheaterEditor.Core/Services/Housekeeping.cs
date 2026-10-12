using System.IO;

namespace SCTheaterEditor.Services;

/// <summary>
/// 오래 쓰면서 쌓이는 것 정리 (시작할 때 백그라운드에서 1회).
/// - 단일 exe 압축 해제 폴더: publish한 exe는 실행할 때 네이티브 dll(SkiaSharp, WebView2Loader)을
///   %TEMP%\.net\{앱 이름}\{빌드 해시}\에 풀어 두는데, 빌드할 때마다 새 폴더가 생기고 옛 폴더는 지워지지 않는다.
///   지금 exe보다 먼저 만들어진 폴더 = 이전 빌드의 것이므로 지운다 (사용 중이라 못 지우면 건너뜀).
/// </summary>
public static class Housekeeping
{
    /// <summary>압축 해제 기준 폴더 (.NET 기본값 %TEMP%\.net, 환경 변수로 바꿀 수 있음)</summary>
    public static string ExtractionBaseFolder
    {
        get
        {
            string? custom = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR");
            return string.IsNullOrEmpty(custom) ? Path.Combine(Path.GetTempPath(), ".net") : custom;
        }
    }

    /// <summary>지금 실행 중인 exe 기준으로 이전 빌드의 압축 해제 폴더 정리</summary>
    public static void CleanExtractionFolders(string appName, AppLog log)
    {
        string? exe = Environment.ProcessPath;
        if (exe == null || !File.Exists(exe)) return;
        try
        {
            var (removed, skipped) = CleanExtractionFolders(Path.Combine(ExtractionBaseFolder, appName), File.GetLastWriteTimeUtc(exe));
            if (removed > 0 || skipped > 0)
                log.Info($"이전 빌드의 임시 폴더 정리: {removed}개 삭제" + (skipped > 0 ? $", {skipped}개는 사용 중이라 건너뜀" : ""));
        }
        catch (Exception ex)
        {
            log.Warn($"이전 빌드의 임시 폴더 정리 실패 (다음 실행 때 다시 시도): {ex.Message}");
        }
    }

    /// <summary>appDir 아래에서 builtUtc보다 먼저 만들어진 폴더를 지운다. (지운 수, 건너뛴 수)</summary>
    public static (int removed, int skipped) CleanExtractionFolders(string appDir, DateTime builtUtc)
    {
        if (!Directory.Exists(appDir)) return (0, 0);
        int removed = 0, skipped = 0;
        foreach (var dir in Directory.GetDirectories(appDir))
        {
            // 지금 exe를 처음 실행할 때 만들어진 폴더(= 지금 쓰는 폴더)는 exe보다 나중에 생김
            if (Directory.GetCreationTimeUtc(dir) >= builtUtc) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;   // 다른 곳에서 쓰는 중 — 다음 실행 때 다시 시도
            }
        }
        return (removed, skipped);
    }
}
