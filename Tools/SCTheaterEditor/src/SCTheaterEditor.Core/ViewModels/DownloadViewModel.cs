using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCTheaterEditor.Models;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 다운로드 탭: 목록 갱신(서버) → 다운로드 계획 → 받을 세트 목록 → 체크한 아이돌/전체 받기.
/// 2026-10-12 MVVM 전환 (기능은 이전 MainWindow 코드와 같음)
/// - 목록을 읽을 때 매니페스트 v1 → v2 이전(아이돌·의상 정보 채움)과 아이돌 표 갱신도 여기서 처리
/// - 받은 세트는 바로 SpineLibrary에 반영 → 리소스 탭이 따라 갱신됨
/// </summary>
public sealed partial class DownloadViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SpineLibrary _library;
    private readonly IReadOnlyList<IdolUnit> _units;
    private readonly IDialogService _dialogs;
    private readonly Func<ICatalogSource> _catalogFactory;
    private readonly Func<ISetDownloader> _downloaderFactory;
    private readonly Action _saveSettings;
    private readonly AppLog _log;

    private List<SetRowViewModel> _rows = new();
    private CancellationTokenSource? _cts;

    public DownloadViewModel(AppSettings settings, SpineLibrary library, IReadOnlyList<IdolUnit> units, IDialogService dialogs,
                             Func<ICatalogSource> catalogFactory, Func<ISetDownloader> downloaderFactory, Action saveSettings, AppLog log)
    {
        _settings = settings;
        _library = library;
        _units = units;
        _dialogs = dialogs;
        _catalogFactory = catalogFactory;
        _downloaderFactory = downloaderFactory;
        _saveSettings = saveSettings;
        _log = log;

        VisibleRows = [];
        SelectedRows = [];   // 추가
        Skipped = [];
        SkippedHeader = "건너뛴 항목 / 충돌";
        SummaryText = "";
        SnapshotText = "";
        ProgressText = "";
        FilterText = "";
        ProgressMax = 1;

        Filter.Changed += () =>
        {
            ApplyFilter();
            _settings.Download.CheckedIdolIds = Filter.CheckedIds;
            _saveSettings();
        };
        Filter.SelectedUnitChanged += () =>
        {
            _settings.Download.SelectedUnit = Filter.SelectedUnit?.Name;
            _saveSettings();
        };
    }

    public IdolFilterViewModel Filter { get; } = new();

    public string SaveFolder => _settings.SaveFolder;

    /// <summary>지금 목록 스냅샷 (없으면 null)</summary>
    public CatalogSnapshot? Snapshot { get; private set; }

    [ObservableProperty] public partial IReadOnlyList<SetRowViewModel> VisibleRows { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<SkippedEntry> Skipped { get; private set; }
    [ObservableProperty] public partial string SkippedHeader { get; private set; }
    [ObservableProperty] public partial string SummaryText { get; private set; }
    [ObservableProperty] public partial string SnapshotText { get; private set; }
    [ObservableProperty] public partial string ProgressText { get; private set; }
    [ObservableProperty] public partial double ProgressValue { get; private set; }
    [ObservableProperty] public partial double ProgressMax { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(DownloadCheckedCommand), nameof(DownloadSelectedCommand), nameof(DownloadAllCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    /// <summary>검색어 (아이돌·의상명·파일명·경로, 공백으로 여러 단어)</summary>
    [ObservableProperty] public partial string FilterText { get; set; }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    // ------------------------------------------------------------------ 스냅샷

    /// <summary>저장된 스냅샷이 있으면 서버 요청 없이 계획 표시 (시작할 때)</summary>
    public void LoadSavedSnapshot()
    {
        try
        {
            var snap = CatalogSnapshot.Load(_settings.MetaFolder);
            if (snap == null)
            {
                SnapshotText = "저장된 목록 없음 — [목록 갱신]을 눌러 주세요";
                return;
            }
            ShowPlan(snap);
        }
        catch (Exception ex)
        {
            _log.Error("저장된 목록 읽기 실패", ex);
        }
    }

    private bool CanStart() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            _log.Info("목록 갱신 시작");
            using var client = _catalogFactory();
            var snap = await client.FetchAsync(new Progress<string>(_log.Info), _cts.Token);
            var previous = Snapshot;

            snap.Save(_settings.MetaFolder);
            _log.Info($"스냅샷 저장: {_settings.MetaFolder}");
            ShowPlan(snap);

            // 이전 목록과 달라졌으면 알림 창 (처음 받은 목록이면 비교하지 않음)
            if (previous != null)
            {
                var diff = CatalogDiff.Compare(previous, snap);
                if (diff.HasChanges)
                {
                    _log.Info($"목록 변경: {diff.Summary}");
                    _dialogs.ShowChangeNotice(diff, previous.FetchedAt);
                }
                else _log.Info("목록 변경 없음");
            }
        }
        catch (OperationCanceledException)
        {
            _log.Info("취소됨 — 이전 스냅샷은 그대로 유지");
        }
        catch (Exception ex)
        {
            _log.Error("목록 갱신 실패 (이전 스냅샷 유지)", ex);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.SaveFolder);
            _dialogs.OpenInExplorer(_settings.SaveFolder);
        }
        catch (Exception ex) { _log.Error("폴더 열기 실패", ex); }
    }

    // ------------------------------------------------------------------ 계획 표시

    private void ShowPlan(CatalogSnapshot snap)
    {
        var plan = SpinePlanner.Build(snap);
        Snapshot = snap;
        UpdateManifestInfo(snap, plan);

        // 처음 표시할 때는 설정값, 목록을 갱신할 때는 지금 화면의 체크 상태·유닛 유지
        bool first = _rows.Count == 0;
        Filter.Rebuild(snap.Idols, _units,
            first ? _settings.Download.CheckedIdolIds : Filter.CheckedIds,
            first ? _settings.Download.SelectedUnit : Filter.SelectedUnit?.Name);

        _rows = plan.Sets.Select(s => new SetRowViewModel(s)
        {
            Status = SpineDownloader.IsComplete(_library.Manifest, _settings.SaveFolder, s) ? SetRowViewModel.Done : SetRowViewModel.Waiting,
        }).ToList();
        SortRows();

        Skipped = plan.Skipped.Concat(plan.Conflicts.Select(c => new SkippedEntry(0, "", "", c, "이름 충돌"))).ToList();
        SkippedHeader = $"건너뛴 항목 {plan.Skipped.Count}개 / 이름 충돌 {plan.Conflicts.Count}개";

        var groups = plan.Sets.GroupBy(s => s.Group).Select(g => $"{g.Key} {g.Count()}");
        SummaryText = $"아이돌 {snap.Idols.Count}명 · 의상 {plan.DressCount}벌 → 받을 세트 {plan.Sets.Count}개 (파일 {plan.FileCount}개), " +
                      $"로컬 복사 {plan.CopyCount}세트 · 그룹: {string.Join(", ", groups)}";
        SnapshotText = $"목록 기준: {snap.FetchedAt:yyyy-MM-dd HH:mm}";

        foreach (var g in plan.Sets.Select(s => s.Group).Distinct().Where(g => !SpineNaming.IsKnownGroup(g)))
            _log.Warn($"처음 보는 그룹 '{g}' — 접두어 '{SpineNaming.GroupPrefix(g)}'로 임시 처리");
        if (plan.Conflicts.Count > 0)
            _log.Warn($"이름 충돌 {plan.Conflicts.Count}건 — 명명 규칙 점검 필요");
    }

    /// <summary>매니페스트 v2: 아이돌 표 갱신 + 비어 있는 세트 정보 채움(v1 이전) → 저장 후 색인 다시 만들기</summary>
    private void UpdateManifestInfo(CatalogSnapshot snap, SpinePlan plan)
    {
        var manifest = _library.Manifest;
        bool wasOld = manifest.Version < SpineManifest.CurrentVersion;
        bool idolsChanged = manifest.UpdateIdols(snap.Idols);
        int filled = manifest.FillFromCatalog(snap, plan);
        if (!wasOld && !idolsChanged && filled == 0) return;

        try
        {
            manifest.Save(_settings.MetaFolder);
            if (wasOld) _log.Info($"매니페스트 v2로 이전: 세트 {filled}개에 아이돌·의상 정보 기록");
        }
        catch (Exception ex) { _log.Error("매니페스트 저장 실패", ex); }
        _library.Rebuild();
    }

    /// <summary>받지 않은 세트(대기·실패)를 맨 위에, 그 안에서는 아이돌 목록 순 → 의상 종류 → 의상명 → 파일명</summary>
    private void SortRows()
    {
        var order = Snapshot?.Idols.Select((idol, i) => (idol.IdolId, i)).ToDictionary(x => x.IdolId, x => x.i) ?? new();
        _rows = _rows
            .OrderBy(r => r.Status == SetRowViewModel.Done ? 1 : 0)
            .ThenBy(r => order.GetValueOrDefault(r.IdolId, int.MaxValue))
            .ThenBy(r => r.DressTypeOrder).ThenBy(r => r.DressName, StringComparer.Ordinal).ThenBy(r => r.BaseName, StringComparer.Ordinal)
            .ToList();
        UpdateSummaryStatus();
        ApplyFilter();
    }

    /// <summary>아이돌 필터 + 검색어 → 보이는 행 (목록을 통째로 바꿔 한 번에 갱신)</summary>
    private void ApplyFilter()
    {
        var words = (FilterText ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        VisibleRows = _rows
            .Where(r => Filter.Allows(r.IdolId) && words.All(w => r.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>완료/전체 개수와 아이돌별 (받은/전체) 표시</summary>
    private void UpdateSummaryStatus()
    {
        int complete = _rows.Count(r => r.Status == SetRowViewModel.Done);
        int failed = _rows.Count(r => r.Status == SetRowViewModel.Failed);
        ProgressText = $"받은 세트 {complete}/{_rows.Count}" + (failed > 0 ? $" · 실패 {failed}" : "");
        ProgressMax = Math.Max(1, _rows.Count);
        ProgressValue = complete;

        var counts = _rows.GroupBy(r => r.IdolId)
                          .ToDictionary(g => g.Key, g => (done: g.Count(r => r.Status == SetRowViewModel.Done), total: g.Count()));
        Filter.UpdateCounts(id => counts.TryGetValue(id, out var c) ? $"({c.done}/{c.total})" : null);
    }

    // ------------------------------------------------------------------ 다운로드

    /// <summary>체크한 아이돌의 세트 전부 (검색어와 무관)</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task DownloadCheckedAsync() => RunDownloadAsync(_rows.Where(r => Filter.Allows(r.IdolId)).ToList(), "체크한 아이돌");

    /// <summary>추가: 목록에서 선택한 행 (DataGrid 선택은 화면이 넘겨 줌 — Ctrl·Shift로 여러 개, Ctrl+A로 전부)</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadSelectedCommand))]
    public partial IReadOnlyList<SetRowViewModel> SelectedRows { get; set; }

    private bool CanDownloadSelected() => !IsBusy && SelectedRows.Count > 0;

    /// <summary>추가: 선택한 의상만 받기</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadSelected))]
    private Task DownloadSelectedAsync() => RunDownloadAsync(SelectedRows.ToList(), "선택한 의상");

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task DownloadAllAsync() => RunDownloadAsync(_rows, "전체");

    private async Task RunDownloadAsync(List<SetRowViewModel> candidates, string scope)
    {
        var queue = candidates.Where(r => r.Status != SetRowViewModel.Done).ToList();
        if (queue.Count == 0)
        {
            _log.Info($"{scope}: 받을 세트가 없습니다 (모두 완료)");
            return;
        }

        // 요청 수는 세트당 3회(텍스처 1장 기준), 간격 기준 예상 시간
        int requests = queue.Count * 3;
        var estimate = TimeSpan.FromMilliseconds((double)requests * _settings.Download.RequestIntervalMs);
        if (!_dialogs.Confirm("다운로드",
                $"{scope} 중 받지 않은 세트 {queue.Count}개를 받습니다.\n" +
                $"요청 약 {requests:N0}회, {_settings.Download.RequestIntervalMs}ms 간격 → 예상 {FormatTime(estimate)} 이상\n\n계속할까요?"))
            return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        int done = 0, downloaded = 0, copied = 0, failed = 0;
        var started = DateTime.Now;
        ProgressMax = queue.Count;
        ProgressValue = 0;
        _log.Info($"다운로드 시작 ({scope}): {queue.Count}세트");

        try
        {
            using var downloader = _downloaderFactory();
            try
            {
                foreach (var row in queue)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    row.Status = SetRowViewModel.Working;
                    try
                    {
                        var result = await downloader.ProcessAsync(row.Set, _cts.Token);
                        row.Status = SetRowViewModel.Done;
                        if (result == SetResult.Downloaded) downloaded++;
                        else if (result == SetResult.CopiedOnly) copied++;
                        _library.Refresh(row.Set.SourcePath);   // 추가: 리소스 탭에 바로 반영
                    }
                    catch (RemoteFileException ex)
                    {
                        row.Status = SetRowViewModel.Failed;
                        failed++;
                        _log.Warn($"실패: {row.BaseName} ({row.DressName}) — {ex.Message}");
                    }
                    catch (InvalidDataException ex)
                    {
                        row.Status = SetRowViewModel.Failed;
                        failed++;
                        _log.Warn($"실패(텍스처 변환): {row.BaseName} ({row.DressName}) — {ex.Message}");
                    }
                    catch (OperationCanceledException)
                    {
                        row.Status = SetRowViewModel.Waiting;
                        throw;
                    }
                    catch (ServerBusyException)
                    {
                        row.Status = SetRowViewModel.Failed;
                        throw;
                    }

                    done++;
                    ProgressValue = done;
                    var elapsed = DateTime.Now - started;
                    var remain = TimeSpan.FromTicks(elapsed.Ticks / done * (queue.Count - done));
                    ProgressText = $"{done}/{queue.Count} · 받음 {downloaded} · 복사만 {copied} · 실패 {failed} · 남은 시간 약 {FormatTime(remain)}";
                }
                _log.Info($"다운로드 끝 ({scope}): 받음 {downloaded}, 복사만 {copied}, 실패 {failed}, 요청 {downloader.RequestCount}회");
            }
            catch (OperationCanceledException)
            {
                _log.Info($"중지됨: {done}/{queue.Count} 처리 — 다음에 이어서 받습니다");
            }
            catch (ServerBusyException ex)
            {
                _log.Warn($"큐 중지: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _log.Error("다운로드 중 오류로 중지", ex);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
            SortRows();   // 받은 세트가 아래로 가도록 다시 정렬
        }
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}시간 {t.Minutes}분" : t.TotalMinutes >= 1 ? $"{t.Minutes}분 {t.Seconds}초" : $"{t.Seconds}초";
}

/// <summary>목록 1행 = 받을 세트 1개 (의상 정보는 첫 저장 위치 기준)</summary>
public sealed partial class SetRowViewModel : ObservableObject
{
    public const string Waiting = "대기";
    public const string Working = "받는 중";
    public const string Done = "완료";
    public const string Failed = "실패";

    public SetRowViewModel(SpineSet s)
    {
        Set = s;
        var t = s.Targets[0];
        IdolId = s.IdolId;
        IdolName = s.IdolName;
        DressType = t.DressType;
        DressTypeOrder = t.DressTypeOrder;
        DressName = s.Targets.Count > 1 ? string.Join(" / ", s.Targets.Select(x => x.DressName)) : t.DressName;
        Group = s.Group;
        BaseName = s.BaseName;
        CopyText = s.Targets.Count > 1 ? $"+{s.Targets.Count - 1}" : "";
        SourcePath = s.SourcePath;
        SearchText = $"{IdolName} {DressType} {DressName} {Group} {BaseName} {SourcePath}";
        Status = Waiting;
    }

    public SpineSet Set { get; }

    [ObservableProperty] public partial string Status { get; set; }

    public int IdolId { get; }
    public string IdolName { get; }
    public string DressType { get; }
    public int DressTypeOrder { get; }
    public string DressName { get; }
    public string Group { get; }
    public string BaseName { get; }
    public string CopyText { get; }
    public string SourcePath { get; }
    public string SearchText { get; }
}
