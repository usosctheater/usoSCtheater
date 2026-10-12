using System.IO;
using System.Text.Json;
using SCTheaterEditor.Models;

namespace SCTheaterEditor.Services;

/// <summary>
/// 받은 세트 기록. {SaveFolder}\_meta\manifest.json
/// 키 = 서버 경로(SourcePath). 이름 규칙이 바뀌어도 이 기록으로 서버 요청 없이 로컬에서 다시 정리할 수 있다.
/// v2 (2026-10-12): 아이돌 표(Idols)와 세트별 아이돌·의상 정보를 함께 기록 → 서버 목록 스냅샷 없이도 리소스 목록·CG 키를 만들 수 있음
/// </summary>
public sealed class SpineManifest
{
    public const int CurrentVersion = 2;

    /// <summary>읽은 파일의 형식 버전 (v1 파일은 1로 읽힘 → FillFromCatalog로 채운 뒤 2)</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>추가(v2): idolId → 아이돌 정보 (이름·nickName·아이돌 목록 순서)</summary>
    public Dictionary<int, ManifestIdol> Idols { get; set; } = new();

    public Dictionary<string, ManifestEntry> Sets { get; set; } = new(StringComparer.Ordinal);

    /// <summary>v1 기록이거나 아이돌·의상 정보가 빠진 세트가 있음 → 목록 스냅샷으로 채워야 함</summary>
    public bool NeedsCatalogInfo => Version < CurrentVersion || Sets.Values.Any(e => e.IdolId == 0 || !Idols.ContainsKey(e.IdolId));

    /// <summary>아이돌 표 갱신 (목록 스냅샷 기준, 목록 순서 = 표시 순서). 바뀌었으면 true</summary>
    public bool UpdateIdols(IReadOnlyList<IdolInfo> idols)
    {
        bool changed = false;
        for (int i = 0; i < idols.Count; i++)
        {
            var src = idols[i];
            if (Idols.TryGetValue(src.IdolId, out var cur) && cur.Name == src.IdolName && cur.NickName == src.NickName && cur.Order == i)
                continue;
            Idols[src.IdolId] = new ManifestIdol { Name = src.IdolName, NickName = src.NickName, Order = i };
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// v1 → v2 이전: 다운로드 계획(목록 스냅샷 기준)으로 세트별 아이돌·의상 정보를 채운다. 채운 세트 수 반환.
    /// 이미 정보가 있는 세트도 계획과 다르면 갱신 (의상명 변경 등)
    /// </summary>
    public int FillFromCatalog(CatalogSnapshot snap, SpinePlan plan)
    {
        UpdateIdols(snap.Idols);
        int filled = 0;
        foreach (var set in plan.Sets)
        {
            if (!Sets.TryGetValue(set.SourcePath, out var e)) continue;
            if (e.ApplySetInfo(set)) filled++;
        }
        Version = CurrentVersion;
        return filled;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string PathOf(string metaFolder) => Path.Combine(metaFolder, "manifest.json");

    /// <summary>
    /// 변경: 손상 대비 읽기 — 원본을 못 읽으면 .bak, 그것도 안 되면 손상 파일을 옆으로 옮기고 빈 기록으로 시작
    /// (예전에는 손상 시 빈 기록으로 시작한 뒤 다음 저장에서 손상 파일을 덮어썼음)
    /// </summary>
    public static SpineManifest Load(string metaFolder)
    {
        string p = PathOf(metaFolder);
        if (!File.Exists(p)) return new SpineManifest();
        try
        {
            var (m, fromBackup) = SafeFile.ReadWithBackup(p, json =>
                JsonSerializer.Deserialize<SpineManifest>(json) ?? throw new JsonException("빈 매니페스트"));
            if (fromBackup) AppLog.Shared.Warn("매니페스트가 손상되어 백업(manifest.json.bak)에서 복구했습니다");
            // 역직렬화하면 비교자가 기본값으로 바뀌므로 다시 지정
            m.Sets = new Dictionary<string, ManifestEntry>(m.Sets, StringComparer.Ordinal);
            return m;
        }
        catch (Exception ex)
        {
            AppLog.Shared.Error("매니페스트를 읽지 못해 빈 기록으로 시작합니다 (손상된 파일은 manifest.json.corrupt-…로 옮김)", ex);
            return new SpineManifest();
        }
    }

    /// <summary>변경: SafeFile로 저장 (임시 파일 → 교체, 직전 기록은 manifest.json.bak)</summary>
    public void Save(string metaFolder) =>
        SafeFile.WriteAllText(PathOf(metaFolder), JsonSerializer.Serialize(this, JsonOptions), keepBackup: true);
}

/// <summary>추가(v2): 아이돌 1명 (idollist 기준)</summary>
public sealed class ManifestIdol
{
    public string Name { get; set; } = "";
    /// <summary>게임 키(CG 키 앞부분, DefaultLipData 아이돌 이름)와 같은 형식</summary>
    public string NickName { get; set; } = "";
    /// <summary>아이돌 목록 순서 (0부터)</summary>
    public int Order { get; set; }
}

/// <summary>세트 1개의 기록</summary>
public sealed class ManifestEntry
{
    // 추가(v2): 아이돌·의상 정보 (첫 저장 위치 = Folders[0] 기준, 의상명은 서버 원문 — 폴더명처럼 문자를 지우지 않음)
    public int IdolId { get; set; }
    public string DressType { get; set; } = "";
    public int DressTypeOrder { get; set; }
    public string DressName { get; set; } = "";

    /// <summary>다운로드 계획의 세트 정보로 채움. 바뀐 값이 있으면 true</summary>
    public bool ApplySetInfo(SpineSet set)
    {
        var t = set.Targets[0];
        if (IdolId == set.IdolId && DressType == t.DressType && DressTypeOrder == t.DressTypeOrder && DressName == t.DressName)
            return false;
        IdolId = set.IdolId;
        DressType = t.DressType;
        DressTypeOrder = t.DressTypeOrder;
        DressName = t.DressName;
        return true;
    }

    public string BaseName { get; set; } = "";
    public string Group { get; set; } = "";
    public string SpineType { get; set; } = "";
    public string EnzaId { get; set; } = "";
    /// <summary>저장한 폴더들 (저장 루트 기준, [0]이 원본)</summary>
    public List<string> Folders { get; set; } = new();
    public DateTime DownloadedAt { get; set; }

    public RemoteFile? Json { get; set; }
    public RemoteFile? Atlas { get; set; }
    /// <summary>atlas 페이지 순서대로 (보통 1장)</summary>
    public List<RemoteFile> Images { get; set; } = new();
}

/// <summary>서버 파일 1개의 정보 (변경 확인용)</summary>
public sealed class RemoteFile
{
    /// <summary>서버 파일명 (data.json, data.atlas, data.png …)</summary>
    public string Name { get; set; } = "";
    /// <summary>저장한 파일명</summary>
    public string SavedName { get; set; } = "";
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public long Size { get; set; }
    /// <summary>이미지 원본 형식 (png / webp / …). 변환했으면 원래 형식이 남음</summary>
    public string? Format { get; set; }
}
