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

    public const string DefaultSaveFolder = @"D:\usosctheater\resource\Grabber";

    public string SaveFolder { get; set; } = DefaultSaveFolder;


    public bool OverwriteExisting { get; set; } = false;

    /// <summary>게임 화면(WebView2) 음소거 (2026-10-28). 상세 창 미리 듣기·추적은 영향 없음</summary>
    public bool Muted { get; set; } = false;

    // ---------- 다운로드 창 (2026-10-28, 마지막 설정 기억) ----------
    /// <summary>다운로드 창 저장 폴더 (기본은 일반 저장 폴더와 같음, 창에서 따로 바꿀 수 있음)</summary>
    public string DownloadSaveFolder { get; set; } = DefaultSaveFolder;
    public bool DownloadOverwrite { get; set; } = false;
    public bool DownloadConvertPng { get; set; } = true;
    public bool DownloadTrim { get; set; } = false;
    public bool DownloadSquare { get; set; } = false;
    /// <summary>여백 (%, 긴 변 기준)</summary>
    public double DownloadPadding { get; set; } = 6;

    /// <summary>캡처할 호스트(부분 일치, ; 구분). 비우면 전부 캡처.</summary>
    public string HostFilter { get; set; } = "";

    /// <summary>단축키: 동작 이름 → 키 ("Ctrl+S" 형식, 빈 값이면 해제). 목록은 HotkeyMap.Defaults 참고.</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new(HotkeyMap.Defaults);

    // ---------------------------------------------------------------------------------

    private static readonly string OldDefaultSaveFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SCResourceGrabber");

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCResourceGrabber", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static AppSettings? _shared;
    /// <summary>모든 창이 같은 설정 객체를 쓴다</summary>
    public static AppSettings Shared => _shared ??= Load();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
                // 이전 기본값(내 문서\SCResourceGrabber)을 그대로 쓰고 있었다면 새 기본값으로 옮김
                if (string.Equals(s.SaveFolder, OldDefaultSaveFolder, StringComparison.OrdinalIgnoreCase))
                    s.SaveFolder = DefaultSaveFolder;
                // 새로 추가된 단축키 동작은 기본값으로 채움 (사용자가 바꾼 값은 유지)
                s.Hotkeys ??= new();
                foreach (var old in HotkeyMap.Removed) s.Hotkeys.Remove(old);
                foreach (var (action, key) in HotkeyMap.Defaults)
                    s.Hotkeys.TryAdd(action, key);
                return s;
            }
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
