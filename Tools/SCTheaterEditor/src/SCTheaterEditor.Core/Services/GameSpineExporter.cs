using System.IO;
using System.Text;

namespace SCTheaterEditor.Services;

/// <summary>게임 내보내기 결과</summary>
public enum GameExportStatus
{
    Copied,       // 게임에 없어서 새로 복사
    Same,         // 이미 같은 내용 — 건너뜀
    Overwritten,  // 내용이 달라 물어본 뒤 덮어씀
    Skipped,      // 내용이 달라 물어봤는데 건너뜀
    Failed,       // 복사할 수 없음 (nickName 없음, 파일 오류 등)
}

public sealed record GameExportResult(SpineAsset Asset, GameExportStatus Status, string? Message = null);

/// <summary>
/// TE 세트 → 게임 Resources/Spine 내보내기 (2026-10-16 스파인 데이터 형식 통일 3회차 결정 1·11~14)
/// - 위치: {게임 Spine 폴더}/{nickName}/
/// - 파일: {CG 키}.json · {CG 키}.atlas.txt · {CG 키}.png (여러 장이면 TE 파일 이름의 BaseName 부분만 CG 키로: stand_x_2.png → fuyuko_stand_x_2.png)
/// - atlas의 페이지 줄(png 이름)도 같은 이름으로 → spine-unity가 {CG 키}_Material을 만들고 SpineAutoImporter가 그대로 등록
/// - 게임에 이미 있으면: 바꾼 결과와 바이트가 같으면 건너뜀, 다르면 confirmOverwrite로 물어봄
/// - 씬 등록은 하지 않음 (Unity SpineAutoImporter 담당)
/// </summary>
public sealed class GameSpineExporter(SpineLibrary library)
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    /// <summary>원본 파일 이름 → 게임 파일 이름 (json, atlas, 텍스처 순)</summary>
    public static IReadOnlyList<(string Source, string Target)> FileMap(SpineAsset a)
    {
        var map = new List<(string, string)>
        {
            (a.JsonFile, a.CgKey + SpineNaming.JsonExt),
            (a.AtlasFile, a.CgKey + SpineNaming.AtlasExt),
        };
        map.AddRange(a.ImageFiles.Select(f => (f, ImageTarget(a, f))));
        return map;
    }

    /// <summary>텍스처 이름: BaseName으로 시작하면 그 부분만 CG 키로</summary>
    public static string ImageTarget(SpineAsset a, string imageFile) =>
        imageFile.StartsWith(a.BaseName, StringComparison.Ordinal)
            ? a.CgKey + imageFile[a.BaseName.Length..]
            : $"{a.CgKey}_{imageFile}";

    public static string TargetFolder(string gameSpineFolder, SpineAsset a) => Path.Combine(gameSpineFolder, a.NickName);

    /// <summary>게임에 이 세트의 json이 있는지 (정보 표시용 빠른 확인)</summary>
    public static bool ExistsInGame(string gameSpineFolder, SpineAsset a) =>
        a.NickName.Length > 0 && File.Exists(Path.Combine(TargetFolder(gameSpineFolder, a), a.CgKey + SpineNaming.JsonExt));

    /// <summary>추가: 게임 폴더에 있는 세트의 CG 키 목록 ({nickName} 폴더 안 json 파일 이름 기준, 한 번에 훑음 — 리소스 목록 ◎ 표시용)</summary>
    public static HashSet<string> ScanGameKeys(string? gameSpineFolder)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(gameSpineFolder) || !Directory.Exists(gameSpineFolder)) return keys;
        foreach (var dir in Directory.EnumerateDirectories(gameSpineFolder))
            foreach (var json in Directory.EnumerateFiles(dir, "*" + SpineNaming.JsonExt))
                keys.Add(Path.GetFileNameWithoutExtension(json));
        return keys;
    }

    /// <summary>atlas의 페이지 줄(이미지 이름만 있는 줄)만 바꿈. 다른 줄·줄바꿈 문자는 그대로</summary>
    public static string RewriteAtlas(string atlas, IReadOnlyDictionary<string, string> pages)
    {
        var lines = atlas.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            bool cr = lines[i].EndsWith('\r');
            string body = cr ? lines[i][..^1] : lines[i];
            if (pages.TryGetValue(body.Trim(), out var renamed))
                lines[i] = renamed + (cr ? "\r" : "");
        }
        return string.Join('\n', lines);
    }

    /// <summary>세트 1개 내보내기. confirmOverwrite(세트, 다른 파일 이름들) → true면 덮어씀</summary>
    public GameExportResult Export(string gameSpineFolder, SpineAsset a, Func<SpineAsset, IReadOnlyList<string>, bool> confirmOverwrite)
    {
        if (a.NickName.Length == 0)
            return new(a, GameExportStatus.Failed, "아이돌 nickName이 없어 게임 폴더를 정할 수 없음");
        try
        {
            var files = Prepare(a);
            string folder = TargetFolder(gameSpineFolder, a);
            // 변경(5회차): atlas는 줄바꿈(CRLF/LF) 차이를 무시 — git 체크아웃(core.autocrlf)이 CRLF로 바꿔 둔 같은 파일을 "다름"으로 보지 않도록
            var differs = files.Where(f => !(f.Name.EndsWith(SpineNaming.AtlasExt, StringComparison.OrdinalIgnoreCase)
                                                 ? SameText(Path.Combine(folder, f.Name), f.Bytes)
                                                 : SameBytes(Path.Combine(folder, f.Name), f.Bytes)))
                               .Select(f => f.Name).ToList();
            if (differs.Count == 0) return new(a, GameExportStatus.Same);

            bool existed = files.Any(f => File.Exists(Path.Combine(folder, f.Name)));
            if (existed && !confirmOverwrite(a, differs))
                return new(a, GameExportStatus.Skipped, $"다른 파일: {string.Join(", ", differs)}");

            foreach (var f in files.Where(f => differs.Contains(f.Name)))
                SafeFile.WriteAllBytes(Path.Combine(folder, f.Name), f.Bytes);   // .tmp → 교체 (Unity는 .tmp를 무시)
            return new(a, existed ? GameExportStatus.Overwritten : GameExportStatus.Copied);
        }
        catch (Exception ex)
        {
            return new(a, GameExportStatus.Failed, ex.Message);
        }
    }

    /// <summary>게임에 쓸 파일 이름·내용 (atlas는 페이지 줄을 바꾼 내용)</summary>
    private List<(string Name, byte[] Bytes)> Prepare(SpineAsset a)
    {
        string src = library.FolderPath(a);
        var pages = a.ImageFiles.ToDictionary(f => f, f => ImageTarget(a, f), StringComparer.Ordinal);
        var result = new List<(string, byte[])>();
        foreach (var (source, target) in FileMap(a))
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(src, source));
            if (source == a.AtlasFile)
                bytes = Utf8NoBom.GetBytes(RewriteAtlas(Encoding.UTF8.GetString(bytes), pages));
            result.Add((target, bytes));
        }
        return result;
    }

    private static bool SameBytes(string path, byte[] bytes) =>
        File.Exists(path) && new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);

    /// <summary>추가(5회차): 글자 파일 비교 — CR을 빼고 비교 (줄바꿈 CRLF/LF만 다르면 같음)</summary>
    private static bool SameText(string path, byte[] bytes) =>
        File.Exists(path) && WithoutCr(File.ReadAllBytes(path)).AsSpan().SequenceEqual(WithoutCr(bytes));

    private static byte[] WithoutCr(byte[] bytes) => Array.FindAll(bytes, b => b != (byte)'\r');
}
