using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SCTheaterEditor.Services;

/// <summary>
/// 앱 설정. %AppData%\SCTheaterEditor\settings.json 에 영역별로 저장된다.
/// - 처음 실행할 때 예전 SCSpineManager 설정(%AppData%\SCSpineManager\settings.json)이 있으면 읽어서 옮긴다 (예전 파일은 그대로 둠)
/// - 저장은 SafeFile(임시 파일 → 교체, .bak 보관). 읽기 실패 시 .bak → 그것도 실패하면 손상 파일을 옆으로 옮기고 기본값
/// </summary>
public sealed class AppSettings
{
    public const string AppName = "SCTheaterEditor";
    private const string LegacyAppName = "SCSpineManager";
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Spine 저장 루트 설정값. 비우면 exe 옆 SpineData, 상대 경로면 exe 폴더 기준</summary>
    [JsonPropertyName("SpineDataFolder")]
    public string SpineDataFolderSetting { get; set; } = "";

    public DownloadSettings Download { get; set; } = new();
    public ResourceSettings Resource { get; set; } = new();
    public ViewerSettings Viewer { get; set; } = new();

    /// <summary>스크립트 탭(에디터) 설정 — 이후 단계에서 채움</summary>
    public EditorSettings Editor { get; set; } = new();

    /// <summary>추가: 로그 창 위치·크기·열림 상태</summary>
    public LogWindowSettings LogWindow { get; set; } = new();

    /// <summary>추가(7회차): 스크립트 탭 화면 배치 (경계선으로 바꾼 크기)</summary>
    public ScriptLayoutSettings ScriptLayout { get; set; } = new();

    /// <summary>읽는 중에 생긴 일 (예전 설정 이전, 손상 복구 등). 로그로 알리기 위한 값, 저장하지 않음</summary>
    [JsonIgnore]
    public string? LoadNotice { get; private set; }

    // ------------------------------------------------------------------ 경로

    /// <summary>기본 Spine 저장 루트 = exe 옆 SpineData (도구 독립 실행 규칙, git에서는 /Tools/**/SpineData/ 제외)</summary>
    public static string DefaultSaveFolder => Path.Combine(AppContext.BaseDirectory, "SpineData");

    /// <summary>Spine 저장 루트(실제 경로). 아래에 _meta(목록 스냅샷·매니페스트·로그)와 {DressType}/{DressName}/ 폴더가 생긴다</summary>
    [JsonIgnore]
    public string SaveFolder => string.IsNullOrWhiteSpace(SpineDataFolderSetting)
        ? DefaultSaveFolder
        : Path.GetFullPath(SpineDataFolderSetting, AppContext.BaseDirectory);

    [JsonIgnore]
    public string MetaFolder => Path.Combine(SaveFolder, "_meta");

