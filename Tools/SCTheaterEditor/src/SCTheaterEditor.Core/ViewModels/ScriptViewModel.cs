using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using SCTheaterEditor.Stage;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 스크립트 탭 (3회차 1차: 무대). 이전 뷰어 탭의 기능을 캐릭터 여러 명 기준으로 옮김.
/// - 무대 1920×1080: 배경(Backgrounds\) + 캐릭터 여러 명(위치 표 StageLayout) + 선택 표시
/// - 리소스 탭 Space → 미리보기(다음 빈자리, 강조) → Enter 적용 / Esc 취소(리소스 탭으로), Ctrl+Space → 바로 추가
/// - 선택 캐릭터 패널: 트랙별 애니메이션 목록(게임 방식/디버그 방식), 립, 스킨, 일시정지·드래그 바, 본 목록
/// - 무대에서 클릭 선택, 드래그로 자리 이동(겹치면 교체), 3/5분할 전환, Delete로 내리기
/// 4회차에 막·라인 문서 모델과 "라인에 적용"(Ctrl+Enter)이 붙는다.
/// </summary>
public sealed partial class ScriptViewModel : ObservableObject
{
    public const string NoBackground = "(배경 없음)";

    private readonly AppSettings _settings;
    private readonly SpineLibrary _library;
    private readonly BackgroundLibrary _backgrounds;
    private readonly IStageHost _host;
    private readonly LipRules _lipRules;
    private readonly IMessenger _messenger;
    private readonly Action _saveSettings;
    private readonly AppLog _log;

    private bool _stageReady;
    private int _nextId;
    private bool _fromState;                         // 상태 메시지로 드래그 바를 옮기는 중 (시점 이동 보내지 않음)
    private double _timeLength;                      // 추가: 드래그 바 길이(초) — 선택한 캐릭터 또는 무대 전체의 가장 긴 트랙
    private Dictionary<int, string>? _positionsBeforePreview;
    private StageActor? _replaced;                    // 무대가 꽉 차 미리보기가 자리를 대신 쓰는 캐릭터
    private StageActor? _selectedBeforePreview;
    private bool _initializing = true;                // 생성 중 (배경 기본값이 저장된 설정을 덮어쓰지 않도록)

    public ScriptViewModel(AppSettings settings, SpineLibrary library, BackgroundLibrary backgrounds, IStageHost host,
                           LipRules lipRules, IMessenger messenger, Action saveSettings, AppLog log,
                           AudioMemoStore? audioMemos = null)   // 추가(6회차): 오디오 메모 (없으면 저장하지 않는 빈 메모)
    {
        Audio = new AudioListViewModel(settings, audioMemos ?? new AudioMemoStore(null), Send, messenger, log);   // 추가(6회차)
        Layout = new ScriptLayoutViewModel(settings, saveSettings, messenger);   // 추가(7회차): 화면 배치
        _settings = settings;
        _library = library;
        _backgrounds = backgrounds;
        _host = host;
        _lipRules = lipRules;
        _messenger = messenger;
        _saveSettings = saveSettings;
        _log = log;

        BackgroundOptions = [NoBackground];
        SkinOptions = [];
        SelectedBackground = NoBackground;
        StatusText = "리소스 탭에서 세트를 고르고 Space(미리보기) 또는 Ctrl+Space(바로 추가)";
        AnimItems = [];
        Bones = [];
        BonesHeader = "본 목록 (기본 자세와 다른 본)";
        InfoText = "";
        TrackStateText = "";
        TimeText = "-";
        LipSecondsText = settings.Viewer.LipSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        Split = 3;

        _host.EventReceived += OnStageEvent;
        messenger.Register<ScriptViewModel, OpenSpineMessage>(this, (r, m) => r.Open(m.Asset, m.Immediate));
        // 추가(5회차): Resources 폴더가 바뀌면 배경 목록 다시 읽기
        messenger.Register<ScriptViewModel, SettingsChangedMessage>(this, static (r, m) =>
        {
            if (m.Area == SettingsArea.ResourcesFolder) r.OnResourcesFolderChanged();
        });

        RefreshBackgrounds();
        string? saved = settings.Editor.Background;
        if (saved != null && _backgrounds.Keys.Contains(saved, StringComparer.OrdinalIgnoreCase)) SelectedBackground = saved;
        _initializing = false;
    }

    /// <summary>추가(4-1): 스크립트 표 (MainViewModel이 연결, 4-2에서 무대와 연동). 변경(6회차): 오디오 목록에도 연결</summary>
    public ActEditorViewModel? Editor
    {
        get => _editor;
        set
        {
            _editor = value;
            Audio.AttachEditor(value);
        }
    }

    private ActEditorViewModel? _editor;

    /// <summary>추가(6회차): 오디오 목록 (변경(7회차): 오디오 패널)</summary>
    public AudioListViewModel Audio { get; }

    /// <summary>추가(7회차): 스크립트 탭 배치 (패널 폭 · 무대/표 높이 비율, 경계선 크기 저장)</summary>
    public ScriptLayoutViewModel Layout { get; }

    public ObservableCollection<StageActor> Actors { get; } = new();

    [ObservableProperty] public partial StageActor? SelectedActor { get; set; }

