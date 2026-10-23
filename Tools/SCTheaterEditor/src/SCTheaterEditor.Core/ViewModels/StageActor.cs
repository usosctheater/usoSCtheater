using CommunityToolkit.Mvvm.ComponentModel;
using SCTheaterEditor.Services;
using SCTheaterEditor.Stage;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 무대 위 캐릭터 1명 (스크립트 탭). 트랙 0~3의 애니메이션·스킨·위치를 가진다.
/// 무대 페이지의 실제 Spine 객체와는 Id로 연결
/// </summary>
public sealed partial class StageActor : ObservableObject
{
    public StageActor(int id, SpineAsset asset, string position)
    {
        Id = id;
        Asset = asset;
        Position = position;
        Skins = [];
        Info = "불러오는 중…";
    }

    public int Id { get; }
    public SpineAsset Asset { get; }

    /// <summary>무대 위 이름표 (아이돌 이름, 없으면 의상명)</summary>
    public string Label => Asset.IdolName.Length > 0 ? Asset.IdolName : Asset.DressName;

    /// <summary>XML Position 값 (center, wide_left …)</summary>
    [ObservableProperty] public partial string Position { get; set; }

    /// <summary>미리보기 중 (Enter 적용 / Esc 취소 전)</summary>
    [ObservableProperty] public partial bool IsPreview { get; set; }

    [ObservableProperty] public partial bool IsLoaded { get; set; }

    /// <summary>추가: 이 캐릭터 일시정지 (재생/일시정지는 캐릭터별, 선택이 없으면 무대 전체)</summary>
    public bool Paused { get; set; }

    /// <summary>추가(8회차): 불러오면 바로 끝 상태(end-loop)로 — F5에서 줄의 Spine이 아닌 새로 올린 캐릭터</summary>
    public bool SeekEndOnLoad { get; set; }

    [ObservableProperty] public partial IReadOnlyList<string> Skins { get; set; }
    [ObservableProperty] public partial string? Skin { get; set; }

    /// <summary>불러온 정보 (Spine 버전·본·애니메이션 수·크기)</summary>
    [ObservableProperty] public partial string Info { get; set; }

    /// <summary>트랙 0~3에 걸린 애니메이션 (없으면 null)</summary>
    public string?[] Tracks { get; } = new string?[AnimationScript.TrackCount];

    /// <summary>데이터의 애니메이션 목록</summary>
    public IReadOnlyList<AnimInfo> Animations { get; set; } = [];

    /// <summary>지금 트랙 배치의 Animation 값 (4회차 "라인에 적용"에서 사용)</summary>
    public string AnimationText => AnimationScript.Build(Tracks);
}
