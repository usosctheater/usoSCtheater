namespace SCSpineManager.Models;

/// <summary>
/// 받을 Spine 1세트 (json + atlas + png). 서버 경로(SourcePath) 하나당 1개.
/// 같은 경로를 여러 의상이 공유하면(enzaId 중복) 저장 위치(Targets)가 여러 개가 된다 — 한 번만 받고 나머지는 로컬 복사.
/// </summary>
public sealed class SpineSet
{
    /// <summary>파일 서버 기준 경로 (예: spine/idols/cb/1040210050/)</summary>
    public required string SourcePath { get; init; }
    /// <summary>dresslist의 assets 그룹명 (idols, awake_idols …)</summary>
    public required string Group { get; init; }
    /// <summary>Spine 타입 (cb, stand, cb_costume …)</summary>
    public required string SpineType { get; init; }
    /// <summary>저장 파일 이름 (확장자 제외, 예: awake_cb_costume_1040210050)</summary>
    public required string BaseName { get; init; }

    public required int IdolId { get; init; }
    public required string IdolName { get; init; }
    public required string EnzaId { get; init; }

    /// <summary>저장 폴더 목록 (저장 루트 기준 상대 경로). [0]이 원본, 나머지는 복사본</summary>
    public List<SpineTarget> Targets { get; } = new();
}

/// <summary>저장 위치 1곳</summary>
public sealed record SpineTarget(string DressType, int DressTypeOrder, string DressName, string RelativeFolder);

/// <summary>받지 않고 건너뛴 항목</summary>
public sealed record SkippedEntry(int IdolId, string EnzaId, string DressName, string Detail, string Reason);

/// <summary>목록 스냅샷으로 만든 다운로드 계획</summary>
public sealed class SpinePlan
{
    public List<SpineSet> Sets { get; } = new();
    public List<SkippedEntry> Skipped { get; } = new();
    /// <summary>서로 다른 서버 경로가 같은 저장 파일명으로 겹친 경우 (명명 규칙 점검용, 0이어야 정상)</summary>
    public List<string> Conflicts { get; } = new();

    public int DressCount { get; set; }
    public int FileCount => Sets.Count * 3;
    public int CopyCount => Sets.Sum(s => s.Targets.Count - 1);
}
