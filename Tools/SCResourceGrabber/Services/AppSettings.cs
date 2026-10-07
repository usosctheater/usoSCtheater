using System.IO;
using System.Text.Json;

namespace SCResourceGrabber.Services;

/// <summary>
/// 앱 설정. %AppData%\SCResourceGrabber\settings.json 에 저장된다.
/// </summary>
public class AppSettings
{
    public string StartUrl { get; set; } = "https://shinycolors.enza.fun/";

    /// <summary>WebView2 사용자 데이터(로그인 쿠키 등) 폴더. 이 폴더가 유지되는 한 로그인도 유지된다.</summary>
    public string ProfileFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCResourceGrabber", "Profile");

    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SCResourceGrabber");

    /// <summary>true면 저장 시 URL 경로 구조(호스트/경로/파일)를 유지, false면 종류별 폴더에 평평하게 저장.</summary>
    public bool KeepUrlPath { get; set; } = true;

    public bool OverwriteExisting { get; set; } = false;

    /// <summary>캡처할 호스트(부분 일치, ; 구분). 비우면 전부 캡처.</summary>
    public string HostFilter { get; set; } = "";

    // ---------------------------------------------------------------------------------

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCResourceGrabber", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { /* 손상된 설정은 기본값으로 */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* 설정 저장 실패는 무시 */ }
    }

    public bool MatchesHost(string host)
    {
        var filters = HostFilter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return filters.Length == 0 || filters.Any(f => host.Contains(f, StringComparison.OrdinalIgnoreCase));
    }
}
