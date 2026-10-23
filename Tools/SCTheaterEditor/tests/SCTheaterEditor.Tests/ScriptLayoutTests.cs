using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;

namespace SCTheaterEditor.Tests;

/// <summary>추가(7회차): 스크립트 탭 화면 배치 — 경계선 크기 저장 · 범위 · 설정 탭 [배치 기본값으로]</summary>
public class ScriptLayoutTests
{
    private sealed class Env
    {
        public readonly AppSettings Settings = new();
        public readonly StrongReferenceMessenger Messenger = new();
        public readonly List<SettingsArea> Messages = new();
        public readonly ScriptLayoutViewModel Layout;
        public readonly SettingsViewModel Page;
        public int Saves;

        public Env()
        {
            Messenger.Register<SettingsChangedMessage>(Messages, (r, m) => ((List<SettingsArea>)r).Add(m.Area));
            Layout = new ScriptLayoutViewModel(Settings, () => Saves++, Messenger);
            Page = new SettingsViewModel(Settings, new FakeDialogs(), () => Saves++, Messenger, new AppLog());
        }
    }

    [Fact]
    public void Defaults_SamePanelWidths()
    {
        var env = new Env();
        Assert.Equal(350, env.Layout.ActorWidth);
        Assert.Equal(350, env.Layout.AudioWidth);
        Assert.Equal((3.0, 2.0), (env.Layout.StageWeight, env.Layout.TableWeight));
        Assert.Contains("Spine 패널 350px", env.Page.LayoutText);
        Assert.Contains("무대 영역 60%", env.Page.LayoutText);
    }

    [Fact]
    public void Drag_SavesFitsAndNotifiesSettingsTab()
    {
        var env = new Env();
        var changed = new List<string?>();
        env.Layout.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        env.Layout.ActorWidth = 420.4;                                     // 끌기 → 반올림 저장
        Assert.Equal(420, env.Settings.ScriptLayout.ActorWidth);
        Assert.Equal(1, env.Saves);
        Assert.Equal(new[] { SettingsArea.ScriptLayout }, env.Messages);
        Assert.Contains("Spine 패널 420px", env.Page.LayoutText);          // 설정 탭 표시도
        Assert.DoesNotContain(string.Empty, changed);                      // 자기 알림으로 전부 다시 읽지 않음

        env.Layout.ActorWidth = 420.2;                                     // 같은 값 → 저장 안 함
        Assert.Equal(1, env.Saves);
        env.Layout.AudioWidth = 50;                                        // 범위 250~1200
        Assert.Equal(250, env.Settings.ScriptLayout.AudioWidth);
        env.Layout.AudioWidth = 5000;
        Assert.Equal(1200, env.Settings.ScriptLayout.AudioWidth);
        env.Layout.StageWeight = 412.5;                                    // 경계선이 비율을 px 크기로 바꿔도 비율로 저장
        env.Layout.TableWeight = 275;
        Assert.Contains("무대 영역 60%", env.Page.LayoutText);
        env.Layout.TableWeight = 0;                                        // 잘못된 값 → 1
        Assert.Equal(1, env.Settings.ScriptLayout.TableWeight);
    }

    [Fact]
    public void ResetFromSettingsTab_RereadsLayout_SameObject()
    {
        var env = new Env();
        var obj = env.Settings.ScriptLayout;
        env.Layout.ActorWidth = 500;
        env.Layout.StageWeight = 5;
        var changed = new List<string?>();
        env.Layout.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        env.Page.ResetLayoutCommand.Execute(null);
        Assert.Same(obj, env.Settings.ScriptLayout);                       // 객체는 그대로 (화면이 바인딩 중)
        Assert.Equal(350, env.Layout.ActorWidth);
        Assert.Equal(3, env.Layout.StageWeight);
        Assert.Contains(string.Empty, changed);                            // 스크립트 탭이 전부 다시 읽음
        Assert.Equal(SettingsArea.ScriptLayout, env.Messages[^1]);
    }

    [Fact]
    public void SavedLayout_LoadsBack_OldFileWithoutSectionUsesDefaults()
    {
        using var t = new TempFolder();
        string path = Path.Combine(t.Path, "settings.json");
        var s = new AppSettings();
        s.ScriptLayout.ActorWidth = 400;
        s.ScriptLayout.AudioWidth = 300;
        s.ScriptLayout.StageWeight = 1.5;
        s.Save(path);
        var loaded = AppSettings.Load(path, null);
        Assert.Equal((400.0, 300.0, 1.5, 2.0),
                     (loaded.ScriptLayout.ActorWidth, loaded.ScriptLayout.AudioWidth, loaded.ScriptLayout.StageWeight, loaded.ScriptLayout.TableWeight));

        File.WriteAllText(path, """{ "Editor": { "BackupCount": 7 } }""");   // 7회차 전 설정 파일
        Assert.Equal(350, AppSettings.Load(path, null).ScriptLayout.AudioWidth);
    }
}
