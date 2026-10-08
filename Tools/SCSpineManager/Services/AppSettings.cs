using System.IO;
using System.Text.Json;

namespace SCSpineManager.Services;

/// <summary>
/// 앱 설정. %AppData%\SCSpineManager\settings.json 에 저장된다. (RG의 AppSettings와 같은 방식)
/// </summary>
public class AppSettings
{
    public const string DefaultSaveFolder = @"D:\usosctheater\resource\SpineData";

    /// <summary>Spine 저장 루트. 아래에 _meta(목록 스냅샷·매니페스트)와 {DressType}/{DressName}/ 폴더가 생긴다</summary>
    public string SaveFolder { get; set; } = DefaultSaveFolder;

    /// <summary>목록 API 주소 (idollist, dresslist?idolId=)</summary>
    public string ApiBase { get; set; } = "https://api.shinycolors.moe/spine/";

    /// <summary>파일 서버 주소 ({path}data.json · data.atlas · data.png)</summary>
    public string FileBase { get; set; } = "https://cf-static.shinycolors.moe/";

    /// <summary>요청 사이 간격(ms). 개인 운영 서버라 기본 1초, 동시 요청 없음</summary>
    public int RequestIntervalMs { get; set; } = 1000;

    /// <summary>추가: 다운로드 탭에서 체크한 아이돌 (idolId). null이면 전부 체크</summary>
    public List<int>? CheckedIdolIds { get; set; }

    /// <summary>추가: 리소스 탭에서 체크한 아이돌 (idolId). null이면 전부 체크</summary>
    public List<int>? ResourceCheckedIdolIds { get; set; }

    // ---------------------------------------------------------------------------------

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCSpineManager", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static AppSettings? _shared;
    public static AppSettings Shared => _shared ??= Load();

    /// <summary>저장 루트 아래 메타 폴더 (목록 스냅샷·매니페스트)</summary>
    public string MetaFolder => Path.Combine(SaveFolder, "_meta");

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
}
