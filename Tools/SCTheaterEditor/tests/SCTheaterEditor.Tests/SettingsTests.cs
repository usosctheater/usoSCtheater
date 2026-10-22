using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Models;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>추가: 설정 탭 — 숫자 검사·기본값, 설정 변경 알림으로 기능이 따라 바뀌는지 (설정 관리 규칙)</summary>
public class SettingsViewModelTests
{
    private sealed class Env : IDisposable
    {
        public readonly TempFolder Temp = new();
        public readonly AppSettings Settings = new();
        public readonly FakeDialogs Dialogs = new();
        public readonly StrongReferenceMessenger Messenger = new();   // 테스트마다 전용 (서로 섞이지 않게)
        public readonly List<SettingsArea> Messages = new();
        public readonly SettingsViewModel Vm;
        public int Saves;

        public Env()
        {
            Messenger.Register<SettingsChangedMessage>(Messages, (r, m) => ((List<SettingsArea>)r).Add(m.Area));
            Vm = new SettingsViewModel(Settings, Dialogs, () => Saves++, Messenger, new AppLog());
        }

        public SpineLibrary EmptyLibrary()
        {
            var lib = new SpineLibrary(Path.Combine(Temp.Path, "SpineData"), new SpineManifest());
            lib.Rebuild();
            return lib;
        }

        public void Dispose() => Temp.Dispose();
    }

    [Fact]
    public void Numbers_RejectInvalid_ApplyValid_SaveOnlyWhenChanged()
    {
        using var env = new Env();
        foreach (var bad in new[] { "abc", "0", "1001", "", "1.5" })
        {
            env.Vm.AutoBackupEditsText = bad;
            Assert.Equal(30, env.Settings.Editor.AutoBackupEdits);
            Assert.NotEqual("", env.Vm.ScriptEditingNote);                 // 원래 값으로 되돌리고 안내
            Assert.Equal("30", env.Vm.AutoBackupEditsText);
        }
        Assert.Equal(0, env.Saves);
        Assert.Empty(env.Messages);

        env.Vm.AutoBackupEditsText = " 45 ";
        Assert.Equal(45, env.Settings.Editor.AutoBackupEdits);
        Assert.Equal("", env.Vm.ScriptEditingNote);
        Assert.Equal(1, env.Saves);
        Assert.Equal(new[] { SettingsArea.ScriptEditing }, env.Messages);

        env.Vm.AutoBackupEditsText = "45";                                // 같은 값 → 저장·알림 없음
        Assert.Equal(1, env.Saves);

        env.Vm.AutoBackupMinutesText = "121";
        Assert.Equal(10, env.Settings.Editor.AutoBackupMinutes);
        env.Vm.AutoBackupMinutesText = "5";
        Assert.Equal(5, env.Settings.Editor.AutoBackupMinutes);
        env.Vm.BackupCountText = "201";
        Assert.Equal(20, env.Settings.Editor.BackupCount);
        env.Vm.BackupCountText = "200";
        Assert.Equal(200, env.Settings.Editor.BackupCount);
        Assert.Equal(3, env.Saves);
    }

    [Fact]
    public void ResetScriptEditing_RestoresDefaults()
    {
        using var env = new Env();
        env.Vm.AutoBackupEditsText = "7";
        env.Vm.AutoBackupMinutesText = "3";
        env.Vm.BackupCountText = "50";
        int saves = env.Saves;
        env.Vm.ResetScriptEditingCommand.Execute(null);
        Assert.Equal(30, env.Settings.Editor.AutoBackupEdits);
        Assert.Equal(10, env.Settings.Editor.AutoBackupMinutes);
        Assert.Equal(20, env.Settings.Editor.BackupCount);
        Assert.Equal(saves + 1, env.Saves);
        Assert.Equal(SettingsArea.ScriptEditing, env.Messages[^1]);
        Assert.Equal("30", env.Vm.AutoBackupEditsText);
    }

    [Fact]
    public void ScenarioFolder_FromSettingsTab_RefreshesActList_CancelChangesNothing()
    {
        using var env = new Env();
        string scenario = Path.Combine(env.Temp.Path, "Scenario");
        Directory.CreateDirectory(Path.Combine(scenario, "IL"));
        File.WriteAllText(Path.Combine(scenario, "IL", "IL01.xml"), ActXmlFormatTests.Sample);
        var editor = new ActEditorViewModel(env.Settings, env.EmptyLibrary(), env.Dialogs, new ImmediateDispatcher(), () => { },
                                            new AppLog(), Path.Combine(env.Temp.Path, "Backups"), messenger: env.Messenger);
        Assert.Empty(editor.ActEntries);

        env.Dialogs.PickResult = null;                                     // 취소
        env.Vm.PickScenarioFolderCommand.Execute(null);
        Assert.Null(env.Settings.Editor.ScenarioFolder);
        Assert.Equal(0, env.Saves);
        Assert.Empty(env.Messages);

        env.Dialogs.PickResult = scenario;
        env.Vm.PickScenarioFolderCommand.Execute(null);
        Assert.Equal(scenario, env.Settings.Editor.ScenarioFolder);
        Assert.Equal("IL / IL01", editor.ActEntries.Single().Display);    // 스크립트 표가 알림을 받아 목록 다시 읽음
        Assert.Contains(scenario, editor.FolderText);
        Assert.Equal(scenario, env.Vm.ScenarioFolderText);
        Assert.Equal(new[] { SettingsArea.ScenarioFolder }, env.Messages);
    }

    [Fact]
    public void GameSpineFolder_FromResourceTab_UpdatesSettingsTab()
    {
        using var env = new Env();
        string spine = Path.Combine(env.Temp.Path, "Spine");
        Directory.CreateDirectory(spine);
        var resource = new ResourceViewModel(env.Settings, env.EmptyLibrary(), new List<IdolUnit>(), env.Dialogs, () => { },
                                             env.Messenger, new AppLog());
        Assert.Equal("(지정 안 함)", env.Vm.GameSpineFolderText);

        env.Dialogs.PickResult = spine;
        resource.PickGameFolderCommand.Execute(null);
        Assert.Equal(spine, env.Settings.Editor.GameSpineFolder);
        Assert.Equal(spine, env.Vm.GameSpineFolderText);                  // 설정 탭 표시도 같은 값
        Assert.Equal(new[] { SettingsArea.GameSpineFolder }, env.Messages);
    }
}
