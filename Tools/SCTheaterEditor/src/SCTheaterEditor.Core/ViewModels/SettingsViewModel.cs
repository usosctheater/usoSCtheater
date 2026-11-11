using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Script;   // 추가: 조합키 슬롯
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 추가: 설정 탭. 설정 관리 규칙(TE 핸드오프 0절) — 값은 AppSettings 한 곳, 바꾸는 로직은 여기(Core), 화면은 바인딩만,
/// 기능은 SettingsChangedMessage만 받음. 바뀌면 바로 저장(0.4초 모아서).
/// 구역: 스크립트 편집(자동 백업·보관 수·조합키 슬롯) / 경로. ④에 게임 Resources 폴더 추가 예정
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const int MinEdits = 1, MaxEdits = 1000;
    public const int MinMinutes = 1, MaxMinutes = 120;
    public const int MinKeep = 1, MaxKeep = 200;

    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly Action _saveSettings;
    private readonly IMessenger _messenger;
    private readonly AppLog _log;

    private readonly LineTypeCatalog _types;   // 추가: 조합키 슬롯 목록
    private readonly GameData? _gameData;      // 추가(번역 모드 2회차): 게임 데이터 복사본

    public SettingsViewModel(AppSettings settings, IDialogService dialogs, Action saveSettings, IMessenger messenger, AppLog log,
                             LineTypeCatalog? types = null,    // 변경: types 추가 (생략하면 내장 LineTypes.xml)
                             GameData? gameData = null,        // 추가(번역 모드 2회차)
                             VoiceMatchData? voices = null, string? voicePath = null)   // 추가(번역 모드 5회차): 보이스 매칭
    {
        _voices = voices;
        _voicePath = voicePath ?? VoiceMatchData.DefaultPath;
        VoiceNote = "";
        VoiceRows = [];
        VoiceOptions = [];
        _gameData = gameData;   // 추가(번역 모드 2회차)
        GameDataNote = "";
        if (gameData != null) gameData.Changed += () => OnPropertyChanged(nameof(GameDataStates));
        _settings = settings;
        _dialogs = dialogs;
        _saveSettings = saveSettings;
        _messenger = messenger;
        _log = log;
        _types = types ?? LineTypeCatalog.LoadEmbedded();
        SlotOptions = [NoSlot, .. _types.Ids];
        Slots = LineSlots.Normalize(settings.Editor.LineSlots, _types)
                         .Select((t, i) => new SlotSetting(i + 1, t.Length == 0 ? NoSlot : t, OnSlotChanged)).ToList();
        ScriptEditingNote = "";
        PathNote = "";
        PreTranslateNote = "";   // 추가(번역 모드 4회차)
        if (gameData != null) gameData.Changed += BuildVoiceRows;   // 추가(번역 모드 5회차): 인물 목록 = CharacterData
        BuildVoiceRows();
        // 다른 곳(스크립트 표 [폴더 지정…]·리소스 탭 [게임 폴더…])에서 바꿔도 표시를 맞춤
        messenger.Register<SettingsViewModel, SettingsChangedMessage>(this, static (r, m) => r.OnSettingsChanged(m));
    }

    // ------------------------------------------------------------------ 스크립트 편집

    /// <summary>자동 백업: 마지막 백업 뒤 편집 N번이면 바로</summary>
    public string AutoBackupEditsText
    {
        get => _settings.Editor.AutoBackupEdits.ToString();
        set => SetNumber(value, MinEdits, MaxEdits, "자동 백업 편집 횟수", _settings.Editor.AutoBackupEdits,
                         v => _settings.Editor.AutoBackupEdits = v, nameof(AutoBackupEditsText));
    }

    /// <summary>자동 백업: 마지막 백업 뒤 M분이 지났고 편집이 있으면 (정수 분)</summary>
    public string AutoBackupMinutesText
    {
        get => ((int)Math.Round(_settings.Editor.AutoBackupMinutes)).ToString();
        set => SetNumber(value, MinMinutes, MaxMinutes, "자동 백업 시간", (int)Math.Round(_settings.Editor.AutoBackupMinutes),
                         v => _settings.Editor.AutoBackupMinutes = v, nameof(AutoBackupMinutesText));
    }

    /// <summary>막마다 남길 백업 수</summary>
    public string BackupCountText
    {
        get => _settings.Editor.BackupCount.ToString();
        set => SetNumber(value, MinKeep, MaxKeep, "백업 보관 수", _settings.Editor.BackupCount,
                         v => _settings.Editor.BackupCount = v, nameof(BackupCountText));
    }

    /// <summary>스크립트 편집 구역 안내 (잘못된 입력 등)</summary>
    [ObservableProperty] public partial string ScriptEditingNote { get; private set; }

    /// <summary>정수·범위 검사 → 맞으면 적용(바뀐 경우만 저장·알림), 아니면 원래 값으로 되돌리고 안내</summary>
    private void SetNumber(string? text, int min, int max, string label, int current, Action<int> apply, string property)
    {
        if (int.TryParse(text?.Trim(), out int v) && v >= min && v <= max)
        {
            ScriptEditingNote = "";
            if (v != current)
            {
                apply(v);
                Changed(SettingsArea.ScriptEditing);
            }
        }
        else ScriptEditingNote = $"{label}: {min}~{max} 사이의 정수만 입력할 수 있어 원래 값으로 되돌렸습니다";
        OnPropertyChanged(property);   // 칸을 실제 값으로 다시 맞춤 (되돌린 경우 포함)
    }

    [RelayCommand]
    private void ResetScriptEditing()
    {
        var d = new EditorSettings();
        _settings.Editor.AutoBackupEdits = d.AutoBackupEdits;
        _settings.Editor.AutoBackupMinutes = d.AutoBackupMinutes;
        _settings.Editor.BackupCount = d.BackupCount;
        ScriptEditingNote = "기본값으로 되돌렸습니다";
        Changed(SettingsArea.ScriptEditing);
        OnPropertyChanged(nameof(AutoBackupEditsText));
        OnPropertyChanged(nameof(AutoBackupMinutesText));
        OnPropertyChanged(nameof(BackupCountText));
    }

    // ------------------------------------------------------------------ 추가: 조합키 (스크립트 표에서 Ctrl+숫자)

    /// <summary>드롭다운에서 빈 슬롯을 나타내는 글자 (저장할 때는 "")</summary>
    public const string NoSlot = "(없음)";

    /// <summary>슬롯 드롭다운 목록: (없음) + 줄 종류 (드롭다운 순서)</summary>
    public IReadOnlyList<string> SlotOptions { get; }

    /// <summary>슬롯 1~9</summary>
    public IReadOnlyList<SlotSetting> Slots { get; }

    private void OnSlotChanged(SlotSetting _) => SaveSlots();

    private void SaveSlots()
    {
        _settings.Editor.LineSlots = Slots.Select(s => s.Selected == NoSlot ? "" : s.Selected).ToList();
        Changed(SettingsArea.LineSlots);
    }

    [RelayCommand]
    private void ResetLineSlots()
    {
        for (int i = 0; i < Slots.Count; i++)
        {
            string t = LineSlots.Default(_types, i);
            Slots[i].Set(t.Length == 0 ? NoSlot : t);
        }
        _settings.Editor.LineSlots = new();   // 비우면 기본값 (드롭다운 순서)
        Changed(SettingsArea.LineSlots);
    }

    // ------------------------------------------------------------------ 경로

    // 변경(5회차): Resources 폴더 하나만 지정, 나머지는 그 아래에서 찾음 (보기만)
    public string ResourcesFolderText => ShowPath(_settings.Editor.GameResourcesFolder);
    public string ScenarioFolderText => ShowSub(_settings.Editor.ScenarioFolder);
    public string GameSpineFolderText => ShowSub(_settings.Editor.GameSpineFolder);
    public string BackgroundFolderText => ShowSub(_settings.Editor.BackgroundFolder);

    /// <summary>추가: 하위 폴더 표시 — 지정 전이면 안내, 폴더가 없으면 (폴더 없음)</summary>
    private static string ShowSub(string? path) =>
        path == null ? "(Resources 폴더를 지정하세요)" : Directory.Exists(path) ? path : $"{path}   (폴더 없음)";

    /// <summary>Spine 데이터 폴더 (보기만 — 바꾸면 Spine 목록·무대·로그 위치가 모두 바뀌어 재시작 필요)</summary>
    public string SpineDataFolderText => _settings.SaveFolder;

    /// <summary>실제 설정 파일 (TE가 직접 실행한 상태에서 여는 경로)</summary>
    public string SettingsFileText => AppSettings.SettingsPath;

    /// <summary>경로 구역 안내</summary>
    [ObservableProperty] public partial string PathNote { get; private set; }

    private static string ShowPath(string? path) => string.IsNullOrWhiteSpace(path) ? "(지정 안 함)" : path;

    // 변경(5회차): Resources 폴더만 지정 (이전: PickScenarioFolder · PickGameSpineFolder)
    [RelayCommand]
    private void PickResourcesFolder() => SettingsFlow.PickResourcesFolder(_settings, _dialogs, _saveSettings, _messenger);

    [RelayCommand] private void OpenResourcesFolder() => OpenFolder(_settings.Editor.GameResourcesFolder);   // 추가
    [RelayCommand] private void OpenBackgroundFolder() => OpenFolder(_settings.Editor.BackgroundFolder);     // 추가
    [RelayCommand] private void OpenScenarioFolder() => OpenFolder(_settings.Editor.ScenarioFolder);
    [RelayCommand] private void OpenGameSpineFolder() => OpenFolder(_settings.Editor.GameSpineFolder);
    [RelayCommand] private void OpenSpineDataFolder() => OpenFolder(_settings.SaveFolder);
    [RelayCommand] private void OpenSettingsFolder() => OpenFolder(AppSettings.SettingsFolder);

    private void OpenFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            PathNote = "폴더가 지정되지 않았거나 없습니다";
            return;
        }
        PathNote = "";
        try { _dialogs.OpenInExplorer(folder); }
        catch (Exception ex) { _log.Error("탐색기 열기 실패", ex); }
    }

    // ------------------------------------------------------------------ 추가(번역 모드 2회차): 게임 데이터 복사본

    /// <summary>복사본 폴더 (exe 옆 Data\)</summary>
    public string GameDataFolderText => _gameData?.Folder ?? "";

    /// <summary>파일별 상태 (게임과 같음 / 다름 / 없음)</summary>
    public IReadOnlyList<GameDataFileState> GameDataStates => _gameData?.States ?? [];

    [ObservableProperty] public partial string GameDataNote { get; private set; }

    /// <summary>[게임 데이터 다시 복사]: 확인 → 게임 Resources\Data 원본으로 덮어쓰고 바로 다시 읽음</summary>
    [RelayCommand]
    private void CopyGameData()
    {
        if (_gameData == null) return;
        if (_settings.Editor.GameResourcesFolder is not { Length: > 0 })
        {
            GameDataNote = "게임 Resources 폴더를 먼저 지정하세요";
            return;
        }
        if (!_dialogs.Confirm("게임 데이터 다시 복사",
                $"게임 Resources\\Data의 {string.Join(" · ", GameData.Files)}를\nTE 복사본({_gameData.Folder})에 덮어씁니다. 계속할까요?\n\n(바로 다시 읽어 적용합니다)"))
            return;
        int n = _gameData.CopyAllFromGame();
        GameDataNote = n == GameData.Files.Count ? $"{n}개 복사·적용 완료" : $"{n}/{GameData.Files.Count}개 복사 — 나머지는 로그 확인";
    }

    [RelayCommand] private void OpenGameDataFolder() => OpenFolder(_gameData?.Folder);

    // ------------------------------------------------------------------ 추가(7회차): 화면 배치 (스크립트 탭 경계선 크기)

    /// <summary>지금 배치 (스크립트 탭에서 경계선을 끌면 알림을 받아 갱신)</summary>
    public string LayoutText
    {
        get
        {
            var l = _settings.ScriptLayout;
            double total = l.StageWeight + l.TableWeight;
            int stage = total > 0 ? (int)Math.Round(l.StageWeight / total * 100) : 60;
            return $"스크립트 탭: Spine 패널 {l.ActorWidth:0}px · 오디오 패널 {l.AudioWidth:0}px · 무대 영역 {stage}% / 스크립트 표 {100 - stage}%";
        }
    }

    /// <summary>[스테이지 레이아웃] 추가: 무대 대사창 안내선 (점선) 켜기/끄기</summary>
    public bool ShowDialogGuide
    {
        get => _settings.Editor.ShowDialogGuide;
        set
        {
            if (_settings.Editor.ShowDialogGuide == value) return;
            _settings.Editor.ShowDialogGuide = value;
            Changed(SettingsArea.StageGuide);
            OnPropertyChanged();
        }
    }

    /// <summary>[배치 기본값으로] — 패널 350 · 350, 무대 영역 : 표 = 3 : 2</summary>
    [RelayCommand]
    private void ResetLayout()
    {
        _settings.ScriptLayout.ResetToDefaults();   // 객체는 그대로 (스크립트 탭이 바인딩 중)
        Changed(SettingsArea.ScriptLayout);
    }

    // ------------------------------------------------------------------ 추가(번역 모드 4회차): 가번역 (Claude Code)

    /// <summary>지정한 claude 경로 (비우면 자동)</summary>
    public string ClaudePathText => ShowPath(_settings.PreTranslate.ClaudePath);

    /// <summary>실제로 실행할 claude (지정 → 기본 설치 위치 → PATH)</summary>
    public string ClaudeResolvedText => ClaudeCli.Resolve(_settings.PreTranslate.ClaudePath) is { } p
        ? p
        : string.IsNullOrWhiteSpace(_settings.PreTranslate.ClaudePath)
            ? $"(찾지 못함 — {ClaudeCli.DefaultInstallPath}에도 PATH에도 없음. Claude Code를 설치하거나 경로를 지정하세요)"
            : "(지정한 파일이 없음)";

    /// <summary>제한 시간 (분, 정수)</summary>
    public string PreTranslateTimeoutText
    {
        get => _settings.PreTranslate.TimeoutMinutes.ToString();
        set
        {
            if (int.TryParse(value?.Trim(), out int v) && v >= PreTranslateSettings.MinTimeout && v <= PreTranslateSettings.MaxTimeout)
            {
                PreTranslateNote = "";
                if (v != _settings.PreTranslate.TimeoutMinutes)
                {
                    _settings.PreTranslate.TimeoutMinutes = v;
                    Changed(SettingsArea.PreTranslate);
                }
            }
            else PreTranslateNote = $"제한 시간: {PreTranslateSettings.MinTimeout}~{PreTranslateSettings.MaxTimeout} 사이의 정수만 입력할 수 있어 원래 값으로 되돌렸습니다";
            OnPropertyChanged();
        }
    }

    [ObservableProperty] public partial string PreTranslateNote { get; private set; }

    /// <summary>[claude 지정…]: claude.exe(또는 claude.cmd)를 고름</summary>
    [RelayCommand]
    private void PickClaude()
    {
        string? start = ClaudeCli.Resolve(_settings.PreTranslate.ClaudePath) is { } p ? Path.GetDirectoryName(p) : null;
        string? picked = _dialogs.PickFile("Claude Code 실행 파일", "Claude Code (claude.exe;claude.cmd)|claude.exe;claude.cmd|실행 파일 (*.exe;*.cmd)|*.exe;*.cmd", start);
        if (picked == null) return;
        _settings.PreTranslate.ClaudePath = picked;
        PreTranslateNote = "";
        Changed(SettingsArea.PreTranslate);
        RefreshPreTranslate();
    }

    /// <summary>[기본값으로]: 경로 자동 · 제한 시간 5분</summary>
    [RelayCommand]
    private void ResetPreTranslate()
    {
        var d = new PreTranslateSettings();
        _settings.PreTranslate.ClaudePath = d.ClaudePath;
        _settings.PreTranslate.TimeoutMinutes = d.TimeoutMinutes;
        PreTranslateNote = "기본값으로 되돌렸습니다";
        Changed(SettingsArea.PreTranslate);
        RefreshPreTranslate();
        OnPropertyChanged(nameof(PreTranslateTimeoutText));
    }

    /// <summary>[다시 찾기]: 설치 후 TE를 끄지 않고 확인</summary>
    [RelayCommand]
    private void RefreshPreTranslate()
    {
        OnPropertyChanged(nameof(ClaudePathText));
        OnPropertyChanged(nameof(ClaudeResolvedText));
    }

    // ------------------------------------------------------------------ 추가(번역 모드 5회차): 보이스 (VOICEVOX 매칭·내보내기 폴더)

    private readonly VoiceMatchData? _voices;
    private readonly string _voicePath;

    /// <summary>VoiceMatch.xml 경로 (exe 옆)</summary>
    public string VoiceMatchPathText => _voicePath;

    /// <summary>드롭다운 항목 = (없음) + VOICEVOX 캐릭터 (VoiceMatch.xml 순서, 이용 제한이 있으면 ⚠)</summary>
    [ObservableProperty] public partial IReadOnlyList<VoiceOption> VoiceOptions { get; private set; }

    /// <summary>맨 위 기본 화자 + CharacterData 인물 순서</summary>
    [ObservableProperty] public partial IReadOnlyList<VoiceRow> VoiceRows { get; private set; }

    [ObservableProperty] public partial string VoiceNote { get; private set; }

    /// <summary>TXT·라이선스 저장 폴더 (비우면 exe 옆 VoiceExport\)</summary>
    public string VoiceExportFolderText => string.IsNullOrWhiteSpace(_settings.Voice.ExportFolder)
        ? $"{VoiceExport.DefaultFolder}   (기본)"
        : _settings.Voice.ExportFolder;

    private void BuildVoiceRows()
    {
        if (_voices == null) return;
        VoiceOptions = [VoiceOption.None, .. _voices.Voices.Select(v => new VoiceOption(v.Id, v.IsRestricted ? $"{v.Name}  ⚠ {v.UseText}" : v.Name, v))];
        var rows = new List<VoiceRow> { new(this, null, "기본 화자 (매칭 없는 줄)", _voices.Default) };
        if (_gameData?.Characters is { } chars)   //[공유 코드 통합] 수정: 이름은 기본 언어 표시 이름 (게임과 같은 대체 순서, 없으면 Id)
            foreach (var c in chars.All)
                rows.Add(new(this, c.Id, $"{chars.Name(c.Id, chars.DefaultLanguage)}  ({c.Id})", _voices.MatchOf(c.Id)));
        VoiceRows = rows;
        VoiceNote = _voices.Problem;
    }

    internal VoiceOption OptionOf(string voiceId) =>
        VoiceOptions.FirstOrDefault(o => o.Id.Equals(voiceId, StringComparison.OrdinalIgnoreCase))
        ?? (voiceId.Length == 0 ? VoiceOption.None : new VoiceOption(voiceId, $"{voiceId}  ⚠ VoiceMatch.xml에 없음", null));

    /// <summary>행에서 바꿈 → VoiceMatch.xml에 바로 저장</summary>
    internal void OnVoiceRowChanged(VoiceRow row)
    {
        if (_voices == null || !_voices.SetMatch(row.CharId, row.Selected.Id, row.Style)) return;
        try
        {
            _voices.Save(_voicePath);
            VoiceNote = $"저장했습니다: {row.Label} → {(row.Selected.Voice is { } v ? VoiceMatchData.SpeakerLine(v, row.Style) : "(없음)")}"
                        + (row.Selected.Voice?.Warning is { } w ? $"  ⚠ {w}" : "");
        }
        catch (Exception ex)
        {
            _log.Error("VoiceMatch.xml 저장 실패", ex);
            VoiceNote = $"⚠ 저장하지 못했습니다: {ex.Message}";
        }
    }

    /// <summary>[다시 읽기]: VoiceMatch.xml을 직접 고친 뒤</summary>
    [RelayCommand]
    private void ReloadVoiceMatch()
    {
        if (_voices == null) return;
        _voices.ReloadFrom(_voicePath);
        BuildVoiceRows();
        if (VoiceNote.Length == 0) VoiceNote = $"다시 읽었습니다 (VOICEVOX 캐릭터 {_voices.Voices.Count}명)";
    }

    [RelayCommand] private void OpenVoiceMatchFolder() => OpenFolder(Path.GetDirectoryName(_voicePath));

    [RelayCommand]
    private void PickVoiceExportFolder()
    {
        string? picked = _dialogs.PickFolder("VOICEVOX 내보내기 폴더", string.IsNullOrWhiteSpace(_settings.Voice.ExportFolder) ? null : _settings.Voice.ExportFolder);
        if (picked == null) return;
        _settings.Voice.ExportFolder = picked;
        Changed(SettingsArea.Voice);
        OnPropertyChanged(nameof(VoiceExportFolderText));
    }

    [RelayCommand]
    private void ResetVoiceExportFolder()
    {
        _settings.Voice.ExportFolder = null;
        Changed(SettingsArea.Voice);
        OnPropertyChanged(nameof(VoiceExportFolderText));
    }

    [RelayCommand]
    private void OpenVoiceExportFolder()
    {
        string dir = string.IsNullOrWhiteSpace(_settings.Voice.ExportFolder) ? VoiceExport.DefaultFolder : _settings.Voice.ExportFolder;
        try { Directory.CreateDirectory(dir); } catch (Exception ex) { _log.Error("내보내기 폴더 만들기 실패", ex); }
        OpenFolder(dir);
        if (PathNote.Length > 0) { VoiceNote = PathNote; PathNote = ""; }
    }

    // ------------------------------------------------------------------ 저장·알림

    private void Changed(SettingsArea area)
    {
        _saveSettings();
        _messenger.Send(new SettingsChangedMessage(area));
    }

    private void OnSettingsChanged(SettingsChangedMessage m)
    {
        if (m.Area == SettingsArea.ScriptLayout) { OnPropertyChanged(nameof(LayoutText)); return; }   // 추가(7회차)
        if (m.Area != SettingsArea.ResourcesFolder) return;   // 변경(5회차)
        _gameData?.Refresh();   // 추가(번역 모드 2회차): 게임 데이터도 다시 비교 (없는 복사본은 채움)
        GameDataNote = "";
        PathNote = "";
        OnPropertyChanged(nameof(ResourcesFolderText));
        OnPropertyChanged(nameof(ScenarioFolderText));
        OnPropertyChanged(nameof(GameSpineFolderText));
        OnPropertyChanged(nameof(BackgroundFolderText));
    }
}

