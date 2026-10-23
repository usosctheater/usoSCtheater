using System.Text.Json;
using System.Text.Json.Serialization;

namespace SCTheaterEditor.Stage;

/// <summary>
/// 무대 페이지(Viewer\js\*.js)와 주고받는 메시지 형식. 양쪽이 이 파일을 기준으로 맞춘다 (JS 쪽 대응: js/main.js).
/// C# → JS: StageCommand 하위 레코드 (JSON의 type = Type 속성)
/// JS → C#: StageEvents.Parse가 JSON → StageEvent 하위 레코드로 바꿈
/// </summary>
public abstract record StageCommand
{
    [JsonPropertyOrder(-1)]
    public abstract string Type { get; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>실제 타입 기준으로 직렬화 (하위 레코드의 속성까지)</summary>
    public string ToJson() => JsonSerializer.Serialize(this, GetType(), Options);
}

/// <summary>무대 설정 (페이지가 준비되면 처음 보냄) — 트랙 수·화면 크기·발 위치 등은 C#(Core)이 기준</summary>
public sealed record StageConfig(int TrackCount, double ScreenWidth, double ScreenHeight, double UnitScale) : StageCommand
{
    public override string Type => "config";
}

/// <summary>배경 (url이 null이면 배경 없음)</summary>
public sealed record SetBackground(string? Url) : StageCommand
{
    public override string Type => "background";
}

/// <summary>캐릭터 올리기 (불러오기가 끝나면 ActorLoaded)</summary>
public sealed record AddActor(int Id, string FolderUrl, string Json, string Atlas, string Label, bool Preview) : StageCommand
{
    public override string Type => "addActor";
}

public sealed record RemoveActor(int Id) : StageCommand
{
    public override string Type => "removeActor";
}

/// <summary>위치 (x·y = 화면 중심 기준 px, y는 위가 +), 스케일 배율</summary>
public sealed record PlaceActor(int Id, double X, double Y, double Scale) : StageCommand
{
    public override string Type => "place";
}

/// <summary>보이기/숨기기 (미리보기가 자리를 대신 쓰는 동안 원래 캐릭터 숨김)</summary>
public sealed record ShowActor(int Id, bool Visible) : StageCommand
{
    public override string Type => "show";
}

/// <summary>미리보기 표시 켜기/끄기</summary>
public sealed record SetPreview(int Id, bool Preview) : StageCommand
{
    public override string Type => "preview";
}

/// <summary>선택 표시 (null = 선택 없음). 상태(State) 메시지는 선택한 캐릭터만 보냄</summary>
public sealed record SelectActor(int? Id) : StageCommand
{
    public override string Type => "select";
}

/// <summary>립 재생 정보 (Seconds &lt; 0 = 계속 말함)</summary>
public sealed record LipPlay(string Moving, string? Still, double Seconds);

/// <summary>게임 방식 재생: 전체 초기화 후 문자열 전체 (CGManager.PlayAnimation) + 립</summary>
public sealed record PlayAnims(int Id, IReadOnlyList<string> Anims, LipPlay? Lip) : StageCommand
{
    public override string Type => "play";
}

/// <summary>디버그 방식: 그 트랙만 교체 (Anim이 null이면 트랙 비움)</summary>
public sealed record SetTrack(int Id, int Track, string? Anim) : StageCommand
{
    public override string Type => "setTrack";
}

public sealed record ResetTracks(int Id) : StageCommand
{
    public override string Type => "reset";
}

public sealed record SetSkin(int Id, string Name) : StageCommand
{
    public override string Type => "skin";
}

/// <summary>
/// 변경: 재생/일시정지 대상 지정 (Id = null이면 무대 전체).
/// 무대 전체 재생은 먼저 모두를 드래그 바 시점으로 맞춘 뒤 재생 (바가 끝이면 0초부터)
/// </summary>
public sealed record SetPaused(bool Paused, int? Id = null) : StageCommand
{
    public override string Type => "pause";
}

/// <summary>
/// 변경: 일시정지 중 시점 이동 — 실제 초 기준 (Id = null이면 무대 전체).
/// 모든 트랙을 "동시에 시작해 Seconds초 지난 상태"로 맞춤 (게임 규칙: 1회 재생은 끝이면 마지막 동작 유지, wait·blank 반복, loop_start 반복)
/// </summary>
public sealed record Seek(int? Id, double Seconds) : StageCommand
{
    public override string Type => "seek";
}

/// <summary>추가: 본 목록 실시간 갱신 켜기/끄기 (켜져 있으면 선택한 캐릭터의 상태 메시지에 본 목록을 함께 보냄)</summary>
public sealed record WatchBones(bool On) : StageCommand
{
    public override string Type => "watchBones";
}

/// <summary>기본 자세와 달라진 본 목록 요청</summary>
public sealed record RequestBones(int Id) : StageCommand
{
    public override string Type => "bones";
}

/// <summary>
/// 추가(6회차): 오디오 재생 — 채널마다 하나 (같은 채널에 다시 보내면 바꿔 재생). Url = StageUrls.Resource.
/// 추가(8회차): StopAt &gt; 0이면 그 초에서 멈춤 (SE 줄의 재생 시간)
/// </summary>
public sealed record PlayAudio(string Channel, string Url, double Volume, bool Loop, double Start = 0, double StopAt = 0) : StageCommand
{
    public override string Type => "audioPlay";
}

/// <summary>추가(8회차): 오디오 일시정지 / 이어 재생 (재생 바)</summary>
public sealed record PauseAudio(string Channel, bool Paused) : StageCommand
{
    public override string Type => "audioPause";
}

/// <summary>
/// 추가(8회차): 캐릭터를 '끝 시점'으로 — 가장 긴 트랙(과 립) 끝으로 시점 이동 후 재생 (게임에서 대사를 넘긴 뒤의 end-loop 상태).
/// F5 때 그 줄이 아닌 Spine (재생 바에는 묶지 않음)
/// </summary>
public sealed record SeekEnd(int Id) : StageCommand
{
    public override string Type => "seekEnd";
}

/// <summary>추가(6회차): 오디오 정지 (Channel = null이면 전부)</summary>
public sealed record StopAudio(string? Channel) : StageCommand
{
    public override string Type => "audioStop";
}

/// <summary>추가(6회차): 오디오 재생 위치 이동 (초)</summary>
public sealed record SeekAudio(string Channel, double Seconds) : StageCommand
{
    public override string Type => "audioSeek";
}

// ====================================================================== JS → C#

public abstract record StageEvent;

/// <summary>페이지 준비 완료 (처음 열 때·새로 고침 때)</summary>
public sealed record StageReady : StageEvent;

public sealed record AnimInfo(string Name, double Duration, double LoopStart);

public sealed record ActorLoaded(int Id, string Version, double Width, double Height, int BoneCount,
                                 IReadOnlyList<string> Skins, string Skin, IReadOnlyList<AnimInfo> Animations) : StageEvent;

public sealed record ActorError(int Id, string Message) : StageEvent;

public sealed record TrackState(string Name, double Time, double Duration, bool Loop, bool Holding);

public sealed record LipState(string Phase, double Elapsed, double Seconds, string Moving, string? Still);

/// <summary>
/// 선택한 캐릭터의 트랙 상태 (10회/초).
/// 변경: Progress → Clock(재생 시작 후 경과 초)·Length(가장 긴 트랙 길이), 본 목록 실시간 갱신이 켜져 있으면 Bones
/// </summary>
public sealed record ActorState(int Id, IReadOnlyList<TrackState?> Tracks, bool Paused, double Clock, double Length,
                                LipState? Lip, IReadOnlyList<BoneDelta>? Bones = null) : StageEvent;

/// <summary>추가: 선택한 캐릭터가 없을 때 무대 전체 시간 (10회/초). Length = 모든 캐릭터·트랙 중 가장 긴 애니메이션, Paused = 모두 멈춤</summary>
public sealed record StageTime(double Time, double Length, bool Paused) : StageEvent;

/// <summary>추가: 배경 이미지 불러오기 실패 (로그에 남김)</summary>
public sealed record BackgroundError(string Url, string Message) : StageEvent;

public sealed record BoneDelta(string Name, double Dr, double Dx, double Dy, double Dsx, double Dsy);

public sealed record ActorBones(int Id, IReadOnlyList<BoneDelta> Bones) : StageEvent;

/// <summary>무대 클릭 (캐릭터가 없는 곳이면 Id = null)</summary>
public sealed record StageClicked(int? Id) : StageEvent;

/// <summary>드래그로 옮겨 놓음 (X = 화면 중심 기준 px)</summary>
public sealed record ActorDropped(int Id, double X) : StageEvent;

/// <summary>무대에 포커스가 있을 때 누른 키 (Ctrl·Alt 조합은 C#의 AcceleratorKeyPressed가 처리)</summary>
public sealed record StageKey(string Key, bool Shift) : StageEvent;

// 추가(6회차): 오디오 이벤트 (Duration·Time = 초)
public sealed record AudioStarted(string Channel, string Url, double Duration) : StageEvent;
public sealed record AudioSeeked(string Channel, double Time) : StageEvent;
public sealed record AudioEnded(string Channel) : StageEvent;
public sealed record AudioError(string Channel, string Url, string Message) : StageEvent;

/// <summary>추가(8회차): 재생 중이거나 멈춰 있는 오디오 채널의 시간 (10회/초, 재생 바)</summary>
public sealed record AudioTime(string Channel, double Time, double Duration, bool Paused) : StageEvent;

public static class StageEvents
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>JSON → 이벤트 (모르는 type이면 null)</summary>
    public static StageEvent? Parse(JsonElement msg)
    {
        string type = msg.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        return type switch
        {
            "ready" => new StageReady(),
            "actorLoaded" => msg.Deserialize<ActorLoaded>(Options),
            "actorError" => msg.Deserialize<ActorError>(Options),
            "state" => msg.Deserialize<ActorState>(Options),
            "stageTime" => msg.Deserialize<StageTime>(Options),               // 추가
            "backgroundError" => msg.Deserialize<BackgroundError>(Options),   // 추가
            "bones" => msg.Deserialize<ActorBones>(Options),
            "clicked" => msg.Deserialize<StageClicked>(Options),
            "dropped" => msg.Deserialize<ActorDropped>(Options),
            "key" => msg.Deserialize<StageKey>(Options),
            "audioStarted" => msg.Deserialize<AudioStarted>(Options),   // 추가(6회차)
            "audioSeeked" => msg.Deserialize<AudioSeeked>(Options),
            "audioEnded" => msg.Deserialize<AudioEnded>(Options),
            "audioError" => msg.Deserialize<AudioError>(Options),
            "audioTime" => msg.Deserialize<AudioTime>(Options),         // 추가(8회차)
            _ => null,
        };
    }
}

