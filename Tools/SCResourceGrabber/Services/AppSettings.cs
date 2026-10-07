using System.IO;
using System.Text.Json;

namespace SCResourceGrabber.Services;

/// <summary>
/// 앱 설정. %AppData%\SCResourceGrabber\settings.json 에 저장된다.
/// </summary>
public class AppSettings
{
    public string StartUrl { get; set; } = "https://shinycolors.enza.fun/";

    /// <summary>Spine 수집 창 시작 주소 / 저장 폴더 (URL 경로 구조를 유지해서 저장)</summary>
    public string SpineStartUrl { get; set; } = "https://spine.shinycolors.moe/";
    public string SpineSaveFolder { get; set; } = @"D:\usosctheater\resource\Grabber\Spine";

    /// <summary>WebView2 사용자 데이터(로그인 쿠키 등) 폴더. 이 폴더가 유지되는 한 로그인도 유지된다.</summary>
    public string ProfileFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCResourceGrabber", "Profile");

    public const string DefaultSaveFolder = @"D:\usosctheater\resource\Grabber";

    public string SaveFolder { get; set; } = DefaultSaveFolder;


    public bool OverwriteExisting { get; set; } = false;

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
    /// <summary>모든 창이 같은 설정 객체를 쓴다 (게임 창·Spine 수집 창)</summary>
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
