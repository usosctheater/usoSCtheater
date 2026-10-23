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

    public SettingsViewModel(AppSettings settings, IDialogService dialogs, Action saveSettings, IMessenger messenger, AppLog log,
                             LineTypeCatalog? types = null)   // 변경: types 추가 (생략하면 내장 LineTypes.xml)
    {
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

    /// <summary>[배치 기본값으로] — 패널 350 · 350, 무대 영역 : 표 = 3 : 2</summary>
    [RelayCommand]
    private void ResetLayout()
    {
        _settings.ScriptLayout.ResetToDefaults();   // 객체는 그대로 (스크립트 탭이 바인딩 중)
        Changed(SettingsArea.ScriptLayout);
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