/// <summary>무대 페이지와의 연결 (WPF: WebView2, 테스트: 가짜)</summary>
public interface IStageHost
{
    void Send(StageCommand command);
    event Action<StageEvent>? EventReceived;
}

/// <summary>무대 페이지가 파일을 읽는 주소 (WebView2가 요청을 가로채 제공)</summary>
public static class StageUrls
{
    public const string AppHost = "viewer.scsm";
    public const string DataHost = "spinedata.scsm";
    public const string BackgroundHost = "backgrounds.scsm";

    /// <summary>Spine 저장 루트 기준 상대 폴더 → 폴더 URL (끝에 / 포함, 경로 조각마다 인코딩)</summary>
    public static string SpineFolder(string relativeFolder) =>
        $"https://{DataHost}/" + string.Concat(relativeFolder
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(seg => Uri.EscapeDataString(seg) + "/"));

    /// <summary>배경 파일 이름 → URL</summary>
    public static string Background(string fileName) => $"https://{BackgroundHost}/{Uri.EscapeDataString(fileName)}";

    /// <summary>추가(6회차): 게임 Resources (무대 페이지에는 Audio\ 아래만 제공)</summary>
    public const string ResourcesHost = "resources.scsm";

    /// <summary>추가(6회차): 게임 Resources 기준 상대 경로 → URL (예: Audio/SE/se_baton.mp3, 경로 조각마다 인코딩)</summary>
    public static string Resource(string relativePath) =>
        $"https://{ResourcesHost}/" + string.Join("/", relativePath
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
}
