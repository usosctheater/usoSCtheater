using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 추가: 설정 탭. 설정 관리 규칙(TE 핸드오프 0절) — 값은 AppSettings 한 곳, 바꾸는 로직은 여기(Core), 화면은 바인딩만,
/// 기능은 SettingsChangedMessage만 받음. 바뀌면 바로 저장(0.4초 모아서).
/// 구역: 스크립트 편집(자동 백업·보관 수) / 경로. 4회차에 조합키 슬롯, ④에 게임 Resources 폴더 추가 예정
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

    public SettingsViewModel(AppSettings settings, IDialogService dialogs, Action saveSettings, IMessenger messenger, AppLog log)
    {
        _settings = settings;
        _dialogs = dialogs;
        _saveSettings = saveSettings;
        _messenger = messenger;
        _log = log;
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

    // ------------------------------------------------------------------ 경로

    public string ScenarioFolderText => ShowPath(_settings.Editor.ScenarioFolder);
    public string GameSpineFolderText => ShowPath(_settings.Editor.GameSpineFolder);

    /// <summary>Spine 데이터 폴더 (보기만 — 바꾸면 Spine 목록·무대·로그 위치가 모두 바뀌어 재시작 필요)</summary>
    public string SpineDataFolderText => _settings.SaveFolder;

    /// <summary>실제 설정 파일 (TE가 직접 실행한 상태에서 여는 경로)</summary>
    public string SettingsFileText => AppSettings.SettingsPath;

    /// <summary>경로 구역 안내</summary>
    [ObservableProperty] public partial string PathNote { get; private set; }

    private static string ShowPath(string? path) => string.IsNullOrWhiteSpace(path) ? "(지정 안 함)" : path;

    [RelayCommand]
    private void PickScenarioFolder() => SettingsFlow.PickScenarioFolder(_settings, _dialogs, _saveSettings, _messenger);

    [RelayCommand]
    private void PickGameSpineFolder() => GameExportFlow.EnsureFolder(_settings, _dialogs, _saveSettings, _messenger, forcePick: true);

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

    // ------------------------------------------------------------------ 저장·알림

    private void Changed(SettingsArea area)
    {
        _saveSettings();
        _messenger.Send(new SettingsChangedMessage(area));
    }

    private void OnSettingsChanged(SettingsChangedMessage m)
    {
        switch (m.Area)
        {
            case SettingsArea.ScenarioFolder: OnPropertyChanged(nameof(ScenarioFolderText)); break;
            case SettingsArea.GameSpineFolder: OnPropertyChanged(nameof(GameSpineFolderText)); break;
        }
    }
}