/// <summary>추가: 설정 탭의 조합키 슬롯 1개 (드롭다운). 바꾸면 SettingsViewModel이 저장·알림</summary>
public sealed class SlotSetting : ObservableObject
{
    private readonly Action<SlotSetting> _changed;
    private string _selected;

    internal SlotSetting(int number, string selected, Action<SlotSetting> changed)
    {
        Number = number;
        _selected = selected;
        _changed = changed;
    }

    public int Number { get; }

    /// <summary>고른 종류 (빈 값은 무시 — 목록 재사용 중 드롭다운이 잠깐 비는 경우)</summary>
    public string Selected
    {
        get => _selected;
        set
        {
            if (string.IsNullOrEmpty(value) || value == _selected) return;
            _selected = value;
            OnPropertyChanged();
            _changed(this);
        }
    }

    /// <summary>저장·알림 없이 값만 (기본값으로 되돌릴 때 — 저장은 한 번에)</summary>
    internal void Set(string value)
    {
        if (value == _selected) return;
        _selected = value;
        OnPropertyChanged(nameof(Selected));
    }
}

/// <summary>추가(번역 모드 5회차): 설정 탭 '보이스' 드롭다운 항목 (Id 빈 값 = 없음)</summary>
public sealed record VoiceOption(string Id, string Display, VoiceInfo? Voice)
{
    public static readonly VoiceOption None = new("", "(없음)", null);
    public override string ToString() => Display;
}

