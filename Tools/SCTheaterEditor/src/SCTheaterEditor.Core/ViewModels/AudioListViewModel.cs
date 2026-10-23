using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Services;
using SCTheaterEditor.Stage;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 추가(6회차): 스크립트 탭 오디오 목록 (선택 캐릭터 패널 아래 접는 영역).
/// - 구분 SE / BGM / Voice, Voice는 하위 폴더(시나리오)를 고름 — 처음 값 = 지금 연 막의 그룹 폴더
/// - 미리 듣기: Space · [▶] = 재생/정지, 더블클릭 = 처음부터 재생. 한 번에 하나 (무대 채널 "preview")
/// - 줄에 넣기: 목록에 포커스가 있을 때 Ctrl+Enter (보이스는 Ctrl+Shift+Enter = 이어 붙이기) → ActEditorViewModel.InsertAudioKey
///   보이스 키: 막의 그룹 폴더 = 키만, 다른 폴더 = "폴더/키" (게임 AudioManager: 시나리오 폴더 → Voice 루트 순으로 찾음)
/// - 메모: AudioMemoStore (exe 옆 AudioMemo.xml)
/// </summary>
public sealed partial class AudioListViewModel : ObservableObject
{
    public const string PreviewChannel = "preview";

    private readonly AppSettings _settings;
    private readonly AudioMemoStore _memos;
    private readonly Action<StageCommand> _send;
    private readonly AppLog _log;
    private ActEditorViewModel? _editor;
    private string? _appliedGroup;   // 마지막으로 보이스 폴더를 맞춘 막 그룹 (같은 막이면 사용자가 고른 폴더 유지)

    public AudioListViewModel(AppSettings settings, AudioMemoStore memos, Action<StageCommand> send, IMessenger messenger, AppLog log)
    {
        _settings = settings;
        _memos = memos;
        _send = send;
        _log = log;
        Items = [];
        VoiceFolders = [];
        StatusText = "";
        if (memos.LoadNotice != null) log.Warn(memos.LoadNotice);
        messenger.Register<AudioListViewModel, SettingsChangedMessage>(this, static (r, m) =>
        {
            if (m.Area == SettingsArea.ResourcesFolder) r.OnResourcesFolderChanged();
        });
        RefreshFolders();
        Refresh();
    }

