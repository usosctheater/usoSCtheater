using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 추가(7회차): 스크립트 탭 배치 (설정 관리 규칙 — 값은 AppSettings.ScriptLayout, 화면은 바인딩만).
/// 화면이 경계선 크기를 양방향 바인딩 → 바뀌면 범위를 맞춰 저장하고 SettingsChangedMessage(ScriptLayout) (설정 탭 표시 갱신).
/// 설정 탭 [배치 기본값으로] → 같은 알림을 받아 전부 다시 읽음
/// </summary>
public sealed class ScriptLayoutViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly IMessenger _messenger;

    public ScriptLayoutViewModel(AppSettings settings, Action saveSettings, IMessenger messenger)
    {
        _settings = settings;
        _save = saveSettings;
        _messenger = messenger;
        messenger.Register<ScriptLayoutViewModel, SettingsChangedMessage>(this, static (r, m) =>
        {
            // 전부 다시 읽음 — 경계선을 끄는 중 내가 보낸 알림은 건너뜀 (끄는 칸 크기를 다시 넣지 않도록)
            if (m.Area == SettingsArea.ScriptLayout && !r._sending) r.OnPropertyChanged(string.Empty);
        });
    }

    private ScriptLayoutSettings S => _settings.ScriptLayout;

    /// <summary>Spine 패널 폭 (px)</summary>
    public double ActorWidth
    {
        get => S.ActorWidth;
        set => Set(FitWidth(value), S.ActorWidth, v => S.ActorWidth = v);
    }

    /// <summary>오디오 패널 폭 (px)</summary>
    public double AudioWidth
    {
        get => S.AudioWidth;
        set => Set(FitWidth(value), S.AudioWidth, v => S.AudioWidth = v);
    }

    /// <summary>무대 영역 높이 비율 (스크립트 표와의 비)</summary>
    public double StageWeight
    {
        get => S.StageWeight;
        set => Set(FitWeight(value), S.StageWeight, v => S.StageWeight = v);
    }

    /// <summary>스크립트 표 높이 비율</summary>
    public double TableWeight
    {
        get => S.TableWeight;
        set => Set(FitWeight(value), S.TableWeight, v => S.TableWeight = v);
    }

    internal static double FitWidth(double v) =>
        double.IsFinite(v) ? Math.Clamp(Math.Round(v), ScriptLayoutSettings.MinPanelWidth, ScriptLayoutSettings.MaxPanelWidth) : ScriptLayoutSettings.MinPanelWidth;

    internal static double FitWeight(double v) => double.IsFinite(v) && v > 0 ? Math.Round(v, 3) : 1;

    private void Set(double value, double current, Action<double> apply, [CallerMemberName] string name = "")
    {
        if (value == current) return;
        apply(value);
        OnPropertyChanged(name);
        _save();   // 0.4초 모아 저장 (끄는 동안 여러 번 와도 한 번)
        _sending = true;
        try { _messenger.Send(new SettingsChangedMessage(SettingsArea.ScriptLayout)); }
        finally { _sending = false; }
    }

    private bool _sending;
}
