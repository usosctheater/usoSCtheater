using System.IO;
using System.Text.Json;

namespace SCSpineManager.Services;

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

    public static SpineManifest Load(string metaFolder)
    {
        string p = PathOf(metaFolder);
        if (!File.Exists(p)) return new SpineManifest();
        var m = JsonSerializer.Deserialize<SpineManifest>(File.ReadAllText(p)) ?? new SpineManifest();
        // 역직렬화하면 비교자가 기본값으로 바뀌므로 다시 지정
        m.Sets = new Dictionary<string, ManifestEntry>(m.Sets, StringComparer.Ordinal);
        return m;
    }

    /// <summary>임시 파일에 쓴 뒤 교체 (쓰는 도중 꺼져도 이전 기록이 남음)</summary>
    public void Save(string metaFolder)
    {
        Directory.CreateDirectory(metaFolder);
        string p = PathOf(metaFolder);
        string tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, p, overwrite: true);
    }
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