    /// <summary>스크립트 표 연결 (막이 바뀌면 보이스 폴더를 그 막의 그룹으로)</summary>
    public void AttachEditor(ActEditorViewModel? editor)
    {
        if (_editor != null) _editor.PropertyChanged -= OnEditorChanged;
        _editor = editor;
        if (editor != null) editor.PropertyChanged += OnEditorChanged;
        ApplyActGroup();
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActEditorViewModel.Document)) ApplyActGroup();
    }

    private string? ActGroup => _editor?.Document?.Group;

    private void ApplyActGroup()
    {
        string? group = ActGroup;
        if (group == null || string.Equals(group, _appliedGroup, StringComparison.OrdinalIgnoreCase)) return;
        _appliedGroup = group;
        if (VoiceFolders.FirstOrDefault(f => f.Equals(group, StringComparison.OrdinalIgnoreCase)) is { } match) VoiceFolder = match;
    }

    // ------------------------------------------------------------------ 목록

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVoice))]
    public partial AudioKind Kind { get; set; }

    public bool IsVoice => Kind == AudioKind.Voice;

    [ObservableProperty] public partial IReadOnlyList<string> VoiceFolders { get; private set; }
    [ObservableProperty] public partial string? VoiceFolder { get; set; }

    [ObservableProperty] public partial IReadOnlyList<AudioRow> Items { get; private set; }
    [ObservableProperty] public partial AudioRow? SelectedItem { get; set; }
    [ObservableProperty] public partial string StatusText { get; private set; }

    partial void OnKindChanged(AudioKind value) => Refresh();

    partial void OnVoiceFolderChanged(string? value)
    {
        if (Kind == AudioKind.Voice) Refresh();
    }

    private void OnResourcesFolderChanged()
    {
        Stop();
        _appliedGroup = null;
        RefreshFolders();
        ApplyActGroup();
        Refresh();
    }

    private void RefreshFolders()
    {
        string? current = VoiceFolder;
        VoiceFolders = AudioLibrary.VoiceFolders(_settings.Editor.GameResourcesFolder);
        VoiceFolder = VoiceFolders.FirstOrDefault(f => f.Equals(current, StringComparison.OrdinalIgnoreCase))
                      ?? VoiceFolders.FirstOrDefault(f => f.Equals(ActGroup, StringComparison.OrdinalIgnoreCase))
                      ?? VoiceFolders.FirstOrDefault();
    }

    /// <summary>[↻]: 보이스 폴더 목록과 파일 목록 다시 읽기</summary>
    [RelayCommand]
    private void Reload()
    {
        RefreshFolders();
        Refresh();
    }

    /// <summary>파일 목록 다시 읽기 (선택은 같은 키로 유지)</summary>
    private void Refresh()
    {
        string? res = _settings.Editor.GameResourcesFolder;
        string? selectedKey = SelectedItem?.File.Key;
        IReadOnlyList<AudioFile> files;
        try { files = AudioLibrary.Scan(res, Kind, VoiceFolder); }
        catch (Exception ex)
        {
            _log.Error("오디오 폴더 읽기 실패", ex);
            files = [];
        }
        Items = files.Select(f => new AudioRow(f, _memos.Get(f.Kind, f.MemoKey), SaveMemo)).ToList();
        _refreshing = true;   // 추가(8회차)
        try { SelectedItem = Items.FirstOrDefault(r => r.File.Key == selectedKey); }
        finally { _refreshing = false; }
        if (Playing != null) Playing = Items.FirstOrDefault(r => r.File.RelativePath == Playing.File.RelativePath);
        if (Playing != null) Playing.IsPlaying = true;
        StatusText = res == null ? "설정 탭에서 게임 Resources 폴더를 지정하세요"
            : Kind == AudioKind.Voice && VoiceFolder == null ? "Resources\\Audio\\Voice 아래에 폴더가 없습니다"
            : Items.Count == 0 ? $"{AudioLibrary.SubFolder(Kind)}{(Kind == AudioKind.Voice ? $"\\{VoiceFolder}" : "")}에 오디오가 없습니다"
            : $"{Items.Count}개";
    }

    private void SaveMemo(AudioRow row, string text)
    {
        try { _memos.Set(row.File.Kind, row.File.MemoKey, text); }
        catch (Exception ex) { _log.Error($"오디오 메모 저장 실패: {AudioMemoStore.DefaultPath}", ex); }
    }

    // ------------------------------------------------------------------ 미리 듣기 (변경(8회차): 재생 바와 같은 대상 — 일시정지 · 이어 재생 · 시점 이동)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    public partial AudioRow? Playing { get; private set; }

    /// <summary>추가(8회차): 재생 중인 항목이 멈춰 있음</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    public partial bool IsPaused { get; private set; }

    /// <summary>소리가 나는 중 (변경(8회차): 멈춤 제외 — 버튼은 ❚❚ 일시정지)</summary>
    public bool IsPlaying => Playing != null && !IsPaused;

    /// <summary>추가(8회차): 사용자가 오디오를 고르거나 재생함 → 재생 바 대상을 이 오디오로 (ScriptViewModel이 받음)</summary>
    public event Action? Activated;

    private bool _refreshing;   // 목록을 다시 읽으며 선택을 되살리는 중 (재생 대상을 바꾸지 않음)

    partial void OnSelectedItemChanged(AudioRow? value)
    {
        if (value != null && !_refreshing) Activated?.Invoke();
    }

    /// <summary>
    /// Space · [▶] · 재생 바: 고른 항목이 재생 중인 것과 다르면 처음부터 재생, 같으면 일시정지 / 이어 재생 (변경(8회차): 정지 → 일시정지).
    /// 끝난 뒤에는 처음부터 다시
    /// </summary>
    [RelayCommand]
    private void TogglePlay()
    {
        if (SelectedItem is { } selected && selected != Playing) Play(selected);
        else if (Playing != null)
        {
            IsPaused = !IsPaused;
            _send(new PauseAudio(PreviewChannel, IsPaused));
            StatusText = IsPaused ? $"일시정지: {Playing.File.Key}" : $"재생: {Playing.File.Key}";
        }
        else
        {
            StatusText = "목록에서 들을 오디오를 고르세요";
            return;
        }
        Activated?.Invoke();
    }

    /// <summary>더블클릭: 그 항목을 처음부터</summary>
    public void PlayFromStart(AudioRow row)
    {
        SelectedItem = row;
        Play(row);
        Activated?.Invoke();
    }

    /// <summary>추가(8회차): 재생 바 시점 이동 (재생 중이거나 멈춘 항목)</summary>
    public void Seek(double seconds)
    {
        if (Playing != null) _send(new SeekAudio(PreviewChannel, seconds));
    }

    private void Play(AudioRow row)
    {
        if (Playing != null) Playing.IsPlaying = false;
        Playing = row;
        IsPaused = false;
        row.IsPlaying = true;
        _send(new PlayAudio(PreviewChannel, StageUrls.Resource(row.File.RelativePath), 1, false));
        StatusText = $"재생: {row.File.Key}";
    }

    public void Stop()
    {
        if (Playing == null) return;
        Playing.IsPlaying = false;
        Playing = null;
        IsPaused = false;
        _send(new StopAudio(PreviewChannel));
        StatusText = "정지";
    }

    /// <summary>무대 이벤트 (ScriptViewModel이 넘김): 끝남·오류면 재생 표시 해제, 시간 알림으로 멈춤 상태 맞춤</summary>
    public void OnStageEvent(StageEvent e)
    {
        switch (e)
        {
            case AudioEnded { Channel: PreviewChannel } when Playing != null:
                Playing.IsPlaying = false;
                Playing = null;
                IsPaused = false;
                StatusText = "끝남";
                break;
            case AudioError { Channel: PreviewChannel } m:
                if (Playing != null) Playing.IsPlaying = false;
                Playing = null;
                IsPaused = false;
                StatusText = $"재생 실패: {m.Message}";
                _log.Warn($"오디오 재생 실패: {m.Url} — {m.Message}");
                break;
        }
    }

    /// <summary>무대 페이지를 새로 불러옴 → 재생 중이던 소리는 사라짐</summary>
    public void OnStageReset()
    {
        if (Playing != null) Playing.IsPlaying = false;
        Playing = null;
        IsPaused = false;
    }

    // ------------------------------------------------------------------ 줄에 넣기

    /// <summary>줄에 넣을 키 (보이스는 막의 그룹 폴더가 아니면 "폴더/키")</summary>
    public string KeyFor(AudioRow row) =>
        row.File.Kind == AudioKind.Voice && !string.Equals(row.File.Folder, ActGroup, StringComparison.OrdinalIgnoreCase)
            ? $"{row.File.Folder}/{row.File.Key}"
            : row.File.Key;

    /// <summary>Ctrl+Enter (보이스 + Ctrl+Shift+Enter = 이어 붙이기)</summary>
    public void InsertSelected(bool append)
    {
        if (SelectedItem is not { } row) { StatusText = "목록에서 넣을 오디오를 고르세요"; return; }
        if (_editor == null) return;
        _editor.InsertAudioKey(row.File.Kind, KeyFor(row), append && row.File.Kind == AudioKind.Voice);
        StatusText = _editor.StatusText;
    }
}

