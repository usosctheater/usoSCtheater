using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SCResourceGrabber.Models;

/// <summary>
/// 추적 창의 한 줄. 같은 URL은 한 줄로 합치고, 로드/재생 여부와 재생 횟수를 누적한다.
/// </summary>
public class TrackedItem : INotifyPropertyChanged
{
    public required string Url { get; init; }
    public DateTime FirstSeen { get; init; } = DateTime.Now;

    private CapturedResource? _resource;
    /// <summary>메인 목록의 캡처 항목. 캡처되지 않은 URL(필터 제외 등)이면 null.</summary>
    public CapturedResource? Resource
    {
        get => _resource;
        set { if (Set(ref _resource, value)) { Notify(nameof(FileName)); Notify(nameof(Category)); Notify(nameof(CategoryLabel)); Notify(nameof(Size)); Notify(nameof(SizeText)); } }
    }

    public string FileName
    {
        get
        {
            if (Resource != null) return Resource.FileName;
            try { return Uri.UnescapeDataString(new Uri(Url).AbsolutePath.Split('/').LastOrDefault() ?? Url); }
            catch { return Url; }
        }
    }

    /// <summary>필터용 분류. 캡처되지 않은 URL은 재생된 적이 있으면 오디오, 아니면 기타로 본다.</summary>
    public ResourceCategory Category => Resource?.Category ?? (PlayCount > 0 ? ResourceCategory.Audio : ResourceCategory.Other);

    public string CategoryLabel => Resource?.CategoryLabel ?? "(목록에 없음)";
    public long Size => Resource?.Size ?? 0;
    public string SizeText => Resource?.SizeText ?? "";
    public string FirstSeenText => FirstSeen.ToString("HH:mm:ss");

    private bool _loaded;
    /// <summary>추적 중에 새로 받은 리소스</summary>
    public bool Loaded { get => _loaded; set { if (Set(ref _loaded, value)) Notify(nameof(SourceText)); } }

    private int _playCount;
    public int PlayCount
    {
        get => _playCount;
        set { if (Set(ref _playCount, value)) { Notify(nameof(SourceText)); Notify(nameof(PlayCountText)); Notify(nameof(Category)); } }
    }

    private DateTime? _lastPlayed;
    public DateTime? LastPlayed { get => _lastPlayed; set { if (Set(ref _lastPlayed, value)) Notify(nameof(LastPlayedText)); } }

    private int _activePlays;
    /// <summary>현재 재생 중인 인스턴스 수 (같은 소리가 겹쳐 재생될 수 있음)</summary>
    public int ActivePlays
    {
        get => _activePlays;
        set { if (Set(ref _activePlays, Math.Max(0, value))) { Notify(nameof(IsPlaying)); Notify(nameof(PlayingText)); } }
    }

    public bool IsPlaying => ActivePlays > 0;
    public string PlayingText => IsPlaying ? "▶ 재생 중" : "";
    public string PlayCountText => PlayCount > 0 ? PlayCount.ToString() : "";
    public string LastPlayedText => LastPlayed?.ToString("HH:mm:ss") ?? "";
    public string SourceText => (Loaded, PlayCount > 0) switch
    {
        (true, true) => "로드+재생",
        (true, false) => "로드",
        (false, true) => "재생",
        _ => ""
    };

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name!);
        return true;
    }
}