    /// <summary>미리보기 중인 캐릭터 (없으면 null)</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmPreviewCommand), nameof(CancelPreviewCommand))]
    public partial StageActor? Preview { get; private set; }

    /// <summary>지금 무대의 분할 (3 / 5)</summary>
    [ObservableProperty] public partial int Split { get; private set; }

    [ObservableProperty] public partial string StatusText { get; private set; }

    [ObservableProperty] public partial IReadOnlyList<string> BackgroundOptions { get; private set; }
    [ObservableProperty] public partial string SelectedBackground { get; set; }

    // ------------------------------------------------------------------ 선택 캐릭터 패널

    /// <summary>애니메이션 목록을 보여줄 트랙 (0 기본 / 1 face / 2 lip / 3 arm·eye)</summary>
    [ObservableProperty] public partial int SelectedTrack { get; set; }

    [ObservableProperty] public partial IReadOnlyList<AnimListItem> AnimItems { get; private set; }
    [ObservableProperty] public partial AnimListItem? SelectedAnimItem { get; set; }

    [ObservableProperty] public partial string InfoText { get; private set; }
    [ObservableProperty] public partial string TrackStateText { get; private set; }
    [ObservableProperty] public partial string TimeText { get; private set; }

    /// <summary>드래그 바 (0~1 = 0초~가장 긴 트랙 길이, 일시정지 중에만 시점 이동)</summary>
    [ObservableProperty] public partial double TimeValue { get; set; }

    /// <summary>변경: 재생 대상의 일시정지 상태 (선택한 캐릭터, 선택이 없으면 무대 전체 = 모두 멈춤)</summary>
    [ObservableProperty] public partial bool Paused { get; private set; }

    [ObservableProperty] public partial IReadOnlyList<string> Bones { get; private set; }
    [ObservableProperty] public partial string BonesHeader { get; private set; }
    [ObservableProperty] public partial bool BonesExpanded { get; set; }

    /// <summary>추가: 선택 캐릭터 패널의 [설정] 영역 펼침 (트랙 교체 방식·스킨·립·본 목록, 기본은 접힘)</summary>
    [ObservableProperty] public partial bool SettingsExpanded { get; set; }

