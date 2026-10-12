using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Models;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 리소스 탭: 받은 세트(SpineLibrary)를 세로 목록 하나로 보여준다.
/// - 의상 종류마다 구분선(── P_UR ──), 한 줄 = 세트 1개 (의상명 · 타입 · CG 키). 의상명은 서버 원문
/// - 아이돌 필터(유닛 드롭다운) + 검색어
/// - 선택 → 정보·텍스처 미리보기, 열기(Space·더블클릭·버튼) → OpenSpineMessage
/// 서버 목록 스냅샷과 무관 (매니페스트 v2의 아이돌 표 사용)
/// </summary>
public sealed partial class ResourceViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SpineLibrary _library;
    private readonly IReadOnlyList<IdolUnit> _units;
    private readonly IDialogService _dialogs;
    private readonly Action _saveSettings;
    private readonly IMessenger _messenger;
    private readonly AppLog _log;
    private string _idolSignature = "";

    public ResourceViewModel(AppSettings settings, SpineLibrary library, IReadOnlyList<IdolUnit> units, IDialogService dialogs,
                             Action saveSettings, IMessenger messenger, AppLog log)
    {
        _settings = settings;
        _library = library;
        _units = units;
        _dialogs = dialogs;
        _saveSettings = saveSettings;
        _messenger = messenger;
        _log = log;

        Items = [];
        SearchText = "";
        SummaryText = "";
        InfoText = "";

        Filter.Changed += () =>
        {
            RebuildItems();
            _settings.Resource.CheckedIdolIds = Filter.CheckedIds;
            _saveSettings();
        };
        Filter.SelectedUnitChanged += () =>
        {
            _settings.Resource.SelectedUnit = Filter.SelectedUnit?.Name;
            _saveSettings();
        };
        _library.Changed += OnLibraryChanged;
        OnLibraryChanged();
    }

    /// <summary>아이돌 표에 없는 아이돌(정보가 아직 없는 세트)도 보여줌</summary>
    public IdolFilterViewModel Filter { get; } = new(allowUnknown: true);

    [ObservableProperty] public partial IReadOnlyList<ResourceItem> Items { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedAsset))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(AddImmediatelyCommand), nameof(OpenFolderCommand))]
    public partial ResourceItem? SelectedItem { get; set; }

    public SpineAsset? SelectedAsset => (SelectedItem as ResourceAssetItem)?.Asset;

    [ObservableProperty] public partial string SearchText { get; set; }
    [ObservableProperty] public partial string SummaryText { get; private set; }
    [ObservableProperty] public partial string InfoText { get; private set; }

    /// <summary>미리보기할 텍스처 경로 (없으면 null)</summary>
    [ObservableProperty] public partial string? PreviewPath { get; private set; }

    partial void OnSearchTextChanged(string value) => RebuildItems();

    partial void OnSelectedItemChanged(ResourceItem? value) => ShowInfo(SelectedAsset);

    // ------------------------------------------------------------------ 명령

    private bool HasAsset() => SelectedAsset != null;

    /// <summary>선택한 세트를 스크립트 탭 무대에 미리보기 (Space·더블클릭·버튼)</summary>
    [RelayCommand(CanExecute = nameof(HasAsset))]
    private void Open()
    {
        if (SelectedAsset is { } a) _messenger.Send(new OpenSpineMessage(a));
    }

    /// <summary>추가: 미리보기 없이 바로 무대에 추가 (Ctrl+Space)</summary>
    [RelayCommand(CanExecute = nameof(HasAsset))]
    private void AddImmediately()
    {
        if (SelectedAsset is { } a) _messenger.Send(new OpenSpineMessage(a, Immediate: true));
    }

    [RelayCommand(CanExecute = nameof(HasAsset))]
    private void OpenFolder()
    {
        if (SelectedAsset is not { } a) return;
        try { _dialogs.OpenInExplorer(Path.Combine(_library.FolderPath(a), a.JsonFile)); }
        catch (Exception ex) { _log.Error("탐색기 열기 실패", ex); }
    }

    /// <summary>파일이 지워졌을 수 있으므로 매니페스트와 실제 파일을 다시 확인 (서버 요청 없음)</summary>
    [RelayCommand]
    private void Refresh() => _library.Rebuild();

    // ------------------------------------------------------------------ 목록

    private void OnLibraryChanged()
    {
        // 아이돌 표가 바뀌었을 때만 필터를 다시 만듦 (체크 상태·유닛 유지)
        var idols = _library.Manifest.Idols.OrderBy(kv => kv.Value.Order)
            .Select(kv => new IdolInfo { IdolId = kv.Key, IdolName = kv.Value.Name, NickName = kv.Value.NickName })
            .ToList();
        string signature = string.Join(",", idols.Select(i => i.IdolId));
        if (signature != _idolSignature)
        {
            bool first = _idolSignature.Length == 0;
            _idolSignature = signature;
            Filter.Rebuild(idols, _units,
                first ? _settings.Resource.CheckedIdolIds : Filter.CheckedIds,
                first ? _settings.Resource.SelectedUnit : Filter.SelectedUnit?.Name);
        }

        var counts = _library.Assets.GroupBy(a => a.IdolId).ToDictionary(g => g.Key, g => g.Count());
        Filter.UpdateCounts(id => counts.TryGetValue(id, out int c) ? $"({c})" : null);
        RebuildItems();
    }

    /// <summary>필터·검색어 → 구분선 + 세트 목록 (목록을 통째로 바꿔 한 번에 갱신, 선택은 같은 세트로 유지)</summary>
    private void RebuildItems()
    {
        string? selectedSource = SelectedAsset?.SourcePath;
        var words = (SearchText ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var shown = _library.Assets
            .Where(a => Filter.Allows(a.IdolId) && words.All(w => a.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var items = new List<ResourceItem>(shown.Count + 16);
        foreach (var group in shown.GroupBy(a => a.DressType))   // Assets가 의상 종류 순으로 정렬돼 있어 순서 유지
        {
            items.Add(new ResourceSeparator($"── {group.Key} ({group.Count()}) ──"));
            items.AddRange(group.Select(a => new ResourceAssetItem(a)));
        }
        Items = items;
        SummaryText = shown.Count == _library.Count ? $"세트 {_library.Count}개" : $"세트 {shown.Count} / {_library.Count}개";
        SelectedItem = selectedSource == null ? null
            : items.OfType<ResourceAssetItem>().FirstOrDefault(i => i.Asset.SourcePath == selectedSource);
    }

    // ------------------------------------------------------------------ 정보

    private void ShowInfo(SpineAsset? a)
    {
        if (a == null)
        {
            InfoText = "";
            PreviewPath = null;
            return;
        }
        string folder = _library.FolderPath(a);
        var entry = _library.Manifest.Sets.GetValueOrDefault(a.SourcePath);

        var sb = new StringBuilder();
        sb.AppendLine($"CG 키: {a.CgKey}");
        sb.AppendLine($"의상: {a.DressName}   [{a.DressType}]");
        sb.AppendLine($"아이돌: {a.IdolName}   enzaId: {a.EnzaId}");
        sb.AppendLine($"세트: {a.BaseName}   ({a.Group} / {a.SpineType})");
        sb.AppendLine($"폴더: {folder}");
        sb.AppendLine($"서버 경로: {a.SourcePath}");
        sb.AppendLine($"받은 시각: {a.DownloadedAt:yyyy-MM-dd HH:mm:ss}");
        if (entry is { Folders.Count: > 1 })
            sb.AppendLine($"저장 위치 {entry.Folders.Count}곳: {string.Join(" | ", entry.Folders)}");
        sb.AppendLine();
        sb.AppendLine("파일:");
        if (entry != null)
        {
            foreach (var (label, f) in new[] { ("json", entry.Json), ("atlas", entry.Atlas) }.Concat(entry.Images.Select(i => ("텍스처", (RemoteFile?)i))))
            {
                if (f == null) continue;
                string path = Path.Combine(folder, f.SavedName);
                string size = File.Exists(path) ? $"{new FileInfo(path).Length / 1024.0:N0} KB" : "없음!";
                string format = f.Format != null && f.Format != "png" ? $"  (원본 {f.Format} → PNG 변환)" : "";
                sb.AppendLine($"  {label,-4} {f.SavedName}  {size}{format}");
                sb.AppendLine($"        서버 수정 {f.LastModified:yyyy-MM-dd HH:mm}  ETag {f.ETag}");
            }
        }
        InfoText = sb.ToString();
        PreviewPath = a.ImageFiles.Count > 0 ? Path.Combine(folder, a.ImageFiles[0]) : null;
    }
}

/// <summary>리소스 목록 항목 (구분선 또는 세트)</summary>
public abstract class ResourceItem;

/// <summary>의상 종류 구분선 (선택 불가)</summary>
public sealed class ResourceSeparator(string title) : ResourceItem
{
    public string Title { get; } = title;
}

/// <summary>세트 1개: 의상명 · 타입 · CG 키</summary>
public sealed class ResourceAssetItem(SpineAsset asset) : ResourceItem
{
    public SpineAsset Asset { get; } = asset;
    public string Title => Asset.DressName;
    public string Detail => $"{Asset.SpineType}  ·  {Asset.CgKey}";
}
