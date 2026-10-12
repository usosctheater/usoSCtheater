using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SCTheaterEditor.Models;
using SCTheaterEditor.Services;

namespace SCTheaterEditor;

/// <summary>
/// 뷰어 탭 (MainWindow의 일부). 렌더링·재생은 Viewer\viewer.js, 여기서는 선택 UI와 상태 표시.
/// 트랙·Animation 문자열 규칙은 Services/AnimationScript (CGManager와 동일).
/// 재생 방식
/// - 게임 방식(기본): 고른 애니메이션은 접두어 기준 트랙에 들어가고, 바뀔 때마다 전체 초기화 후 문자열 전체를 다시 재생 (CGManager.PlayAnimation과 동일)
/// - 디버그 방식(설정 체크): 선택한 트랙만 교체, 나머지 트랙은 계속 재생 (SpineDebugScene과 동일)
/// 립 재생(게임 방식에서만): 재생할 때마다 CGManager.LipSyncCoroutine과 같은 규칙으로 말하는 중/멈춤 립을 정해 lip 트랙에 건다
/// (계속 말함 = 보이스가 끝나지 않는 경우, N초 = 보이스 길이가 N초인 경우)
/// </summary>
public partial class MainWindow
{
    private ViewerHost? _viewer;                // 뷰어 탭 WebView2 연결
    private Task? _viewerInit;                  // 뷰어 초기화 (처음 열 때 1회)

    /// <summary>불러온 세트의 애니메이션 (이름, 길이, loop_start 시간 또는 -1)</summary>
    private List<(string name, double duration, double loopStart)> _viewerAnims = new();
    /// <summary>트랙 0~3에 걸린 애니메이션 (없으면 null)</summary>
    private readonly string?[] _viewerTracks = new string?[AnimationScript.TrackCount];
    private bool _viewerLoaded;
    private bool _suppressSkinEvent;            // 추가: 불러올 때 스킨 목록을 채우는 동안 변경 이벤트 무시
    private bool _viewerPaused;                 // 추가: 일시정지 상태
    private bool _suppressSliderEvent;          // 추가: 재생 중 진행률로 드래그 바를 옮길 때 이벤트 무시
    private string _viewerIdolKey = "";         // 추가: 립 기본값 조회용 아이돌 키 (= idollist nickName)
    private readonly LipRules _lipRules = LipRules.LoadEmbedded();   // 변경: exe에 포함된 DefaultLipData.xml 복사본
    private bool _viewerSettingsShown;          // 추가: 설정값을 화면에 채우는 중에는 변경 이벤트 무시

    // 트랙별 우선 표시 접두어 (SpineDebugManager와 동일: 0은 알려진 접두어가 없는 것, 3은 arm_ 다음 eye_)
    private static readonly string[] KnownPrefixes = { "face_", "lip_", "arm_", "eye_" };
    private static readonly Dictionary<int, string[]> TrackPriorityPrefixes = new()
    {
        { 1, new[] { "face_" } },
        { 2, new[] { "lip_" } },
        { 3, new[] { "arm_", "eye_" } },
    };

    private int SelectedTrack =>
        ViewerTrack1.IsChecked == true ? 1 : ViewerTrack2.IsChecked == true ? 2 : ViewerTrack3.IsChecked == true ? 3 : 0;

    // ---------------------------------------------------------------- 초기화·불러오기

    /// <summary>뷰어 초기화는 처음 열 때 1회 (WebView2 런타임이 없으면 여기서 실패)</summary>
    private Task EnsureViewerAsync() => _viewerInit ??= InitViewerAsync();