    /// <summary>%AppData%\SCTheaterEditor (설정)</summary>
    public static string SettingsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    /// <summary>%LocalAppData%\SCTheaterEditor (WebView2 데이터 등 지워도 되는 것)</summary>
    public static string LocalDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);

    public static string SettingsPath => Path.Combine(SettingsFolder, "settings.json");

    private static string LegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyAppName, "settings.json");

    // ------------------------------------------------------------------ 읽기·저장

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load() => Load(SettingsPath, LegacySettingsPath);

    /// <summary>설정 읽기 (테스트에서 경로를 바꿔 쓰기 위해 분리)</summary>
    public static AppSettings Load(string path, string? legacyPath)
    {
        if (File.Exists(path))
        {
            try
            {
                var (s, fromBackup) = SafeFile.ReadWithBackup(path, json =>
                    JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? throw new JsonException("빈 설정"));
                if (fromBackup) s.LoadNotice = "설정 파일이 손상되어 백업(.bak)에서 복구했습니다";
                return s.Normalize();
            }
            catch (Exception ex)
            {
                return new AppSettings { LoadNotice = $"설정 파일을 읽지 못해 기본값으로 시작합니다 (손상된 파일은 .corrupt로 옮김): {ex.Message}" };
            }
        }

        if (legacyPath != null && File.Exists(legacyPath))
        {
            try
            {
                var s = FromLegacyJson(File.ReadAllText(legacyPath));
                s.LoadNotice = $"예전 SCSpineManager 설정을 가져왔습니다 ({legacyPath}, 예전 파일은 그대로 둠)";
                return s;
            }
            catch (Exception ex)
            {
                return new AppSettings { LoadNotice = $"예전 SCSpineManager 설정을 읽지 못해 기본값으로 시작합니다: {ex.Message}" };
            }
        }
        return new AppSettings();
    }

    public void Save() => Save(SettingsPath);

    public void Save(string path) =>
        SafeFile.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions), keepBackup: true);

    // ------------------------------------------------------------------ 예전 설정 이전

    /// <summary>예전 SCSpineManager의 평평한 설정 → 영역별 설정</summary>
    public static AppSettings FromLegacyJson(string json)
    {
        var old = JsonSerializer.Deserialize<LegacySettings>(json) ?? new LegacySettings();
        var s = new AppSettings
        {
            SpineDataFolderSetting = old.SaveFolder ?? "",
            Download = new DownloadSettings { CheckedIdolIds = old.CheckedIdolIds },
            Resource = new ResourceSettings { CheckedIdolIds = old.ResourceCheckedIdolIds },
            Viewer = new ViewerSettings
            {
                SingleTrackMode = old.ViewerSingleTrackMode,
                LipEnabled = old.ViewerLipEnabled,
                LipAlways = old.ViewerLipAlways,
                LipSeconds = old.ViewerLipSeconds,
            },
        };
        if (!string.IsNullOrWhiteSpace(old.ApiBase)) s.Download.ApiBase = old.ApiBase;
        if (!string.IsNullOrWhiteSpace(old.FileBase)) s.Download.FileBase = old.FileBase;
        if (old.RequestIntervalMs > 0) s.Download.RequestIntervalMs = old.RequestIntervalMs;
        return s.Normalize();
    }

    /// <summary>
    /// 예전 기본 경로가 저장돼 있으면 비움 → 새 기본값(exe 옆 SpineData) 사용
    /// (resource\SpineData: 2026-10-12 이동 전 기본값 / Tools\SCSpineManager\SpineData: 이름 변경 전 exe 옆)
    /// </summary>
    private AppSettings Normalize()
    {
        string v = SpineDataFolderSetting?.Trim().TrimEnd('\\', '/') ?? "";
        if (v.Equals(@"D:\usosctheater\resource\SpineData", StringComparison.OrdinalIgnoreCase) ||
            v.EndsWith(@"\Tools\SCSpineManager\SpineData", StringComparison.OrdinalIgnoreCase))
            SpineDataFolderSetting = "";
        Version = CurrentVersion;
        return this;
    }

    /// <summary>예전 설정 파일 형식 (SCSpineManager 0.1)</summary>
    private sealed class LegacySettings
    {
        public string? SaveFolder { get; set; }
        public string? ApiBase { get; set; }
        public string? FileBase { get; set; }
        public int RequestIntervalMs { get; set; }
        public List<int>? CheckedIdolIds { get; set; }
        public List<int>? ResourceCheckedIdolIds { get; set; }
        public bool ViewerSingleTrackMode { get; set; }
        public bool ViewerLipEnabled { get; set; } = true;
        public bool ViewerLipAlways { get; set; }
        public double ViewerLipSeconds { get; set; } = 3.0;
    }
}

/// <summary>다운로드 탭</summary>
public sealed class DownloadSettings
{
    /// <summary>목록 API 주소 (idollist, dresslist?idolId=)</summary>
    public string ApiBase { get; set; } = "https://api.shinycolors.moe/spine/";

    /// <summary>파일 서버 주소 ({path}data.json · data.atlas · data.png)</summary>
    public string FileBase { get; set; } = "https://cf-static.shinycolors.moe/";

    /// <summary>요청 사이 간격(ms). 개인 운영 서버라 기본 1초, 동시 요청 없음</summary>
    public int RequestIntervalMs { get; set; } = 1000;

    /// <summary>체크한 아이돌 (idolId). null이면 전부 체크</summary>
    public List<int>? CheckedIdolIds { get; set; }

    /// <summary>추가: 아이돌 필터에서 고른 유닛 (드롭다운)</summary>
    public string? SelectedUnit { get; set; }
}

/// <summary>리소스 탭</summary>
public sealed class ResourceSettings
{
    /// <summary>체크한 아이돌 (idolId). null이면 전부 체크</summary>
    public List<int>? CheckedIdolIds { get; set; }

    /// <summary>추가: 아이돌 필터에서 고른 유닛 (드롭다운)</summary>
    public string? SelectedUnit { get; set; }
}

/// <summary>무대 재생 설정 (스크립트 탭의 선택 캐릭터 패널, 이전 뷰어 탭 설정을 그대로 사용)</summary>
public sealed class ViewerSettings
{
    /// <summary>true면 선택한 트랙만 교체(디버그 씬 방식), false면 게임 방식(전체 다시 재생)</summary>
    public bool SingleTrackMode { get; set; }