/// <summary>추가(번역 모드 5회차): 설정 탭 '보이스' 1행 (CharId = null이면 기본 화자). 바꾸면 SettingsViewModel이 VoiceMatch.xml에 저장</summary>
public sealed class VoiceRow : ObservableObject
{
    private readonly SettingsViewModel _owner;
    private VoiceOption _selected;
    private string _style;

    internal VoiceRow(SettingsViewModel owner, string? charId, string label, VoiceMatchEntry entry)
    {
        _owner = owner;
        CharId = charId;
        Label = label;
        _selected = owner.OptionOf(entry.Voice);
        _style = entry.Style;
    }

    public string? CharId { get; }
    public string Label { get; }

    public VoiceOption Selected
    {
        get => _selected;
        set
        {
            if (value == null || value == _selected) return;   // 목록을 다시 만들 때 잠깐 null
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Warning));
            _owner.OnVoiceRowChanged(this);
        }
    }

    /// <summary>스타일 (비우면 기본 스타일) — VOICEVOX 화면의 스타일 이름 그대로 (예: あまあま)</summary>
    public string Style
    {
        get => _style;
        set
        {
            value = (value ?? "").Trim();
            if (value == _style) return;
            _style = value;
            OnPropertyChanged();
            _owner.OnVoiceRowChanged(this);
        }
    }

    /// <summary>이용 제한 안내 (빨간 글씨, 없으면 빈 값)</summary>
    public string Warning => _selected.Voice?.Warning is { } w ? "⚠ " + w : _selected.Voice == null && _selected.Id.Length > 0 ? "⚠ VoiceMatch.xml에 없는 화자" : "";
}
