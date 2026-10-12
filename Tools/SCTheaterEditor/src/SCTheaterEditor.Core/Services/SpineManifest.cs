using System.IO;
using System.Text.Json;

namespace SCTheaterEditor.Services;

/// <summary>
/// 받은 세트 기록. {SaveFolder}\_meta\manifest.json
/// 키 = 서버 경로(SourcePath). 이름 규칙이 바뀌어도 이 기록으로 서버 요청 없이 로컬에서 다시 정리할 수 있다.
/// </summary>
public sealed class SpineManifest
{
    public int Version { get; set; } = 1;
    public Dictionary<string, ManifestEntry> Sets { get; set; } = new(StringComparer.Ordinal);

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

/// <summary>세트 1개의 기록</summary>
public sealed class ManifestEntry
{
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