    /// <summary>바꾼 트랙만 교체 (디버그 씬 방식). 끄면 게임 방식</summary>
    public bool SingleTrackMode
    {
        get => _settings.Viewer.SingleTrackMode;
        set
        {
            if (_settings.Viewer.SingleTrackMode == value) return;
            _settings.Viewer.SingleTrackMode = value;
            _saveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LipControlsEnabled));
            OnPropertyChanged(nameof(LipSecondsEnabled));
        }
    }

    public bool LipEnabled
    {
        get => _settings.Viewer.LipEnabled;
        set { if (SetLip(() => _settings.Viewer.LipEnabled = value, _settings.Viewer.LipEnabled != value)) OnPropertyChanged(); }
    }

    /// <summary>계속 말함 (false면 N초 후 멈춤)</summary>
    public bool LipAlways
    {
        get => _settings.Viewer.LipAlways;
        set { if (SetLip(() => _settings.Viewer.LipAlways = value, _settings.Viewer.LipAlways != value)) { OnPropertyChanged(); OnPropertyChanged(nameof(LipSeconds)); } }
    }

    /// <summary>"말하다 멈춤" 라디오 (LipAlways의 반대)</summary>
    public bool LipSeconds
    {
        get => !LipAlways;
        set { if (value) LipAlways = false; }
    }

    /// <summary>N초 입력 (0.1 ~ 60, 잘못된 값이면 이전 값으로 되돌림)</summary>
    [ObservableProperty] public partial string LipSecondsText { get; set; }

    public bool LipControlsEnabled => !SingleTrackMode && LipEnabled;
    public bool LipSecondsEnabled => LipControlsEnabled && !LipAlways;

    [ObservableProperty] public partial IReadOnlyList<string> SkinOptions { get; private set; }
    [ObservableProperty] public partial string? SelectedSkin { get; set; }

    // ------------------------------------------------------------------ 미리보기·추가·내리기

    /// <summary>리소스 탭에서 연 세트 → 미리보기(또는 바로 추가)</summary>
    public void Open(SpineAsset asset, bool immediate)
    {
        _messenger.Send(new ShowTabMessage(AppTab.Script));

        var existing = Actors.FirstOrDefault(a => !a.IsPreview && a.Asset.SourcePath == asset.SourcePath);
        if (existing != null)
        {
            CancelPreviewInternal(goBack: false);
            SelectedActor = existing;
            StatusText = $"이미 무대에 있는 세트입니다: {asset.CgKey}";
            return;
        }
        if (Preview != null)
        {
            if (Preview.Asset.SourcePath == asset.SourcePath && !immediate) { SelectedActor = Preview; return; }
            CancelPreviewInternal(goBack: false);
        }

        _positionsBeforePreview = Actors.ToDictionary(a => a.Id, a => a.Position);
        _selectedBeforePreview = SelectedActor;
        var (remap, position) = StageLayout.NextSlot(Actors.Select(a => a.Position).ToList());
        if (position == null)
        {
            // 꽉 참(5명): 선택한 캐릭터(없으면 마지막) 자리를 미리보기가 대신 씀 — 적용하면 그 캐릭터를 내림
            _replaced = SelectedActor ?? Actors.Last();
            position = _replaced.Position;
        }
        foreach (var a in Actors)
            if (remap.TryGetValue(a.Position, out var moved)) a.Position = moved;

        var actor = new StageActor(++_nextId, asset, position) { IsPreview = !immediate };
        Actors.Add(actor);
        Send(new AddActor(actor.Id, StageUrls.SpineFolder(asset.RelativeFolder), asset.JsonFile, asset.AtlasFile, actor.Label, actor.IsPreview));
        if (_replaced != null) Send(new ShowActor(_replaced.Id, false));
        PlaceAll();
        UpdateSplit();

        if (immediate)
        {
            CommitPreview(actor);
            StatusText = $"추가: {asset.CgKey} ({position})";
        }
        else
        {
            Preview = actor;
            StatusText = $"미리보기: {asset.CgKey} ({position}) — Enter 적용 · Esc 취소";
        }
        SelectedActor = actor;
    }

    private bool HasPreview() => Preview != null;

    [RelayCommand(CanExecute = nameof(HasPreview))]
    private void ConfirmPreview()
    {
        if (Preview is not { } actor) return;
        CommitPreview(actor);
        StatusText = $"추가: {actor.Asset.CgKey} ({actor.Position})";
    }

    /// <summary>미리보기 취소 → 무대를 미리보기 전으로 되돌리고 리소스 탭으로</summary>
    [RelayCommand(CanExecute = nameof(HasPreview))]
    private void CancelPreview() => CancelPreviewInternal(goBack: true);

    private void CommitPreview(StageActor actor)
    {
        if (actor.IsPreview)
        {
            actor.IsPreview = false;
            Send(new SetPreview(actor.Id, false));
        }
        if (_replaced != null) RemoveActorInternal(_replaced);
        _replaced = null;
        _positionsBeforePreview = null;
        _selectedBeforePreview = null;
        Preview = null;
        UpdateSplit();
    }

    private void CancelPreviewInternal(bool goBack)
    {
        if (Preview is not { } actor) return;
        RemoveActorInternal(actor);
        if (_positionsBeforePreview != null)
            foreach (var a in Actors)
                if (_positionsBeforePreview.TryGetValue(a.Id, out var p)) a.Position = p;
        if (_replaced != null) Send(new ShowActor(_replaced.Id, true));
        _replaced = null;
        _positionsBeforePreview = null;
        Preview = null;
        PlaceAll();
        UpdateSplit();
        SelectedActor = _selectedBeforePreview != null && Actors.Contains(_selectedBeforePreview) ? _selectedBeforePreview : null;
        _selectedBeforePreview = null;
        StatusText = "미리보기 취소";
        if (goBack) _messenger.Send(new ShowTabMessage(AppTab.Resource));
    }

    private bool HasSelection() => SelectedActor != null;

    /// <summary>선택한 캐릭터 내리기 (Delete). 미리보기면 취소와 같음 (탭은 그대로)</summary>
    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedActor is not { } actor) return;
        if (actor.IsPreview) { CancelPreviewInternal(goBack: false); return; }
        RemoveActorInternal(actor);
        UpdateSplit();
        StatusText = $"내림: {actor.Asset.CgKey}";
    }

    private void RemoveActorInternal(StageActor actor)
    {
        Send(new RemoveActor(actor.Id));
        Actors.Remove(actor);
        if (SelectedActor == actor) SelectedActor = null;
    }

    // ------------------------------------------------------------------ 자리

    /// <summary>3/5분할 전환 (5→3은 3명 이하일 때만)</summary>
    [RelayCommand]
    private void SetSplit(string? value)
    {
        int split = value == "5" ? 5 : 3;
        var visible = Actors.Where(a => a != _replaced).ToList();
        var map = StageLayout.ToSplit(visible.Select(a => a.Position).ToList(), split);
        if (map == null)
        {
            StatusText = "3분할로 바꾸려면 무대의 캐릭터가 3명 이하여야 합니다";
            OnPropertyChanged(nameof(Split));   // 추가: 누른 라디오 버튼이 스스로 체크된 것을 지금 분할로 되돌림
            return;
        }
        foreach (var a in visible) a.Position = map[a.Position];
        PlaceAll();
        UpdateSplit();
    }

    /// <summary>드래그로 놓은 위치 → 가장 가까운 자리, 다른 캐릭터가 있으면 서로 교체</summary>
    private void OnDropped(int id, double x)
    {
        var actor = Actors.FirstOrDefault(a => a.Id == id);
        if (actor == null) return;
        string target = StageLayout.Snap(x, Split);
        var other = Actors.FirstOrDefault(a => a != actor && a != _replaced && a.Position == target);
        if (other != null) other.Position = actor.Position;
        actor.Position = target;
        PlaceAll();
    }

    private void PlaceAll()
    {
        foreach (var a in Actors)
        {
            var p = StageLayout.Get(a.Position) ?? StageLayout.Get("center")!;
            Send(new PlaceActor(a.Id, p.X, StageLayout.BaseY, p.Scale));
        }
    }

    private void UpdateSplit() => Split = StageLayout.SplitOf(Actors.Where(a => a != _replaced).Select(a => a.Position));

    // ------------------------------------------------------------------ 배경

    [RelayCommand]
    private void RefreshBackgrounds()
    {
        try { _backgrounds.Refresh(); }
        catch (Exception ex) { _log.Error("배경 폴더 읽기 실패", ex); }
        string current = SelectedBackground;
        BackgroundOptions = [NoBackground, .. _backgrounds.Keys];
        SelectedBackground = BackgroundOptions.Contains(current) ? current : NoBackground;
        // 추가(5회차): 게임 폴더를 아직 모르면 안내
        if (!_initializing && _backgrounds.Folder == null) StatusText = "배경: 설정 탭에서 게임 Resources 폴더를 지정하세요";
    }

    /// <summary>추가(5회차): Resources 폴더가 바뀜 → 배경 폴더(Resources\BG)를 바꿔 목록 다시 읽고, 저장된 배경을 다시 고르고, 무대에 다시 보냄</summary>
    private void OnResourcesFolderChanged()
    {
        _backgrounds.SetFolder(_settings.Editor.BackgroundFolder);
        RefreshBackgrounds();
        string? saved = _settings.Editor.Background;
        if (SelectedBackground == NoBackground && saved != null &&
            _backgrounds.Keys.FirstOrDefault(k => k.Equals(saved, StringComparison.OrdinalIgnoreCase)) is { } key)
            SelectedBackground = key;
        SendBackground();   // 같은 키라도 파일이 다른 폴더로 바뀌었을 수 있음
    }

    partial void OnSelectedBackgroundChanged(string value)
    {
        string? key = value == NoBackground ? null : value;
        if (!_initializing && _settings.Editor.Background != key)
        {
            _settings.Editor.Background = key;
            _saveSettings();
        }
        SendBackground();
    }

    private void SendBackground()
    {
        string? file = SelectedBackground == NoBackground ? null : _backgrounds.FileName(SelectedBackground);
        Send(new SetBackground(file == null ? null : StageUrls.Background(file)));
    }

    // ------------------------------------------------------------------ 재생

    partial void OnSelectedActorChanged(StageActor? value)
    {
        Send(new SelectActor(value?.Id));
        UpdatePaused();   // 추가
        SelectedTrack = 0;
        Bones = [];
        BonesHeader = "본 목록 (기본 자세와 다른 본)";
        TrackStateText = "";
        TimeText = "-";
        RefreshPanel();
    }

    partial void OnSelectedTrackChanged(int value) => RefreshAnimList();

    private void RefreshPanel()
    {
        var a = SelectedActor;
        InfoText = a == null ? "" : $"{a.Asset.DressName}\n{a.Asset.CgKey} · {a.Position}\n{a.Info}";
        SkinOptions = a?.Skins ?? [];
        SelectedSkin = a?.Skin;
        RefreshAnimList();
    }

    /// <summary>선택한 트랙 기준 목록: (None) → 우선 그룹(예전 게임 SpineDebugManager에서 가져온 규칙) → 구분선 → 나머지</summary>
    private void RefreshAnimList()
    {
        var a = SelectedActor;
        if (a == null || !a.IsLoaded) { AnimItems = []; SelectedAnimItem = null; return; }
        int track = SelectedTrack;
        var items = new List<AnimListItem> { AnimListItem.None };
        var priority = a.Animations.Where(x => AnimationScript.IsPriority(x.Name, track)).OrderBy(x => AnimationScript.PriorityOrder(x.Name, track)).ToList();
        var rest = a.Animations.Where(x => !AnimationScript.IsPriority(x.Name, track)).ToList();
        items.AddRange(priority.Select(AnimListItem.From));
        if (rest.Count > 0) items.Add(AnimListItem.Separator);
        items.AddRange(rest.Select(AnimListItem.From));
        AnimItems = items;
        SelectedAnimItem = items.FirstOrDefault(i => i.Kind != AnimListItemKind.Separator && i.Name == a.Tracks[track]);
    }

    /// <summary>목록에서 고른 애니메이션 적용 (None = 선택한 트랙 비우기)</summary>
    [RelayCommand]
    private void ApplyAnim(AnimListItem? item)
    {
        if (item == null || item.Kind == AnimListItemKind.Separator || SelectedActor is not { IsLoaded: true } a) return;
        string? name = item.Name;
        int selected = SelectedTrack;
        if (SingleTrackMode)
        {
            // 디버그 방식: 선택한 트랙에만 건다 (접두어와 달라도 허용)
            a.Tracks[selected] = name;
            Send(new SetTrack(a.Id, selected, name));
        }
        else
        {
            // 게임 방식: 접두어 기준 트랙에 들어가고 전체를 다시 재생
            int target = name == null ? selected : AnimationScript.TrackIndex(name);
            a.Tracks[target] = name;
            PlayAll(a);
            if (target != selected)
                StatusText = $"{name}은(는) 접두어 기준으로 트랙 {target}({AnimationScript.TrackLabels[target]})에 들어갑니다 (게임과 동일)";
        }
        SelectedAnimItem = item;
    }

    /// <summary>선택한 캐릭터 전체 트랙 초기화 (R)</summary>
    [RelayCommand]
    private void ResetSelected()
    {
        if (SelectedActor is not { IsLoaded: true } a) return;
        Array.Clear(a.Tracks);
        Send(new ResetTracks(a.Id));
        RefreshAnimList();
    }

    /// <summary>게임 방식 재생: 트랙 0→3 순서의 문자열 전체 (재생 전 전체 초기화) + 립</summary>
    private void PlayAll(StageActor a)
    {
        var anims = a.Tracks.Where(x => x != null).Select(x => x!).ToList();
        Send(new PlayAnims(a.Id, anims, BuildLip(a, anims)));
    }

    /// <summary>립 재생 정보 (꺼져 있거나 디버그 방식이면 null) — CGManager.LipSyncCoroutine 규칙, 아이돌 키 = nickName</summary>
    private LipPlay? BuildLip(StageActor a, IReadOnlyList<string> anims)
    {
        if (!LipEnabled || SingleTrackMode) return null;
        var names = a.Animations.Select(x => x.Name).ToHashSet();
        var (moving, still) = _lipRules.Resolve(anims, a.Asset.NickName, names.Contains);
        if (moving == null) return null;
        return new LipPlay(moving, still, LipAlways ? -1 : _settings.Viewer.LipSeconds);
    }

    partial void OnSelectedSkinChanged(string? value)
    {
        if (SelectedActor is not { IsLoaded: true } a || value == null || a.Skin == value) return;
        a.Skin = value;
        Send(new SetSkin(a.Id, value));
        if (!SingleTrackMode) PlayAll(a);   // 부착물이 새 스킨 기준으로 바뀌므로 처음부터 다시
    }

    private bool SetLip(Action apply, bool changed)
    {
        if (!changed) return false;
        apply();
        _saveSettings();
        OnPropertyChanged(nameof(LipControlsEnabled));
        OnPropertyChanged(nameof(LipSecondsEnabled));
        ReplayAll();
        return true;
    }

    partial void OnLipSecondsTextChanged(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0.1 && v <= 60)
        {
            if (Math.Abs(v - _settings.Viewer.LipSeconds) < 1e-9) return;
            _settings.Viewer.LipSeconds = v;
            _saveSettings();
            ReplayAll();
        }
        else
        {
            LipSecondsText = _settings.Viewer.LipSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>립 설정이 바뀌면 게임 방식에서는 모두 처음부터 다시 재생해 바로 확인</summary>
    private void ReplayAll()
    {
        if (SingleTrackMode) return;
        foreach (var a in Actors.Where(a => a.IsLoaded)) PlayAll(a);
    }

    // 변경: 재생/일시정지를 캐릭터별로
    /// <summary>
    /// 재생/일시정지 (Space). 선택한 캐릭터가 있으면 그 캐릭터만, 없으면 무대 전체
    /// (전체: 하나라도 재생 중이면 모두 멈춤, 모두 멈춰 있으면 드래그 바 시점으로 맞춘 뒤 모두 재생)
    /// </summary>
    [RelayCommand]
    private void TogglePause()
    {
        if (SelectedActor is { } a)
        {
            a.Paused = !a.Paused;
            Send(new SetPaused(a.Paused, a.Id));
        }
        else if (Actors.Count > 0)
        {
            bool pause = Actors.Any(x => !x.Paused);
            foreach (var x in Actors) x.Paused = pause;
            Send(new SetPaused(pause));
        }
        UpdatePaused();
        if (Paused) RefreshBones();
    }

    private void UpdatePaused() =>
        Paused = SelectedActor?.Paused ?? (Actors.Count > 0 && Actors.All(a => a.Paused));

    /// <summary>변경: 드래그 바 → 실제 초 (선택한 캐릭터, 없으면 무대 전체)</summary>
    partial void OnTimeValueChanged(double value)
    {
        if (_fromState || !Paused) return;
        double seconds = value * _timeLength;
        if (SelectedActor is { } a)
        {
            if (!a.IsLoaded) return;
            Send(new Seek(a.Id, seconds));
            RefreshBones();
        }
        else Send(new Seek(null, seconds));
    }

    [RelayCommand]
    private void RefreshBones()
    {
        if (BonesVisible && SelectedActor is { IsLoaded: true } a) Send(new RequestBones(a.Id));
    }

    /// <summary>변경: 본 목록이 실제로 보이는지 = 설정 영역과 본 목록을 모두 펼침 (보이는 동안만 무대에서 실시간 갱신)</summary>
    public bool BonesVisible => SettingsExpanded && BonesExpanded;

    partial void OnBonesExpandedChanged(bool value) => OnBonesVisibilityChanged();

    partial void OnSettingsExpandedChanged(bool value) => OnBonesVisibilityChanged();

    private void OnBonesVisibilityChanged()
    {
        Send(new WatchBones(BonesVisible));   // 펼쳐 둔 동안 재생 중에도 본 목록 갱신
        RefreshBones();
    }

    // ------------------------------------------------------------------ 추가(4-2): 스크립트 ↔ 무대

    /// <summary>동기화로 내린 캐릭터를 바로 지우지 않고 숨겨 두는 수 (다시 올릴 때 Spine을 새로 불러오지 않음)</summary>
    public const int HiddenCacheSize = 10;

    private readonly List<StageActor> _hidden = new();   // 숨겨 둔 캐릭터 (오래된 것이 앞)
    private DateTime _lastSync;

    /// <summary>숨겨 둔 캐릭터 수 (테스트·상태 표시용)</summary>
    public int HiddenCount => _hidden.Count;

    /// <summary>라인에 적용·줄 추가에 쓸 지금 선택한 캐릭터 정보 (선택이 없거나 미리보기면 null)</summary>
    public StageStamp? CurrentStamp => SelectedActor is { IsPreview: false } a
        ? new StageStamp(a.Asset.CgKey, a.Position, a.AnimationText, a.Asset.NickName)
        : null;

    /// <summary>
    /// [스크립트 → 무대] (F5): 스크립트에서 선택한 줄 시점의 무대 상태(게임과 같은 계산)로 무대를 맞춤.
    /// 무대 페이지·WPF 양쪽에서 F5가 와도 한 번만 (0.3초 안의 두 번째는 무시)
    /// </summary>
    [RelayCommand]
    private void SyncFromScript()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSync < TimeSpan.FromMilliseconds(300)) return;
        _lastSync = now;
        if (Editor?.ComputeSnapshot() is not { } snapshot)
        {
            StatusText = "스크립트 표에서 동기화할 줄을 선택하세요";
            return;
        }
        ApplySnapshot(snapshot);
    }

    /// <summary>무대를 계산 결과로: 배경 · 캐릭터(같은 세트는 그대로 두고 위치·애니메이션만, 없는 캐릭터는 숨겨 두기, 숨겨 둔 캐릭터는 다시 보이기)</summary>
    public void ApplySnapshot(StageSnapshot snapshot)
    {
        CancelPreviewInternal(goBack: false);
        var skipped = new List<string>();

        // 배경 (게임 Resources\BG에 없으면 배경 없음 + 안내)   변경(5회차): 주석만
        if (snapshot.Background == null) SelectedBackground = NoBackground;
        else
        {
            string? key = FindBackground(snapshot.Background);
            if (key == null)
            {
                RefreshBackgrounds();   // 폴더에 새로 넣었을 수 있음
                key = FindBackground(snapshot.Background);
            }
            SelectedBackground = key ?? NoBackground;
            if (key == null) skipped.Add($"배경 {snapshot.Background}");
        }

        // 올릴 캐릭터 (SpineData에 없는 키는 건너뜀)
        var desired = new List<(SpineAsset Asset, StageActorState State)>();
        foreach (var st in snapshot.Actors)
        {
            var asset = _library.Find(st.CgKey);
            if (asset == null) skipped.Add(st.CgKey);
            else if (!desired.Any(d => d.Asset.SourcePath == asset.SourcePath)) desired.Add((asset, st));
        }

        foreach (var a in Actors.ToList())
            if (!desired.Any(d => d.Asset.SourcePath == a.Asset.SourcePath)) HideActor(a);

        foreach (var (asset, st) in desired)
        {
            var actor = Actors.FirstOrDefault(a => a.Asset.SourcePath == asset.SourcePath);
            bool replay = st.Animation != null;
            if (actor == null && _hidden.FirstOrDefault(a => a.Asset.SourcePath == asset.SourcePath) is { } cached)
            {
                _hidden.Remove(cached);
                Actors.Add(cached);
                Send(new ShowActor(cached.Id, true));
                actor = cached;
                replay = true;
            }
            if (actor == null)
            {
                actor = new StageActor(++_nextId, asset, st.Position);
                Actors.Add(actor);
                Send(new AddActor(actor.Id, StageUrls.SpineFolder(asset.RelativeFolder), asset.JsonFile, asset.AtlasFile, actor.Label, false));
            }
            actor.Position = st.Position;
            if (st.Animation != null)
            {
                // 게임 PlayAnimation과 같이 전체 트랙을 새 문자열로
                Array.Clear(actor.Tracks);
                var tracks = AnimationScript.Parse(st.Animation);
                Array.Copy(tracks, actor.Tracks, Math.Min(tracks.Length, actor.Tracks.Length));
            }
            if (replay && actor.IsLoaded) PlayAll(actor);   // 아직 불러오는 중이면 OnActorLoaded에서 재생
        }

        PlaceAll();
        UpdateSplit();
        if (SelectedActor != null && !Actors.Contains(SelectedActor)) SelectedActor = null;
        UpdatePaused();

        string status = $"동기화: {snapshot.LineIndex + 1}번 줄 시점 · 캐릭터 {desired.Count}명 · 배경 {(snapshot.Background ?? "없음")}";
        if (skipped.Count > 0) status += $" · 올리지 못함: {string.Join(", ", skipped)}";
        if (snapshot.Notes.Count > 0) status += " · " + string.Join(" / ", snapshot.Notes);
        StatusText = status;
    }

    private string? FindBackground(string key) =>
        _backgrounds.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>캐릭터를 무대에서 내리되 지우지 않고 숨겨 둠 (최근 HiddenCacheSize명, 넘치면 가장 오래된 것부터 지움)</summary>
    private void HideActor(StageActor actor)
    {
        Send(new ShowActor(actor.Id, false));
        Actors.Remove(actor);
        if (SelectedActor == actor) SelectedActor = null;
        _hidden.Add(actor);
        while (_hidden.Count > HiddenCacheSize)
        {
            Send(new RemoveActor(_hidden[0].Id));
            _hidden.RemoveAt(0);
        }
    }

    /// <summary>[라인에 적용] (Ctrl+Enter): 선택한 캐릭터의 CG·위치·애니메이션을 스크립트에서 선택한 줄에</summary>
    [RelayCommand]
    private void ApplyToLine()
    {
        if (CurrentStamp is not { } stamp)
        {
            StatusText = SelectedActor?.IsPreview == true ? "미리보기를 먼저 적용(Enter)하세요" : "무대에서 적용할 캐릭터를 선택하세요";
            return;
        }
        Editor?.ApplyStamp(stamp);
    }

    // ------------------------------------------------------------------ 키

    /// <summary>
    /// 스크립트 탭 단축키 (무대 페이지·WPF 어느 쪽에서 눌러도 여기로). 처리했으면 true.
    /// Space 재생/일시정지 · R 초기화 · Enter 미리보기 적용 · Esc 미리보기 취소 · Delete 내리기
    /// </summary>
    public bool HandleKey(string key)
    {
        switch (key)
        {
            case " ": TogglePause(); return true;
            case "r": case "R": ResetSelected(); return true;
            case "Enter": if (Preview == null) return false; ConfirmPreview(); return true;
            case "Escape": if (Preview == null) return false; CancelPreview(); return true;
            case "Delete": RemoveSelected(); return true;
            case "F5": SyncFromScript(); return true;   // 추가(4-2): 무대에 포커스가 있을 때 페이지가 넘겨 줌
            default: return false;
        }
    }

    // ------------------------------------------------------------------ 무대 이벤트

    private void Send(StageCommand command)
    {
        if (_stageReady) _host.Send(command);   // 준비 전에는 상태만 갖고 있다가 Ready 때 한꺼번에 보냄
    }

    private void OnStageEvent(StageEvent e)
    {
        switch (e)
        {
            case StageReady: Resync(); break;
            case ActorLoaded m: OnActorLoaded(m); break;
            case ActorError m:
                if (Actors.FirstOrDefault(a => a.Id == m.Id) is { } failed)
                {
                    failed.Info = $"불러오기 실패: {m.Message}";
                    if (failed == SelectedActor) RefreshPanel();
                    _log.Warn($"무대 불러오기 실패: {failed.Asset.CgKey} — {m.Message}");
                }
                break;
            case ActorState m when m.Id == SelectedActor?.Id: ShowState(m); break;
            case StageTime m when SelectedActor == null: ShowStageTime(m); break;                         // 추가
            case ActorBones m when m.Id == SelectedActor?.Id: ShowBones(m.Bones); break;
            case BackgroundError m: _log.Warn($"배경 불러오기 실패: {m.Url} — {m.Message}"); break;      // 추가
            case StageClicked m: SelectedActor = Actors.FirstOrDefault(a => a.Id == m.Id); break;
            case ActorDropped m: OnDropped(m.Id, m.X); break;
            case StageKey m: HandleKey(m.Key); break;
            case AudioEnded or AudioError: Audio.OnStageEvent(e); break;   // 추가(6회차): 오디오 목록 미리 듣기
        }
    }

    /// <summary>페이지가 (다시) 준비됨 → 지금 무대 상태를 전부 보냄</summary>
    private void Resync()
    {
        _stageReady = true;
        _hidden.Clear();   // 추가(4-2): 페이지를 새로 불러오면 숨겨 둔 캐릭터는 페이지에서 사라짐
        Audio.OnStageReset();   // 추가(6회차): 재생 중이던 미리 듣기도 사라짐
        Send(new StageConfig(AnimationScript.TrackCount, StageLayout.ScreenWidth, StageLayout.ScreenHeight, StageLayout.UnitScale));
        SendBackground();
        Send(new WatchBones(BonesVisible));   // 변경: 무대 전체 일시정지 대신 (캐릭터별 일시정지는 불러온 뒤 OnActorLoaded에서)
        foreach (var a in Actors)
        {
            a.IsLoaded = false;
            Send(new AddActor(a.Id, StageUrls.SpineFolder(a.Asset.RelativeFolder), a.Asset.JsonFile, a.Asset.AtlasFile, a.Label, a.IsPreview));
        }
        PlaceAll();
        if (_replaced != null) Send(new ShowActor(_replaced.Id, false));
        Send(new SelectActor(SelectedActor?.Id));
    }

    private void OnActorLoaded(ActorLoaded m)
    {
        // 변경(4-2): 불러오는 중에 숨겨 둔 캐릭터도 정보는 채움 (다시 보일 때 바로 쓰도록)
        if ((Actors.FirstOrDefault(a => a.Id == m.Id) ?? _hidden.FirstOrDefault(a => a.Id == m.Id)) is not { } a) return;
        if (_hidden.Contains(a)) Send(new ShowActor(a.Id, false));
        a.Animations = m.Animations;
        a.Skins = m.Skins;
        a.Skin = m.Skin;
        a.Info = $"Spine {m.Version} · 본 {m.BoneCount}개 · 애니메이션 {m.Animations.Count}개 · {m.Width:0} x {m.Height:0}";
        a.IsLoaded = true;

        // 처음 올릴 때 wait가 있으면 기본 트랙에 (게임에서 가장 흔한 시작 상태). 다시 불러온 경우는 지금 트랙 유지
        if (a.Tracks.All(t => t == null) && m.Animations.Any(x => x.Name == "wait")) a.Tracks[0] = "wait";
        if (a.Skin != null && a.Skins.Count > 1) Send(new SetSkin(a.Id, a.Skin));
        PlayAll(a);
        if (a.Paused) Send(new SetPaused(true, a.Id));   // 추가: 페이지를 새로 불러온 경우 멈춰 있던 캐릭터는 다시 멈춤
        if (a == SelectedActor) RefreshPanel();
    }

    private void ShowState(ActorState m)
    {
        var sb = new StringBuilder();
        var anims = SelectedActor!.Animations;
        for (int i = 0; i < m.Tracks.Count; i++)
        {
            var t = m.Tracks[i];
            string label = AnimationScript.TrackLabels[Math.Min(i, AnimationScript.TrackLabels.Length - 1)];
            if (t == null) { sb.AppendLine($"[{i}] {label,-7} -"); continue; }
            string mode = t.Holding ? "유지" : t.Loop ? "반복" : anims.Any(a => a.Name == t.Name && a.LoopStart >= 0) ? "loop_start" : "재생";
            sb.AppendLine($"[{i}] {label,-7} {t.Name}  {t.Time,5:0.00} / {t.Duration:0.00}s  {mode}");
        }
        if (m.Lip is { } lip)
        {
            sb.AppendLine(lip.Seconds < 0
                ? $"립: {lip.Moving} 계속 말함 (멈춤 립: {lip.Still ?? "없음"})"
                : lip.Phase == "moving"
                    ? $"립: 말하는 중 {lip.Elapsed:0.0} / {lip.Seconds:0.0}s → {lip.Still ?? "정지"}"
                    : $"립: 멈춤 ({lip.Still ?? "lip 트랙 정지"})");
        }
        TrackStateText = sb.ToString().TrimEnd();
        ShowTime(m.Clock, m.Length);   // 변경: 기본 트랙 진행률 → 실제 초 (가장 긴 트랙 기준)
        if (m.Bones != null) ShowBones(m.Bones);   // 추가: 본 목록 실시간 갱신
    }

    /// <summary>추가: 선택한 캐릭터가 없을 때 무대 전체 시간</summary>
    private void ShowStageTime(StageTime m)
    {
        TrackStateText = $"무대 전체 {Actors.Count}명 — 가장 긴 애니메이션 {m.Length:0.00}s\n(캐릭터를 선택하면 그 캐릭터만 재생·정지)";
        ShowTime(m.Time, m.Length);
    }

    /// <summary>추가: 드래그 바·시간 표시 (바는 길이에서 멈춤)</summary>
    private void ShowTime(double time, double length)
    {
        _timeLength = length;
        double t = Math.Min(time, length);
        if (!Paused)
        {
            _fromState = true;
            TimeValue = length > 0 ? t / length : 0;
            _fromState = false;
        }
        TimeText = length > 0 ? $"{t:0.00} / {length:0.00}s" : "-";
    }

    private void ShowBones(IReadOnlyList<BoneDelta> bones)
    {
        var lines = bones.Select(b =>
        {
            var parts = new List<string>();
            if (Math.Abs(b.Dr) > 0.1) parts.Add($"회전 {b.Dr:+0.0;-0.0}°");
            if (Math.Abs(b.Dx) > 0.1 || Math.Abs(b.Dy) > 0.1) parts.Add($"위치 {b.Dx:+0.0;-0.0},{b.Dy:+0.0;-0.0}");
            if (Math.Abs(b.Dsx) > 0.01 || Math.Abs(b.Dsy) > 0.01) parts.Add($"크기 {b.Dsx:+0.00;-0.00},{b.Dsy:+0.00;-0.00}");
            return $"{b.Name}  {string.Join(" · ", parts)}";
        }).ToList();
        if (lines.SequenceEqual(Bones)) return;   // 추가: 바뀐 게 없으면 목록을 다시 그리지 않음 (재생 중 10회/초)
        Bones = lines;
        BonesHeader = $"본 목록 (기본 자세와 다른 본 {Bones.Count}개)";
    }
}

public enum AnimListItemKind { None, Item, Separator }

/// <summary>애니메이션 목록 1줄</summary>
public sealed record AnimListItem(AnimListItemKind Kind, string? Name, string Text)
{
    public static readonly AnimListItem None = new(AnimListItemKind.None, null, "(None) — 이 트랙 비우기");
    public static readonly AnimListItem Separator = new(AnimListItemKind.Separator, null, "──── 그 밖의 애니메이션 ────");

    public static AnimListItem From(AnimInfo a)
    {
        string mark = a.LoopStart >= 0 ? $"  ↻ {a.LoopStart:0.00}s부터 반복"
                    : AnimationScript.ForceLoopAnims.Contains(a.Name) ? "  ∞ 반복" : "";
        return new AnimListItem(AnimListItemKind.Item, a.Name, $"{a.Name}  ({a.Duration:0.00}s){mark}");
    }

    public bool IsSeparator => Kind == AnimListItemKind.Separator;
}
