using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SCSpineManager.Models;
using SCSpineManager.Services;

namespace SCSpineManager;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Shared;
    private CancellationTokenSource? _cts;
    private ICollectionView? _setView;
    private List<SetRow> _rows = new();                        // 추가: 현재 계획의 전체 행
    private SpineManifest _manifest = new();                   // 추가: 받은 세트 기록
    private readonly IdolFilterBar _idolFilter;                // 추가: 아이돌 체크리스트
    private CatalogSnapshot? _snapshot;                        // 추가: 현재 목록 (리소스 트리 정렬에 사용)
    private ResourceNode? _selectedResource;                   // 추가: 리소스 탭에서 선택한 항목
    private readonly List<IdolUnit> _units = IdolUnits.LoadEmbedded();   // 추가: 유닛 분류 (Resources\IdolUnits.xml)
    private readonly IdolFilterBar _resourceFilter;            // 추가: 리소스 탭 아이돌 필터

    public MainWindow()
    {
        InitializeComponent();
        SaveFolderBox.Text = _settings.SaveFolder;

        // 추가: 리소스 탭 아이돌 필터 — 바꾸면 트리를 다시 만들고 선택을 설정에 저장
        _resourceFilter = new IdolFilterBar(ResourceIdolPanel);
        _resourceFilter.Changed += () =>
        {
            RebuildResourceTree();
            _settings.ResourceCheckedIdolIds = _resourceFilter.CheckedIds;
            _settings.Save();
        };

        // 추가: 아이돌 체크리스트 — 바꾸면 목록을 다시 거르고 선택을 설정에 저장
        _idolFilter = new IdolFilterBar(IdolPanel);
        _idolFilter.Changed += () =>
        {
            _setView?.Refresh();
            _settings.CheckedIdolIds = _idolFilter.CheckedIds;
            _settings.Save();
        };

        // 추가: 매니페스트 읽기 (손상되었으면 빈 기록으로 시작 — 파일은 그대로 두고 덮어쓰지 않도록 경고)
        try { _manifest = SpineManifest.Load(_settings.MetaFolder); }
        catch (Exception ex) { Log($"매니페스트 읽기 실패 — 새 기록으로 시작합니다: {ex.Message}"); }
        LicenseBox.Text = LoadEmbeddedText("THIRD-PARTY-NOTICES.txt");

        // 저장된 스냅샷이 있으면 서버 요청 없이 바로 계획 표시
        Loaded += (_, _) => LoadSavedSnapshot();
    }

    // ---------------------------------------------------------------- 스냅샷

    private void LoadSavedSnapshot()
    {
        try
        {
            var snap = CatalogSnapshot.Load(_settings.MetaFolder);
            if (snap == null)
            {
                SnapshotText.Text = "저장된 목록 없음 — [목록 갱신]을 눌러 주세요";
                return;
            }
            ShowPlan(snap);
        }
        catch (Exception ex)
        {
            Log($"저장된 목록 읽기 실패: {ex.Message}");
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(Log);
        try
        {
            Log("목록 갱신 시작");
            using var client = new SpineCatalogClient(_settings);
            var snap = await client.FetchAsync(progress, _cts.Token);
            var previous = _snapshot;   // 추가: 비교용 이전 목록

            Directory.CreateDirectory(_settings.MetaFolder);
            snap.Save(_settings.MetaFolder);
            Log($"스냅샷 저장: {_settings.MetaFolder}");
            ShowPlan(snap);

            // 추가: 이전 목록과 달라졌으면 알림 창 (처음 받은 목록이면 비교하지 않음)
            if (previous != null)
            {
                var diff = CatalogDiff.Compare(previous, snap);
                if (diff.HasChanges)
                {
                    Log($"목록 변경: {diff.Summary}");
                    new ChangeNoticeWindow(this, diff, previous.FetchedAt).ShowDialog();
                }
                else Log("목록 변경 없음");
            }
        }
        catch (OperationCanceledException)
        {
            Log("취소됨 — 이전 스냅샷은 그대로 유지");
        }
        catch (Exception ex)
        {
            Log($"목록 갱신 실패 (이전 스냅샷 유지): {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void SetBusy(bool busy)
    {
        RefreshButton.IsEnabled = !busy;
        DownloadCheckedButton.IsEnabled = !busy;   // 변경: 체크한 아이돌 받기
        DownloadAllButton.IsEnabled = !busy;       // 추가
        CancelButton.IsEnabled = busy;
    }

    // ---------------------------------------------------------------- 다운로드 (추가)

    // 변경: 보이는 항목 → 체크한 아이돌의 세트 전부 (검색어와 무관)
    private async void DownloadCheckedButton_Click(object sender, RoutedEventArgs e) =>
        await RunDownloadAsync(_rows.Where(r => _idolFilter.Allows(r.IdolId)).ToList(), "체크한 아이돌");

    private async void DownloadAllButton_Click(object sender, RoutedEventArgs e) =>
        await RunDownloadAsync(_rows, "전체");

    private async Task RunDownloadAsync(List<SetRow> candidates, string scope)
    {
        var queue = candidates.Where(r => r.Status != SetRow.Done).ToList();
        if (queue.Count == 0)
        {
            Log($"{scope}: 받을 세트가 없습니다 (모두 완료)");
            return;
        }

        // 요청 수는 세트당 3회(텍스처 1장 기준), 간격 기준 예상 시간
        int requests = queue.Count * 3;
        var estimate = TimeSpan.FromMilliseconds((double)requests * _settings.RequestIntervalMs);
        var answer = MessageBox.Show(this,
            $"{scope} 중 받지 않은 세트 {queue.Count}개를 받습니다.\n" +
            $"요청 약 {requests:N0}회, {_settings.RequestIntervalMs}ms 간격 → 예상 {FormatTime(estimate)} 이상\n\n계속할까요?",
            "다운로드", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        SetBusy(true);
        _cts = new CancellationTokenSource();
        int done = 0, downloaded = 0, copied = 0, failed = 0;
        var started = DateTime.Now;
        DownloadProgress.Maximum = queue.Count;
        DownloadProgress.Value = 0;
        Log($"다운로드 시작 ({scope}): {queue.Count}세트");

        try
        {
            using var downloader = new SpineDownloader(_settings, _manifest, msg => Dispatcher.Invoke(() => Log(msg)));
            foreach (var row in queue)
            {
                _cts.Token.ThrowIfCancellationRequested();
                row.Status = SetRow.Working;
                try
                {
                    var result = await downloader.ProcessAsync(row.Set, _cts.Token);
                    row.Status = SetRow.Done;
                    if (result == SetResult.Downloaded) downloaded++;
                    else if (result == SetResult.CopiedOnly) copied++;
                }
                catch (RemoteFileException ex)
                {
                    row.Status = SetRow.Failed;
                    failed++;
                    Log($"실패: {row.BaseName} ({row.DressName}) — {ex.Message}");
                }
                catch (InvalidDataException ex)
                {
                    row.Status = SetRow.Failed;
                    failed++;
                    Log($"실패(텍스처 변환): {row.BaseName} ({row.DressName}) — {ex.Message}");
                }
                catch (OperationCanceledException)
                {
                    row.Status = SetRow.Waiting;
                    throw;
                }
                catch (ServerBusyException)
                {
                    row.Status = SetRow.Failed;
                    throw;
                }

                done++;
                DownloadProgress.Value = done;
                var elapsed = DateTime.Now - started;
                var remain = TimeSpan.FromTicks(elapsed.Ticks / done * (queue.Count - done));
                ProgressText.Text = $"{done}/{queue.Count} · 받음 {downloaded} · 복사만 {copied} · 실패 {failed} · 남은 시간 약 {FormatTime(remain)}";
            }
            Log($"다운로드 끝 ({scope}): 받음 {downloaded}, 복사만 {copied}, 실패 {failed}, 요청 {downloader.RequestCount}회");
        }
        catch (OperationCanceledException)
        {
            Log($"중지됨: {done}/{queue.Count} 처리 — 다음에 이어서 받습니다");
        }
        catch (ServerBusyException ex)
        {
            Log($"큐 중지: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log($"다운로드 중 오류로 중지: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
            ShowRows(_rows);         // 변경: 받은 세트가 아래로 가도록 다시 정렬 (UpdateSummaryStatus 포함)
            RebuildResourceTree();   // 추가: 받은 결과를 리소스 탭에 반영
        }
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}시간 {t.Minutes}분" : t.TotalMinutes >= 1 ? $"{t.Minutes}분 {t.Seconds}초" : $"{t.Seconds}초";

    /// <summary>완료/전체 개수를 진행 표시줄에 반영</summary>
    private void UpdateSummaryStatus()
    {
        int complete = _rows.Count(r => r.Status == SetRow.Done);
        int failed = _rows.Count(r => r.Status == SetRow.Failed);
        ProgressText.Text = $"받은 세트 {complete}/{_rows.Count}" + (failed > 0 ? $" · 실패 {failed}" : "");
        DownloadProgress.Maximum = Math.Max(1, _rows.Count);
        DownloadProgress.Value = complete;

        // 추가: 아이돌 체크박스 옆에 (받은 세트/전체 세트), 리소스 탭은 (받은 세트)
        var counts = _rows.GroupBy(r => r.IdolId)
                          .ToDictionary(g => g.Key, g => (done: g.Count(r => r.Status == SetRow.Done), total: g.Count()));
        _idolFilter.UpdateCounts(id => counts.TryGetValue(id, out var c) ? $"({c.done}/{c.total})" : null);
        _resourceFilter.UpdateCounts(id => counts.TryGetValue(id, out var c) && c.done > 0 ? $"({c.done})" : null);
    }

    // ---------------------------------------------------------------- 리소스 탭 (추가)

    /// <summary>완료된 세트로 폴더 트리를 다시 만든다 (서버 요청 없음)</summary>
    private void RebuildResourceTree()
    {
        if (_snapshot == null) return;
        var idolOrder = _snapshot.Idols.Select((idol, i) => (idol.IdolId, i)).ToDictionary(x => x.IdolId, x => x.i);
        // 변경: 리소스 탭 아이돌 필터에서 켠 아이돌만
        var doneSets = _rows.Where(r => r.Status == SetRow.Done && _resourceFilter.Allows(r.IdolId)).Select(r => r.Set);
        var root = ResourceTreeBuilder.Build(_settings.SaveFolder, doneSets, _manifest, idolOrder);
        // 변경: 최상위(SpineData)는 접을 필요가 없으므로 트리에는 의상 종류부터 넣고, 최상위는 위에 고정 표시
        ResourceTree.ItemsSource = root.Children;
        ResourceRootText.Text = root.Display;
        ResourceRootText.ToolTip = root.FullPath;
        ResourceSummaryText.Text = $"의상 종류 {root.Children.Count} · 의상 {root.Children.Sum(t => t.Children.Count)}";
        ShowResourceInfo(null);
    }

    private void ResourceRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        // 파일이 지워졌을 수 있으므로 완료 상태부터 다시 확인
        foreach (var r in _rows.Where(r => r.Status is SetRow.Done or SetRow.Waiting))
            r.Status = SpineDownloader.IsComplete(_manifest, _settings.SaveFolder, r.Set) ? SetRow.Done : SetRow.Waiting;
        ShowRows(_rows);   // 변경: 상태가 바뀐 세트의 정렬 위치도 반영
        RebuildResourceTree();
    }

    private void ResourceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        ShowResourceInfo(e.NewValue as ResourceNode);

    private void ResourceOpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedResource == null) return;
        try
        {
            // 파일이면 탐색기에서 그 파일을 선택한 상태로, 폴더면 폴더를 연다
            var args = _selectedResource.Kind == ResourceNodeKind.File
                ? $"/select,\"{_selectedResource.FullPath}\""
                : $"\"{_selectedResource.FullPath}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        }
        catch (Exception ex) { Log($"탐색기 열기 실패: {ex.Message}"); }
    }

    /// <summary>오른쪽 정보 칸 + 텍스처 미리보기</summary>
    private void ShowResourceInfo(ResourceNode? node)
    {
        _selectedResource = node;
        ResourceOpenFolderButton.IsEnabled = node != null;
        ResourcePreview.Source = null;
        if (node == null) { ResourceInfoBox.Text = ""; return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"경로: {node.FullPath}");

        switch (node.Kind)
        {
            case ResourceNodeKind.Root:
            case ResourceNodeKind.DressType:
            case ResourceNodeKind.Dress:
                sb.AppendLine(node.Display);
                break;

            case ResourceNodeKind.Set:
            case ResourceNodeKind.File:
                var set = node.Set!;
                var entry = node.Entry!;
                sb.AppendLine($"세트: {set.BaseName}   ({set.Group} / {set.SpineType})");
                sb.AppendLine($"아이돌: {set.IdolName}   enzaId: {set.EnzaId}");
                sb.AppendLine($"서버 경로: {set.SourcePath}");
                sb.AppendLine($"받은 시각: {entry.DownloadedAt:yyyy-MM-dd HH:mm:ss}");
                if (entry.Folders.Count > 1)
                    sb.AppendLine($"저장 위치 {entry.Folders.Count}곳: {string.Join(" | ", entry.Folders)}");
                sb.AppendLine();
                sb.AppendLine("파일:");
                string folder = node.Kind == ResourceNodeKind.File ? Path.GetDirectoryName(node.FullPath)! : node.FullPath;
                foreach (var (label, f) in new[] { ("json", entry.Json), ("atlas", entry.Atlas) }.Concat(entry.Images.Select(i => ("텍스처", (RemoteFile?)i))))
                {
                    if (f == null) continue;
                    string path = Path.Combine(folder, f.SavedName);
                    string size = File.Exists(path) ? $"{new FileInfo(path).Length / 1024.0:N0} KB" : "없음!";
                    string format = f.Format != null && f.Format != "png" ? $"  (원본 {f.Format} → PNG 변환)" : "";
                    sb.AppendLine($"  {label,-4} {f.SavedName}  {size}{format}");
                    sb.AppendLine($"        서버 수정 {f.LastModified:yyyy-MM-dd HH:mm}  ETag {f.ETag}");
                }
                ShowPreview(node.Kind == ResourceNodeKind.File && node.Name.EndsWith(SpineNaming.ImageExt, StringComparison.OrdinalIgnoreCase)
                    ? node.FullPath
                    : Path.Combine(folder, entry.Images.FirstOrDefault()?.SavedName ?? ""));
                break;
        }
        ResourceInfoBox.Text = sb.ToString();
    }

    /// <summary>텍스처 미리보기 (파일을 잠그지 않도록 메모리로 읽음)</summary>
    private void ShowPreview(string path)
    {
        try
        {
            if (!File.Exists(path) || !path.EndsWith(SpineNaming.ImageExt, StringComparison.OrdinalIgnoreCase)) return;
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit();
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(path);
            img.EndInit();
            img.Freeze();
            ResourcePreview.Source = img;
        }
        catch { /* 미리보기 실패는 무시 */ }
    }

    // ---------------------------------------------------------------- 계획 표시

    private void ShowPlan(CatalogSnapshot snap)
    {
        var plan = SpinePlanner.Build(snap);

        // 추가: 아이돌 체크리스트 다시 만들기 (체크 상태는 설정에서 복원)
        _snapshot = snap;
        // 처음 표시할 때는 설정값, 목록을 갱신할 때는 지금 화면의 체크 상태를 유지
        // 변경: 유닛 분류(_units)를 넘겨 유닛 → 멤버 형태로 생성, 리소스 탭 필터도 같이 생성
        bool first = _rows.Count == 0;
        _idolFilter.Rebuild(snap.Idols, _units, first ? _settings.CheckedIdolIds : _idolFilter.CheckedIds);
        _resourceFilter.Rebuild(snap.Idols, _units, first ? _settings.ResourceCheckedIdolIds : _resourceFilter.CheckedIds);

        // 변경: 매니페스트 기준으로 완료/대기 상태를 넣고, 정렬·표시는 ShowRows에서
        var rows = plan.Sets
            .Select(s => new SetRow(s) { Status = SpineDownloader.IsComplete(_manifest, _settings.SaveFolder, s) ? SetRow.Done : SetRow.Waiting })
            .ToList();
        ShowRows(rows);

        var skipped = plan.Skipped
            .Concat(plan.Conflicts.Select(c => new SkippedEntry(0, "", "", c, "이름 충돌")))
            .ToList();
        SkippedGrid.ItemsSource = skipped;
        SkippedExpander.Header = $"건너뛴 항목 {plan.Skipped.Count}개 / 이름 충돌 {plan.Conflicts.Count}개";

        var groups = plan.Sets.GroupBy(s => s.Group).Select(g => $"{g.Key} {g.Count()}");
        SummaryText.Text =
            $"아이돌 {snap.Idols.Count}명 · 의상 {plan.DressCount}벌 → 받을 세트 {plan.Sets.Count}개 (파일 {plan.FileCount}개), " +
            $"로컬 복사 {plan.CopyCount}세트 · 그룹: {string.Join(", ", groups)}";
        SnapshotText.Text = $"목록 기준: {snap.FetchedAt:yyyy-MM-dd HH:mm}";

        foreach (var g in plan.Sets.Select(s => s.Group).Distinct().Where(g => !SpineNaming.IsKnownGroup(g)))
            Log($"주의: 처음 보는 그룹 '{g}' — 접두어 '{SpineNaming.GroupPrefix(g)}'로 임시 처리");
        if (plan.Conflicts.Count > 0)
            Log($"주의: 이름 충돌 {plan.Conflicts.Count}건 — 명명 규칙 점검 필요");

        RebuildResourceTree();   // 추가
    }

    /// <summary>
    /// 추가: 목록 정렬·표시. 받지 않은 세트(대기·실패)를 맨 위에, 그 안에서는 아이돌 목록 순 → 의상 종류 → 의상명 → 파일명.
    /// 다운로드 중에는 행이 움직이지 않도록 다시 정렬하지 않고, 큐가 끝난 뒤 다시 호출한다.
    /// </summary>
    private void ShowRows(List<SetRow> rows)
    {
        var order = _snapshot?.Idols.Select((idol, i) => (idol.IdolId, i)).ToDictionary(x => x.IdolId, x => x.i) ?? new();
        _rows = rows
            .OrderBy(r => r.Status == SetRow.Done ? 1 : 0)
            .ThenBy(r => order.GetValueOrDefault(r.IdolId, int.MaxValue))
            .ThenBy(r => r.DressTypeOrder).ThenBy(r => r.DressName).ThenBy(r => r.BaseName)
            .ToList();
        UpdateSummaryStatus();
        _setView = CollectionViewSource.GetDefaultView(_rows);
        _setView.Filter = FilterRow;
        SetGrid.ItemsSource = _setView;
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => _setView?.Refresh();

    private bool FilterRow(object o)
    {
        if (o is not SetRow r) return true;
        if (!_idolFilter.Allows(r.IdolId)) return false;   // 추가: 체크한 아이돌만 표시
        string text = FilterBox.Text.Trim();
        if (text.Length == 0) return true;
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .All(w => r.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- 기타

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_settings.SaveFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", _settings.SaveFolder) { UseShellExecute = true });
        }
        catch (Exception ex) { Log($"폴더 열기 실패: {ex.Message}"); }
    }

    private void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();

        // 추가: 오래 걸리는 다운로드를 나중에 확인할 수 있도록 파일에도 남김 (_meta\log.txt)
        try
        {
            Directory.CreateDirectory(_settings.MetaFolder);
            File.AppendAllText(Path.Combine(_settings.MetaFolder, "log.txt"), $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}");
        }
        catch { /* 로그 파일 실패는 무시 */ }
    }

    private static string LoadEmbeddedText(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>목록 1행 = 받을 세트 1개 (의상 정보는 첫 저장 위치 기준)</summary>
    public sealed class SetRow : INotifyPropertyChanged   // 변경: 상태 표시 갱신을 위해 INotifyPropertyChanged
    {
        // 추가: 상태 문자열
        public const string Waiting = "대기";
        public const string Working = "받는 중";
        public const string Done = "완료";
        public const string Failed = "실패";

        public event PropertyChangedEventHandler? PropertyChanged;

        private string _status = Waiting;
        public string Status
        {
            get => _status;
            set { if (_status == value) return; _status = value; PropertyChanged?.Invoke(this, new(nameof(Status))); }
        }

        /// <summary>추가: 원본 세트 (다운로드에 사용)</summary>
        public SpineSet Set { get; }

        public SetRow(SpineSet s)
        {
            Set = s;   // 추가
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
        }

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
}
