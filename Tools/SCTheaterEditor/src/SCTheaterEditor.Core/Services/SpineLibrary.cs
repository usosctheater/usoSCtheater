using System.IO;

namespace SCTheaterEditor.Services;

/// <summary>
/// 받은 Spine 세트 1개 (SpineLibrary의 항목). 스크립트 탭·리소스 탭·뷰어가 함께 쓰는 단위.
/// </summary>
public sealed record SpineAsset(
    string CgKey,
    string SourcePath,
    string BaseName,
    string Group,
    string SpineType,
    int IdolId,
    string IdolName,
    string NickName,
    int IdolOrder,
    string DressType,
    int DressTypeOrder,
    string DressName,
    string EnzaId,
    // 저장 루트 기준 폴더 (원본 = 매니페스트 Folders[0])
    string RelativeFolder,
    string JsonFile,
    string AtlasFile,
    IReadOnlyList<string> ImageFiles,
    DateTime DownloadedAt)
{
    /// <summary>검색용 문자열 (의상명·종류·파일명·CG 키·아이돌 이름)</summary>
    public string SearchText { get; } = $"{DressName} {DressType} {BaseName} {CgKey} {IdolName}";
}

/// <summary>
/// 받은 Spine 세트의 메모리 색인 (서버 목록 스냅샷 없이 매니페스트만으로 만든다).
/// - CG 키 = {nickName}_{파일 이름} (예: madoka_stand_1040210050) — 게임 ExtractIdolKey·립 기본값과 같은 앞부분
/// - 원본 폴더에 json·atlas·텍스처가 모두 있는 세트만 포함
/// - 다운로드가 끝나면 Refresh(sourcePath)로 그 세트만 반영, 바뀌면 Changed 이벤트
/// - UI 스레드에서만 고친다 (다운로드도 UI 스레드에서 이어지므로 잠금 없음)
/// </summary>
public sealed class SpineLibrary
{
    private static readonly string[] GroupOrder = { "idols", "awake_idols", "support_idols", "idol_evolution_skins" };
    private static readonly string[] TypeOrder = { "cb", "cb_costume", "stand", "stand_costume", "picture_motion" };

    private readonly Dictionary<string, SpineAsset> _bySource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpineAsset> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<SpineAsset>? _sorted;

    public string Root { get; }
    public SpineManifest Manifest { get; private set; }

    /// <summary>세트가 추가·삭제·갱신됨</summary>
    public event Action? Changed;

    public SpineLibrary(string root, SpineManifest manifest)
    {
        Root = root;
        Manifest = manifest;
    }

    public int Count => _bySource.Count;

    /// <summary>정렬된 전체 목록: 의상 종류 순서 → 아이돌 목록 순서 → enzaId → 그룹 → 타입 → 파일명</summary>
    public IReadOnlyList<SpineAsset> Assets => _sorted ??= _bySource.Values
        .OrderBy(a => a.DressTypeOrder).ThenBy(a => a.DressType, StringComparer.Ordinal)
        .ThenBy(a => a.IdolOrder).ThenBy(a => a.EnzaId, StringComparer.Ordinal)
        .ThenBy(a => IndexOr(GroupOrder, a.Group)).ThenBy(a => IndexOr(TypeOrder, a.SpineType))
        .ThenBy(a => a.BaseName, StringComparer.Ordinal)
        .ToList();

    public SpineAsset? Find(string cgKey) => _byKey.GetValueOrDefault(cgKey);

    public SpineAsset? FindBySource(string sourcePath) => _bySource.GetValueOrDefault(sourcePath);

    public static string MakeCgKey(string nickName, string baseName) =>
        string.IsNullOrEmpty(nickName) ? baseName : $"{nickName}_{baseName}";

    /// <summary>전체 다시 만들기 (파일 존재 확인 포함). 매니페스트를 바꿔 끼울 때도 사용</summary>
    public void Rebuild(SpineManifest? manifest = null)
    {
        if (manifest != null) Manifest = manifest;
        _bySource.Clear();
        _byKey.Clear();
        foreach (var (source, entry) in Manifest.Sets)
        {
            var asset = Create(source, entry);
            if (asset != null) Add(asset);
        }
        _sorted = null;
        Changed?.Invoke();
    }

    /// <summary>세트 1개만 다시 확인 (다운로드 직후)</summary>
    public void Refresh(string sourcePath)
    {
        if (_bySource.Remove(sourcePath, out var old)) _byKey.Remove(old.CgKey);
        if (Manifest.Sets.TryGetValue(sourcePath, out var entry))
        {
            var asset = Create(sourcePath, entry);
            if (asset != null) Add(asset);
        }
        _sorted = null;
        Changed?.Invoke();
    }

    public string FolderPath(SpineAsset a) => Path.Combine(Root, a.RelativeFolder);

    // ------------------------------------------------------------------

    private void Add(SpineAsset a)
    {
        _bySource[a.SourcePath] = a;
        _byKey[a.CgKey] = a;   // 같은 키(같은 파일을 공유하는 의상)는 하나만 — 원본 폴더 기준
    }

    private SpineAsset? Create(string source, ManifestEntry e)
    {
        if (e.Json == null || e.Atlas == null || e.Images.Count == 0 || e.Folders.Count == 0) return null;
        string folder = e.Folders[0];
        string full = Path.Combine(Root, folder);
        if (!File.Exists(Path.Combine(full, e.Json.SavedName)) || !File.Exists(Path.Combine(full, e.Atlas.SavedName)) ||
            !e.Images.All(i => File.Exists(Path.Combine(full, i.SavedName))))
            return null;

        var idol = Manifest.Idols.GetValueOrDefault(e.IdolId);
        return new SpineAsset(
            CgKey: MakeCgKey(idol?.NickName ?? "", e.BaseName),
            SourcePath: source,
            BaseName: e.BaseName,
            Group: e.Group,
            SpineType: e.SpineType,
            IdolId: e.IdolId,
            IdolName: idol?.Name ?? "",
            NickName: idol?.NickName ?? "",
            IdolOrder: idol?.Order ?? int.MaxValue,
            DressType: e.DressType,
            DressTypeOrder: e.DressTypeOrder,
            DressName: e.DressName.Length > 0 ? e.DressName : Path.GetFileName(folder),
            EnzaId: e.EnzaId,
            RelativeFolder: folder,
            JsonFile: e.Json.SavedName,
            AtlasFile: e.Atlas.SavedName,
            ImageFiles: e.Images.Select(i => i.SavedName).ToList(),
            DownloadedAt: e.DownloadedAt);
    }

    private static int IndexOr(string[] arr, string v)
    {
        int i = Array.IndexOf(arr, v);
        return i < 0 ? arr.Length : i;
    }
}
