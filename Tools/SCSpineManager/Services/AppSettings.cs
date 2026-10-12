using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SCSpineManager.Services;

/// <summary>
/// 앱 설정. %AppData%\SCSpineManager\settings.json 에 저장된다. (RG의 AppSettings와 같은 방식)
/// </summary>
public class AppSettings
{
    /// <summary>
    /// 변경: 기본 저장 루트 = 실행 파일(exe) 옆 SpineData 폴더 (도구 독립 실행 규칙 — 프로젝트 경로에 의존하지 않음).
    /// 용량이 커서 git에서는 /Tools/**/SpineData/ 로 제외
    /// </summary>
    public static string DefaultSaveFolder => Path.Combine(AppContext.BaseDirectory, "SpineData");

    /// <summary>예전 기본값 — 이 값이 저장돼 있으면 새 기본값(exe 옆)을 쓰도록 비움 (2026-10-12 데이터 이동)</summary>
    private const string OldDefaultSaveFolder = @"D:\usosctheater\resource\SpineData";

    /// <summary>
    /// 변경: 설정 파일에 저장되는 값. 비우면 기본 저장 루트, 상대 경로면 exe 폴더 기준
    /// (설정 파일의 키 이름은 예전과 같은 "SaveFolder")
    /// </summary>
    [JsonPropertyName("SaveFolder")]
    public string SaveFolderSetting { get; set; } = "";

    /// <summary>Spine 저장 루트(실제 경로). 아래에 _meta(목록 스냅샷·매니페스트)와 {DressType}/{DressName}/ 폴더가 생긴다</summary>
    [JsonIgnore]
    public string SaveFolder => string.IsNullOrWhiteSpace(SaveFolderSetting)
        ? DefaultSaveFolder
        : Path.GetFullPath(SaveFolderSetting, AppContext.BaseDirectory);

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

    /// <summary>추가: 뷰어 재생 방식 — true면 선택한 트랙만 교체(디버그 씬 방식), false면 게임 방식(전체 다시 재생)</summary>
    public bool ViewerSingleTrackMode { get; set; }

    /// <summary>추가: 뷰어 립 재생 (게임 방식에서만) — 켜기 / 계속 말함(true) 또는 N초 후 멈춤(false) / N초</summary>
    public bool ViewerLipEnabled { get; set; } = true;
    public bool ViewerLipAlways { get; set; }
    public double ViewerLipSeconds { get; set; } = 3.0;

    // 변경: ProjectDataFolder 삭제 — DefaultLipData.xml은 도구 안의 복사본(Resources\DefaultLipData.xml)을 exe에 포함해 사용

    // ---------------------------------------------------------------------------------

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SCSpineManager", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static AppSettings? _shared;
    public static AppSettings Shared => _shared ??= Load();

    /// <summary>저장 루트 아래 메타 폴더 (목록 스냅샷·매니페스트)</summary>
    [JsonIgnore]   // 추가: 계산 값이라 설정 파일에 쓰지 않음
    public string MetaFolder => Path.Combine(SaveFolder, "_meta");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
                // 추가: 예전 기본 경로가 저장돼 있으면 새 기본값(exe 옆 SpineData)으로
                if (string.Equals(s.SaveFolderSetting?.TrimEnd('\\', '/'), OldDefaultSaveFolder, StringComparison.OrdinalIgnoreCase))
                    s.SaveFolderSetting = "";
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
}