    /// <summary>뷰어 탭 화면이 처음 만들어질 때 설정값 표시</summary>
    private void ViewerPanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (_viewerSettingsShown) return;
        ViewerSingleTrackCheck.IsChecked = _settings.Viewer.SingleTrackMode;
        // 추가: 립 재생 설정
        ViewerLipCheck.IsChecked = _settings.Viewer.LipEnabled;
        ViewerLipAlwaysRadio.IsChecked = _settings.Viewer.LipAlways;
        ViewerLipSecondsRadio.IsChecked = !_settings.Viewer.LipAlways;
        ViewerLipSecondsBox.Text = _settings.Viewer.LipSeconds.ToString("0.##");
        _viewerSettingsShown = true;
        UpdateLipControls();
    }

    private async Task InitViewerAsync()
    {
        _viewer = new ViewerHost(ViewerWeb);
        _viewer.MessageReceived += OnViewerMessage;
        await _viewer.InitializeAsync(_settings.SaveFolder);
    }

    /// <summary>리소스 탭의 세트(또는 그 세트의 파일)를 뷰어 탭에서 연다</summary>
    private async void OpenInViewer(ResourceNode? node)
    {
        if (node?.Set == null || node.Entry?.Json == null || node.Entry.Atlas == null) return;
        string folder = node.Kind == ResourceNodeKind.File ? Path.GetDirectoryName(node.FullPath)! : node.FullPath;
        string relative = Path.GetRelativePath(_settings.SaveFolder, folder);
        string title = $"{node.Set.IdolName} · {Path.GetFileName(folder)} · {node.Set.BaseName}";

        // 추가: 립 기본값 조회용 아이돌 키 (= idollist nickName)
        _viewerIdolKey = _snapshot?.Idols.FirstOrDefault(i => i.IdolId == node.Set.IdolId)?.NickName ?? "";

        MainTabs.SelectedItem = ViewerTab;
        ViewerTitleText.Text = title;
        ViewerInfoText.Text = "불러오는 중…";
        _viewerLoaded = false;
        try
        {
            await EnsureViewerAsync();
            _viewer!.Post(new
            {
                type = "load",
                folderUrl = ViewerHost.FolderUrl(relative),
                json = node.Entry.Json.SavedName,
                atlas = node.Entry.Atlas.SavedName,
                title,
            });
        }
        catch (Exception ex)
        {
            _viewerInit = null;   // 다음에 다시 시도할 수 있도록
            ViewerInfoText.Text = $"뷰어를 시작하지 못했습니다: {ex.Message}\n(WebView2 런타임 설치 여부를 확인하세요)";
            LogError("뷰어 시작 실패", ex);
        }
    }

    /// <summary>뷰어 페이지에서 온 메시지</summary>
    private void OnViewerMessage(JsonElement msg)
    {
        string type = msg.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        switch (type)
        {
            case "loaded": OnViewerLoaded(msg); break;
            case "state": ShowTrackState(msg); break;   // 변경: 진행률·일시정지·립 상태도 함께
            case "bones": ShowBones(msg.GetProperty("bones")); break;   // 추가
            case "key": HandleViewerKey(msg.GetProperty("key").GetString()); break;
            case "error":
                ViewerInfoText.Text = $"불러오기 실패: {msg.GetProperty("message").GetString()}";
                LogWarn($"뷰어 불러오기 실패: {msg.GetProperty("message").GetString()}");
                break;
        }
    }

    private void OnViewerLoaded(JsonElement msg)
    {
        _viewerAnims = msg.GetProperty("animations").EnumerateArray()
            .Select(a => (a.GetProperty("name").GetString() ?? "", a.GetProperty("duration").GetDouble(), a.GetProperty("loopStart").GetDouble()))
            .ToList();
        _viewerLoaded = true;

        // 추가: 스킨 목록 (viewer.js가 고른 스킨을 선택 표시)
        _suppressSkinEvent = true;
        ViewerSkinCombo.ItemsSource = msg.GetProperty("skins").EnumerateArray().Select(s => s.GetString()).ToList();
        ViewerSkinCombo.SelectedItem = msg.GetProperty("skin").GetString();
        ViewerSkinCombo.IsEnabled = ViewerSkinCombo.Items.Count > 1;
        _suppressSkinEvent = false;

        ViewerInfoText.Text =
            $"Spine {msg.GetProperty("version").GetString()} · 본 {msg.GetProperty("bones").GetInt32()}개 · 애니메이션 {_viewerAnims.Count}개 · " +
            $"{msg.GetProperty("width").GetDouble():0} x {msg.GetProperty("height").GetDouble():0}";

        // 처음에는 wait가 있으면 기본 트랙에 재생 (게임에서 가장 흔한 시작 상태)
        Array.Clear(_viewerTracks);
        if (_viewerAnims.Any(a => a.name == "wait")) _viewerTracks[0] = "wait";
        ViewerTrack0.IsChecked = true;
        PlayAllTracks();
        RefreshAnimList();
        ViewerBoneList.ItemsSource = null;   // 추가
    }

    // ---------------------------------------------------------------- 애니메이션 목록

    private static bool IsPriority(string name, int track)
    {
        if (track == 0) return !KnownPrefixes.Any(p => name.StartsWith(p));
        return TrackPriorityPrefixes.TryGetValue(track, out var ps) && ps.Any(p => name.StartsWith(p));
    }

    private static int PriorityOrder(string name, int track) =>
        TrackPriorityPrefixes.TryGetValue(track, out var ps) ? Math.Max(0, Array.FindIndex(ps, p => name.StartsWith(p))) : 0;

    /// <summary>선택한 트랙 기준으로 목록 재구성: None → 우선 그룹 → 구분선 → 나머지</summary>
    private void RefreshAnimList()
    {
        if (ViewerAnimList == null) return;   // XAML 초기화 중 (라디오 버튼 기본 체크)
        ViewerAnimList.Items.Clear();
        if (!_viewerLoaded) return;
        int track = SelectedTrack;

        ViewerAnimList.Items.Add(new ListBoxItem { Content = "(None) — 이 트랙 비우기", Tag = null, Foreground = Brushes.Gray });
        var priority = _viewerAnims.Where(a => IsPriority(a.name, track)).OrderBy(a => PriorityOrder(a.name, track)).ToList();
        var rest = _viewerAnims.Where(a => !IsPriority(a.name, track)).ToList();

        foreach (var a in priority) ViewerAnimList.Items.Add(MakeAnimItem(a));
        if (rest.Count > 0)
            ViewerAnimList.Items.Add(new ListBoxItem { Content = "──── 그 밖의 애니메이션 ────", IsEnabled = false, Focusable = false });
        foreach (var a in rest) ViewerAnimList.Items.Add(MakeAnimItem(a));

        // 지금 이 트랙에 걸린 애니메이션을 선택 표시
        ViewerAnimList.SelectedItem = ViewerAnimList.Items.OfType<ListBoxItem>()
            .FirstOrDefault(i => i.IsEnabled && Equals(i.Tag, _viewerTracks[track]));
        ViewerAnimList.ScrollIntoView(ViewerAnimList.SelectedItem ?? ViewerAnimList.Items[0]);
    }

    private static ListBoxItem MakeAnimItem((string name, double duration, double loopStart) a)
    {
        string mark = a.loopStart >= 0 ? $"  ↻ {a.loopStart:0.00}s부터 반복" : AnimationScript.ForceLoopAnims.Contains(a.name) ? "  ∞ 반복" : "";
        return new ListBoxItem { Content = $"{a.name}  ({a.duration:0.00}s){mark}", Tag = a.name };
    }

    private void ViewerTrack_Checked(object sender, RoutedEventArgs e) => RefreshAnimList();

    /// <summary>항목 클릭(같은 항목을 다시 눌러도) → 적용</summary>
    private void ViewerAnimList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ViewerAnimList, (DependencyObject)e.OriginalSource) is ListBoxItem item && item.IsEnabled)
            ApplyAnim(item.Tag as string);
    }

    private void ViewerAnimList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewerAnimList.SelectedItem is ListBoxItem item && item.IsEnabled)
        {
            ApplyAnim(item.Tag as string);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- 재생

    /// <summary>목록에서 고른 애니메이션 적용 (null = 선택한 트랙 비우기)</summary>
    private void ApplyAnim(string? name)
    {
        if (!_viewerLoaded || _viewer == null) return;
        int selected = SelectedTrack;

        if (ViewerSingleTrackCheck.IsChecked == true)
        {
            // 디버그 방식: 선택한 트랙에만 건다 (접두어와 달라도 허용)
            _viewerTracks[selected] = name;
            _viewer.Post(new { type = "setTrack", track = selected, anim = name });
        }
        else
        {
            // 게임 방식: 접두어 기준 트랙에 들어가고 전체를 다시 재생
            int target = name == null ? selected : AnimationScript.TrackIndex(name);
            _viewerTracks[target] = name;
            PlayAllTracks();
            if (target != selected)
                ViewerInfoText.Text = $"{name}은(는) 접두어 기준으로 트랙 {target}({AnimationScript.TrackLabels[target]})에 들어갑니다 (게임과 동일)";
        }
    }

    /// <summary>게임 방식 재생: 트랙 0→3 순서의 문자열 전체 (재생 전 전체 초기화) + 립 재생</summary>
    private void PlayAllTracks()
    {
        var anims = _viewerTracks.Where(a => a != null).Select(a => a!).ToArray();
        _viewer?.Post(new { type = "play", anims, lip = BuildLip(anims) });
    }

    /// <summary>추가: 립 재생 정보 (꺼져 있거나 디버그 방식이면 null)</summary>
    private object? BuildLip(IReadOnlyList<string> anims)
    {
        if (ViewerLipCheck.IsChecked != true || ViewerSingleTrackCheck.IsChecked == true) return null;
        var names = _viewerAnims.Select(a => a.name).ToHashSet();
        var (moving, still) = _lipRules.Resolve(anims, _viewerIdolKey, names.Contains);
        if (moving == null) return null;
        double seconds = ViewerLipAlwaysRadio.IsChecked == true ? -1 : _settings.Viewer.LipSeconds;
        return new { moving, still, seconds };
    }

    private void ViewerResetButton_Click(object sender, RoutedEventArgs e) => ResetViewerTracks();

    private void ResetViewerTracks()
    {
        if (!_viewerLoaded) return;
        Array.Clear(_viewerTracks);
        _viewer?.Post(new { type = "reset" });
        RefreshAnimList();
    }

    /// <summary>추가: 스킨 변경 — 게임 방식이면 지금 문자열을 처음부터 다시 재생 (부착물이 새 스킨 기준으로 바뀌므로)</summary>
    private void ViewerSkinCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSkinEvent || !_viewerLoaded || ViewerSkinCombo.SelectedItem is not string skin) return;
        _viewer?.Post(new { type = "skin", name = skin });
        if (ViewerSingleTrackCheck.IsChecked != true) PlayAllTracks();
    }

    private void ViewerSingleTrackCheck_Changed(object sender, RoutedEventArgs e)
    {
        _settings.Viewer.SingleTrackMode = ViewerSingleTrackCheck.IsChecked == true;
        App.SaveSettingsSoon();
        UpdateLipControls();   // 추가: 립 재생은 게임 방식에서만
    }

    // ---------------------------------------------------------------- 립 재생 설정 (추가)

    private void UpdateLipControls()
    {
        if (ViewerLipCheck == null || ViewerLipSecondsBox == null) return;
        bool game = ViewerSingleTrackCheck.IsChecked != true;
        ViewerLipCheck.IsEnabled = game;
        bool on = game && ViewerLipCheck.IsChecked == true;
        ViewerLipAlwaysRadio.IsEnabled = on;
        ViewerLipSecondsRadio.IsEnabled = on;
        ViewerLipSecondsBox.IsEnabled = on && ViewerLipSecondsRadio.IsChecked == true;
    }

    /// <summary>립 설정 변경 → 저장, 게임 방식이면 처음부터 다시 재생해 바로 확인</summary>
    private void ViewerLipSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_viewerSettingsShown) return;
        _settings.Viewer.LipEnabled = ViewerLipCheck.IsChecked == true;
        _settings.Viewer.LipAlways = ViewerLipAlwaysRadio.IsChecked == true;
        App.SaveSettingsSoon();
        UpdateLipControls();
        if (_viewerLoaded && ViewerSingleTrackCheck.IsChecked != true) PlayAllTracks();
    }

    /// <summary>N초 입력: Enter 또는 포커스를 잃을 때 확정 (0.1 ~ 60초, 잘못된 값이면 이전 값으로)</summary>
    private void ViewerLipSecondsBox_LostFocus(object sender, RoutedEventArgs e) => CommitLipSeconds();

    private void ViewerLipSecondsBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitLipSeconds();
        e.Handled = true;
    }

    private void CommitLipSeconds()
    {
        if (double.TryParse(ViewerLipSecondsBox.Text, out double v) && v >= 0.1 && v <= 60)
        {
            bool changed = Math.Abs(v - _settings.Viewer.LipSeconds) > 1e-9;
            _settings.Viewer.LipSeconds = v;
            App.SaveSettingsSoon();
            if (changed && _viewerLoaded && ViewerSingleTrackCheck.IsChecked != true) PlayAllTracks();
        }
        ViewerLipSecondsBox.Text = _settings.Viewer.LipSeconds.ToString("0.##");
    }

    // ---------------------------------------------------------------- 재생 제어 (추가)

    private void ViewerPauseButton_Click(object sender, RoutedEventArgs e) => TogglePause();

    /// <summary>재생/일시정지 (전체 속도 0/1 — SpineDebugScene과 같음). 일시정지하면 본 목록 갱신</summary>
    private void TogglePause()
    {
        if (!_viewerLoaded) return;
        _viewerPaused = !_viewerPaused;
        _viewer?.Post(new { type = "pause", paused = _viewerPaused });
        ViewerPauseButton.Content = _viewerPaused ? "▶ 재생" : "❚❚ 일시정지";
        ViewerTimeSlider.IsEnabled = _viewerPaused;
        if (_viewerPaused) RequestBones();
    }

    /// <summary>드래그 바: 일시정지 중일 때만 시점 이동 (모든 트랙을 각자 길이 × 값으로)</summary>
    private void ViewerTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderEvent || !_viewerPaused || !_viewerLoaded) return;
        _viewer?.Post(new { type = "seek", value = e.NewValue });
        RequestBones();
    }

    // ---------------------------------------------------------------- 본 목록 (추가)

    private void ViewerBonesRefreshButton_Click(object sender, RoutedEventArgs e) => RequestBones();

    private void RequestBones()
    {
        if (_viewerLoaded && ViewerBonesExpander.IsExpanded) _viewer?.Post(new { type = "bones" });
    }

    private void ViewerBonesExpander_Expanded(object sender, RoutedEventArgs e) => RequestBones();

    /// <summary>기본 자세와 달라진 본 (변화량 표시)</summary>
    private void ShowBones(JsonElement bones)
    {
        ViewerBoneList.ItemsSource = bones.EnumerateArray().Select(b =>
        {
            var parts = new List<string>();
            double dr = b.GetProperty("dr").GetDouble(), dx = b.GetProperty("dx").GetDouble(), dy = b.GetProperty("dy").GetDouble();
            double dsx = b.GetProperty("dsx").GetDouble(), dsy = b.GetProperty("dsy").GetDouble();
            if (Math.Abs(dr) > 0.1) parts.Add($"회전 {dr:+0.0;-0.0}°");
            if (Math.Abs(dx) > 0.1 || Math.Abs(dy) > 0.1) parts.Add($"위치 {dx:+0.0;-0.0},{dy:+0.0;-0.0}");
            if (Math.Abs(dsx) > 0.01 || Math.Abs(dsy) > 0.01) parts.Add($"크기 {dsx:+0.00;-0.00},{dsy:+0.00;-0.00}");
            return $"{b.GetProperty("name").GetString()}  {string.Join(" · ", parts)}";
        }).ToList();
        ViewerBonesExpander.Header = $"본 목록 (기본 자세와 다른 본 {ViewerBoneList.Items.Count}개)";
    }

    // ---------------------------------------------------------------- 상태 표시
    // (Animation 문자열 내보내기는 씬 구성 탭으로 이관 예정 — 문자열 규칙은 Services/AnimationScript)

    private void ShowTrackState(JsonElement msg)
    {
        var tracks = msg.GetProperty("tracks");
        var sb = new StringBuilder();
        int i = 0;
        foreach (var t in tracks.EnumerateArray())
        {
            if (t.ValueKind == JsonValueKind.Null) sb.AppendLine($"[{i}] {AnimationScript.TrackLabels[i],-7} -");
            else
            {
                string name = t.GetProperty("name").GetString() ?? "";
                double time = t.GetProperty("time").GetDouble(), duration = t.GetProperty("duration").GetDouble();
                string mode = t.GetProperty("holding").GetBoolean() ? "유지"
                            : t.GetProperty("loop").GetBoolean() ? "반복"
                            : _viewerAnims.Any(a => a.name == name && a.loopStart >= 0) ? "loop_start" : "재생";
                sb.AppendLine($"[{i}] {AnimationScript.TrackLabels[i],-7} {name}  {time,5:0.00} / {duration:0.00}s  {mode}");
            }
            i++;
        }
        // 추가: 립 상태 (말하는 중 → 멈춤)
        if (msg.TryGetProperty("lip", out var lip) && lip.ValueKind == JsonValueKind.Object)
        {
            double sec = lip.GetProperty("seconds").GetDouble();
            bool talking = lip.GetProperty("phase").GetString() == "moving";
            sb.AppendLine(sec < 0
                ? $"립: {lip.GetProperty("moving").GetString()} 계속 말함 (멈춤 립: {lip.GetProperty("still").GetString() ?? "없음"})"
                : talking
                    ? $"립: 말하는 중 {lip.GetProperty("elapsed").GetDouble():0.0} / {sec:0.0}s → {lip.GetProperty("still").GetString() ?? "정지"}"
                    : $"립: 멈춤 ({lip.GetProperty("still").GetString() ?? "lip 트랙 정지"})");
        }
        ViewerTrackStateText.Text = sb.ToString().TrimEnd();

        // 추가: 진행률 (재생 중에만 드래그 바를 따라 움직임)
        if (!_viewerPaused)
        {
            _suppressSliderEvent = true;
            ViewerTimeSlider.Value = msg.GetProperty("progress").GetDouble();
            _suppressSliderEvent = false;
        }
        var t0 = tracks.EnumerateArray().FirstOrDefault(x => x.ValueKind != JsonValueKind.Null);
        ViewerTimeText.Text = t0.ValueKind == JsonValueKind.Object
            ? $"{t0.GetProperty("time").GetDouble():0.00} / {t0.GetProperty("duration").GetDouble():0.00}s"
            : "-";
    }

    // ---------------------------------------------------------------- 단축키·기타

    /// <summary>뷰어 탭 단축키 (WPF 쪽 포커스) — 글자 입력 칸에서는 무시</summary>
    private void ViewerTab_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.R) { ResetViewerTracks(); e.Handled = true; }
        else if (e.Key == Key.Space) { TogglePause(); e.Handled = true; }   // 추가: 스페이스바 = 재생/일시정지
    }

    /// <summary>뷰어 화면(WebView2)에 포커스가 있을 때 누른 키</summary>
    private void HandleViewerKey(string? key)
    {
        if (string.Equals(key, "r", StringComparison.OrdinalIgnoreCase)) ResetViewerTracks();
        else if (key == " ") TogglePause();   // 추가
    }

    private async void ViewerDevToolsButton_Click(object sender, RoutedEventArgs e)
    {
        try { await EnsureViewerAsync(); _viewer?.OpenDevTools(); }
        catch (Exception ex) { LogError("개발자 도구 열기 실패", ex); }
    }
}