    /// <summary>립 재생 (게임 방식에서만) — 켜기 / 계속 말함(true) 또는 N초 후 멈춤(false) / N초</summary>
    public bool LipEnabled { get; set; } = true;
    public bool LipAlways { get; set; }
    public double LipSeconds { get; set; } = 3.0;
}

/// <summary>스크립트 탭 (에디터)</summary>
public sealed class EditorSettings
{
    /// <summary>추가: 무대에 마지막으로 고른 배경 키 (없으면 null)</summary>
    public string? Background { get; set; }

    /// <summary>
    /// 추가(5회차): 게임 Assets\Resources — TE가 게임 파일을 찾는 단 하나의 경로 (설정 탭에서 지정).
    /// 아래 폴더는 게임 규칙대로 이 안에서 찾음 (도구 독립 규칙: 코드에 게임 경로를 박지 않음)
    /// </summary>
    public string? GameResourcesFolder { get; set; }

    /// <summary>변경(5회차): Resources\Scenario — 저장하지 않고 Resources 폴더에서 계산 (이전: 따로 지정)</summary>
    [JsonIgnore] public string? ScenarioFolder => Sub("Scenario");

    /// <summary>변경(5회차): Resources\Spine (게임으로 내보내기 대상, 아래 {nickName} 폴더에 씀)</summary>
    [JsonIgnore] public string? GameSpineFolder => Sub("Spine");

    /// <summary>추가(5회차): Resources\BG (무대 배경 — 이전: exe 옆 Backgrounds\)</summary>
    [JsonIgnore] public string? BackgroundFolder => Sub("BG");

    private string? Sub(string name) =>
        string.IsNullOrWhiteSpace(GameResourcesFolder) ? null : Path.Combine(GameResourcesFolder, name);

    /// <summary>변경: 막별로 남길 백업 수 (저장 전 원본 + 자동 백업 공용) 10 → 20</summary>
    public int BackupCount { get; set; } = 20;

    /// <summary>추가: 자동 백업 — 마지막 백업 뒤 편집 N번이면 바로</summary>
    public int AutoBackupEdits { get; set; } = 30;

    /// <summary>추가: 자동 백업 — 마지막 백업 뒤 M분이 지났고 편집이 1번 이상이면</summary>
    public double AutoBackupMinutes { get; set; } = 10;

    /// <summary>추가(4-1): 마지막으로 연 막 파일과 선택한 라인(0부터) — 재실행 시 다시 엶</summary>
    public string? LastActPath { get; set; }
    public int LastLine { get; set; }

    /// <summary>추가: 스크립트 표 칸 폭 (키: 종류.속성, 예: TEXT.Text). 없으면 LineTypes.xml 기본값</summary>
    public Dictionary<string, double> FieldWidths { get; set; } = new();

    /// <summary>추가: 조합키 슬롯 1~9의 줄 종류 ("" = 빈 슬롯). 비어 있으면 기본값(드롭다운 순서) — LineSlots.Normalize</summary>
    public List<string> LineSlots { get; set; } = new();
}

/// <summary>
/// 추가(7회차): 스크립트 탭 배치 — 위쪽 [무대 | Spine 패널 | 오디오 패널]의 패널 폭(px)과 무대 영역 : 스크립트 표 높이 비율.
/// 경계선을 끌면 바뀌고(ScriptLayoutViewModel), 설정 탭 [배치 기본값으로]는 ResetToDefaults
/// </summary>
public sealed class ScriptLayoutSettings
{
    public const double MinPanelWidth = 250;
    public const double MaxPanelWidth = 1200;

    public double ActorWidth { get; set; } = 350;    // Spine 패널 (이전 고정 폭과 같음)
    public double AudioWidth { get; set; } = 350;    // 오디오 패널 (Spine 패널과 같은 폭)
    public double StageWeight { get; set; } = 3;     // 무대 영역 높이 비율
    public double TableWeight { get; set; } = 2;     // 스크립트 표 높이 비율

    /// <summary>기본값으로 (객체는 그대로 — 화면이 이 객체를 바인딩하고 있으므로)</summary>
    public void ResetToDefaults()
    {
        var d = new ScriptLayoutSettings();
        ActorWidth = d.ActorWidth;
        AudioWidth = d.AudioWidth;
        StageWeight = d.StageWeight;
        TableWeight = d.TableWeight;
    }
}

/// <summary>추가: 로그 창 (위치가 없거나 화면 밖이면 메인 창 가운데)</summary>
public sealed class LogWindowSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 320;

    /// <summary>끌 때 열려 있었으면 다음 실행 때 다시 엶</summary>
    public bool Open { get; set; }
}
