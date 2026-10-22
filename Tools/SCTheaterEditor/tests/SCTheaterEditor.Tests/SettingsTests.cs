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

    // ------------------------------------------------------------------ 변경(5회차): 게임 Resources 폴더 하나

    /// <summary>게임 Resources 폴더 흉내 (Scenario\IL\IL01.xml · Spine\ · BG\bg_a.png)</summary>
    private static string MakeResources(string parent, string name = "Resources")
    {
        string res = Path.Combine(parent, name);
        Directory.CreateDirectory(Path.Combine(res, "Scenario", "IL"));
        File.WriteAllText(Path.Combine(res, "Scenario", "IL", "IL01.xml"), ActXmlFormatTests.Sample);
        Directory.CreateDirectory(Path.Combine(res, "Spine"));
        Directory.CreateDirectory(Path.Combine(res, "BG"));
        File.WriteAllBytes(Path.Combine(res, "BG", "bg_a.png"), [1, 2, 3]);
        return res;
    }

    [Fact]
    public void SubFolders_ComputedFromResources()
    {
        var e = new EditorSettings();
        Assert.Null(e.ScenarioFolder);
        Assert.Null(e.GameSpineFolder);
        Assert.Null(e.BackgroundFolder);
        e.GameResourcesFolder = @"D:\game\Assets\Resources";
        Assert.Equal(@"D:\game\Assets\Resources\Scenario", e.ScenarioFolder);
        Assert.Equal(@"D:\game\Assets\Resources\Spine", e.GameSpineFolder);
        Assert.Equal(@"D:\game\Assets\Resources\BG", e.BackgroundFolder);
    }

    [Fact]
    public void Save_WritesOnlyResources_OldKeysIgnoredOnLoad()
    {
        using var temp = new TempFolder();
        string path = Path.Combine(temp.Path, "settings.json");
        var s = new AppSettings();
        s.Editor.GameResourcesFolder = @"D:\game\Assets\Resources";
        s.Save(path);
        string json = File.ReadAllText(path);
        Assert.Contains("GameResourcesFolder", json);
        Assert.DoesNotContain("ScenarioFolder", json);                     // 계산 값은 저장하지 않음
        Assert.DoesNotContain("BackgroundFolder", json);

        // 옛 키가 남은 설정 파일: 문제없이 읽고 옛 값은 무시 (자동 추측하지 않음)
        File.WriteAllText(path, """{ "Editor": { "ScenarioFolder": "D:\\old\\Scenario", "GameSpineFolder": "D:\\old\\Spine", "BackupCount": 7 } }""");
        var loaded = AppSettings.Load(path, null);
        Assert.Null(loaded.LoadNotice);
        Assert.Null(loaded.Editor.GameResourcesFolder);
        Assert.Null(loaded.Editor.ScenarioFolder);
        Assert.Equal(7, loaded.Editor.BackupCount);
    }

    [Fact]
    public void ResourcesFolder_FromSettingsTab_UpdatesActList_GameKeys_Backgrounds()
    {
        using var env = new Env();
        string res = MakeResources(env.Temp.Path);
        var lib = env.EmptyLibrary();
        var editor = new ActEditorViewModel(env.Settings, lib, env.Dialogs, new ImmediateDispatcher(), () => { },
                                            new AppLog(), Path.Combine(env.Temp.Path, "Backups"), messenger: env.Messenger);
        var resource = new ResourceViewModel(env.Settings, lib, new List<IdolUnit>(), env.Dialogs, () => { },
                                             env.Messenger, new AppLog());
        var stage = new FakeStage();
        var bg = new BackgroundLibrary(env.Settings.Editor.BackgroundFolder);
        var script = new ScriptViewModel(env.Settings, lib, bg, stage, LipRules.LoadEmbedded(), env.Messenger, () => { }, new AppLog());
        Assert.Empty(editor.ActEntries);
        Assert.Equal(new[] { ScriptViewModel.NoBackground }, script.BackgroundOptions);
        Assert.Equal("(지정 안 함)", env.Vm.ResourcesFolderText);
        Assert.Equal("(Resources 폴더를 지정하세요)", env.Vm.ScenarioFolderText);

        env.Dialogs.PickResult = null;                                     // 취소
        env.Vm.PickResourcesFolderCommand.Execute(null);
        Assert.Null(env.Settings.Editor.GameResourcesFolder);
        Assert.Equal(0, env.Saves);
        Assert.Empty(env.Messages);

        env.Dialogs.PickResult = res;
        env.Vm.PickResourcesFolderCommand.Execute(null);
        Assert.Equal(res, env.Settings.Editor.GameResourcesFolder);
        Assert.Equal(1, env.Saves);
        Assert.Equal(new[] { SettingsArea.ResourcesFolder }, env.Messages);
        Assert.Equal("IL / IL01", editor.ActEntries.Single().Display);    // 스크립트 표가 알림을 받아 목록 다시 읽음
        Assert.Contains(Path.Combine(res, "Scenario"), editor.FolderText);
        Assert.Equal(Path.Combine(res, "BG"), bg.Folder);                  // 무대 배경도 같은 알림으로
        Assert.Contains("bg_a", script.BackgroundOptions);
        Assert.Equal(res, env.Vm.ResourcesFolderText);
        Assert.Equal(Path.Combine(res, "Spine"), env.Vm.GameSpineFolderText);
        Assert.Equal(Path.Combine(res, "BG"), env.Vm.BackgroundFolderText);
    }

    [Fact]
    public void ResourcesFolder_NotNamedResources_ConfirmCancelChangesNothing()
    {
        using var env = new Env();
        string other = MakeResources(env.Temp.Path, "Other");
        env.Dialogs.PickResult = other;
        env.Dialogs.ConfirmResult = false;
        env.Vm.PickResourcesFolderCommand.Execute(null);
        Assert.Null(env.Settings.Editor.GameResourcesFolder);
        Assert.Equal(0, env.Saves);
        Assert.Empty(env.Messages);

        env.Dialogs.ConfirmResult = true;                                  // 확인하면 그대로 씀
        env.Vm.PickResourcesFolderCommand.Execute(null);
        Assert.Equal(other, env.Settings.Editor.GameResourcesFolder);
    }

    [Fact]
    public void SubFolderMissing_ShownAsMissing()
    {
        using var env = new Env();
        string res = Path.Combine(env.Temp.Path, "Resources");
        Directory.CreateDirectory(res);                                    // 하위 폴더 없음
        env.Dialogs.PickResult = res;
        env.Vm.PickResourcesFolderCommand.Execute(null);
        Assert.EndsWith("(폴더 없음)", env.Vm.ScenarioFolderText);
        Assert.False(Directory.Exists(Path.Combine(res, "BG")));          // 게임 폴더를 만들지 않음
    }

    [Fact]
    public void ExportToGame_WithoutResources_ShowsErrorOnly()
    {
        using var env = new Env();
        Assert.Null(GameExportFlow.EnsureFolder(env.Settings, env.Dialogs));
        Assert.Contains("설정 탭", env.Dialogs.Errors.Single());
        Assert.Equal(0, env.Saves);
        Assert.Empty(env.Messages);
    }
}