/// <summary>추가(6회차): 오디오 목록 한 줄 — 키 · 길이 · 메모(오른쪽, 클릭하면 바로 편집 · Enter 확정 · Esc 취소)</summary>
public sealed partial class AudioRow : ObservableObject
{
    private readonly Action<AudioRow, string> _save;

    internal AudioRow(AudioFile file, string memo, Action<AudioRow, string> save)
    {
        File = file;
        _save = save;
        Memo = memo;
        EditText = "";
    }

    public AudioFile File { get; }
    public string Key => File.Key;
    public string LengthText => AudioLibrary.FormatDuration(File.Duration);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMemo), nameof(MemoLabel))]
    public partial string Memo { get; private set; }

    public bool HasMemo => Memo.Length > 0;

    /// <summary>오른쪽 표시: "(경쾌한 일상)", 없으면 "(메모 추가)" (선택·마우스를 올린 줄에만 흐리게)</summary>
    public string MemoLabel => HasMemo ? $"({Memo})" : "(메모 추가)";

    [ObservableProperty] public partial bool IsEditing { get; private set; }
    [ObservableProperty] public partial string EditText { get; set; }
    [ObservableProperty] public partial bool IsPlaying { get; set; }

    public void BeginEdit()
    {
        EditText = Memo;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        if (!IsEditing) return;
        IsEditing = false;
        string text = EditText.Trim();
        if (text == Memo) return;
        Memo = text;
        _save(this, text);
    }

    public void CancelEdit() => IsEditing = false;
}
