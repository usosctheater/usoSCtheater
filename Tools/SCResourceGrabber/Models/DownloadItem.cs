using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SCResourceGrabber.Models;

/// <summary>다운로드 창 목록 한 줄 (캡처 항목 + 저장할 이름)</summary>
public class DownloadItem : INotifyPropertyChanged
{
    public required CapturedResource Resource { get; init; }

    private int _index;
    public int Index { get => _index; set => Set(ref _index, value); }

    private string _saveName = "";
    /// <summary>확장자 없는 저장 이름. 비어 있으면 원본 이름 사용</summary>
    public string SaveName { get => _saveName; set { if (Set(ref _saveName, value)) OnPropertyChanged(nameof(SaveNameText)); } }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    public bool IsImage => Resource.Category == ResourceCategory.Image;
    public ImageSource? Thumbnail => Resource.Thumbnail;
    public string OriginalName => Resource.FileName;
    public string OriginalStem => Path.GetFileNameWithoutExtension(Resource.FileName);
    public string CategoryLabel => Resource.CategoryLabel;

    /// <summary>목록 표시용: 이름이 비어 있으면 흐리게 보일 안내 문구</summary>
    public string SaveNameText => SaveName.Length > 0 ? SaveName : "(원본 이름)";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name!);
        return true;
    }
}
