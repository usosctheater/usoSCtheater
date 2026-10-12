using SCTheaterEditor.Services;

namespace SCTheaterEditor.Tests;

/// <summary>테스트마다 빈 임시 폴더 (끝나면 삭제)</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "te-tests-" + Guid.NewGuid().ToString("N"));
    public TempFolder() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public class SafeFileTests
{
    [Fact]
    public void Write_ReplacesAndKeepsBackup()
    {
        using var t = new TempFolder();
        string p = t.File("a.json");
        SafeFile.WriteAllText(p, "one", keepBackup: true);
        SafeFile.WriteAllText(p, "two", keepBackup: true);

        Assert.Equal("two", File.ReadAllText(p));
        Assert.Equal("one", File.ReadAllText(p + SafeFile.BackupSuffix));
        Assert.False(File.Exists(p + SafeFile.TempSuffix));
    }

    [Fact]
    public void ReadWithBackup_RecoversFromBackup()
    {
        using var t = new TempFolder();
        string p = t.File("a.json");
        File.WriteAllText(p, "broken");
        File.WriteAllText(p + SafeFile.BackupSuffix, "42");

        var (value, fromBackup) = SafeFile.ReadWithBackup(p, int.Parse);
        Assert.Equal(42, value);
        Assert.True(fromBackup);
    }

    [Fact]
    public void ReadWithBackup_BothBroken_MovesAsideAndThrows()
    {
        using var t = new TempFolder();
        string p = t.File("a.json");
        File.WriteAllText(p, "broken");

        Assert.Throws<FormatException>(() => SafeFile.ReadWithBackup(p, int.Parse));
        Assert.False(File.Exists(p));
        Assert.Single(Directory.GetFiles(t.Path, "a.json.corrupt-*"));
    }
}

public class AppSettingsTests
{
    [Fact]
    public void LegacySettings_AreMigratedIntoSections()
    {
        using var t = new TempFolder();
        string legacy = t.File("legacy.json");
        File.WriteAllText(legacy, """
            { "SaveFolder": "D:\\usosctheater\\Tools\\SCSpineManager\\SpineData", "RequestIntervalMs": 1500,
              "CheckedIdolIds": [1, 2], "ResourceCheckedIdolIds": [3],
              "ViewerSingleTrackMode": true, "ViewerLipEnabled": false, "ViewerLipAlways": true, "ViewerLipSeconds": 4.5 }
            """);

        var s = AppSettings.Load(t.File("new.json"), legacy);

        Assert.Equal("", s.SpineDataFolderSetting);            // 옛 exe 옆 경로 → 새 기본값
        Assert.Equal(1500, s.Download.RequestIntervalMs);
        Assert.Equal(new[] { 1, 2 }, s.Download.CheckedIdolIds);
        Assert.Equal(new[] { 3 }, s.Resource.CheckedIdolIds);
        Assert.True(s.Viewer.SingleTrackMode);
        Assert.False(s.Viewer.LipEnabled);
        Assert.True(s.Viewer.LipAlways);
        Assert.Equal(4.5, s.Viewer.LipSeconds);
        Assert.NotNull(s.LoadNotice);
        Assert.Equal("https://api.shinycolors.moe/spine/", s.Download.ApiBase);   // 없던 값은 기본값
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        using var t = new TempFolder();
        string p = t.File("settings.json");
        var s = new AppSettings();
        s.Viewer.LipSeconds = 7;
        s.Download.CheckedIdolIds = new() { 5 };
        s.Save(p);

        var loaded = AppSettings.Load(p, null);
        Assert.Equal(7, loaded.Viewer.LipSeconds);
        Assert.Equal(new[] { 5 }, loaded.Download.CheckedIdolIds);
        Assert.Null(loaded.LoadNotice);
    }

    [Fact]
    public void CorruptSettings_StartWithDefaults()
    {
        using var t = new TempFolder();
        string p = t.File("settings.json");
        File.WriteAllText(p, "{ not json");

        var s = AppSettings.Load(p, null);
        Assert.NotNull(s.LoadNotice);
        Assert.True(s.Viewer.LipEnabled);
        Assert.Single(Directory.GetFiles(t.Path, "settings.json.corrupt-*"));
    }
}

public class AppLogTests
{
    [Fact]
    public void Rotates_AndKeepsLimitedFiles()
    {
        using var t = new TempFolder();
        var log = new AppLog { MaxFileBytes = 200, KeepFiles = 2 };
        log.SetFileFolder(t.Path);
        var seen = new List<LogEntry>();
        log.Written += seen.Add;

        for (int i = 0; i < 40; i++)
        {
            log.Info(new string('x', 50) + i);
            if (i % 8 == 7) Thread.Sleep(1100);   // 넘긴 파일 이름(초 단위 시각)이 겹치지 않도록
        }

        Assert.Equal(40, seen.Count);
        Assert.True(File.Exists(t.File(AppLog.FileName)));
        Assert.InRange(Directory.GetFiles(t.Path, "log_*.txt").Length, 1, 2);
    }

    [Fact]
    public void Error_WritesExceptionToFile()
    {
        using var t = new TempFolder();
        var log = new AppLog();
        log.SetFileFolder(t.Path);
        log.Error("실패", new InvalidOperationException("원인"));

        string text = File.ReadAllText(t.File(AppLog.FileName));
        Assert.Contains("[ERROR] 실패: 원인", text);
        Assert.Contains("InvalidOperationException", text);
    }
}

public class HousekeepingTests
{
    [Fact]
    public void RemovesOnlyFoldersOlderThanExe()
    {
        using var t = new TempFolder();
        string oldDir = Directory.CreateDirectory(t.File("old")).FullName;
        string curDir = Directory.CreateDirectory(t.File("current")).FullName;
        File.WriteAllText(System.IO.Path.Combine(oldDir, "a.dll"), "x");
        var built = DateTime.UtcNow;
        Directory.SetCreationTimeUtc(oldDir, built.AddDays(-1));
        Directory.SetCreationTimeUtc(curDir, built.AddMinutes(1));

        var (removed, skipped) = Housekeeping.CleanExtractionFolders(t.Path, built);

        Assert.Equal((1, 0), (removed, skipped));
        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(curDir));
    }
}
