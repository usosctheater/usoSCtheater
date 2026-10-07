using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SCResourceGrabber.Models;

/// <summary>선언 순서 = 필터·정렬 순서</summary>
public enum ResourceCategory { Image, Audio, Video, Spine, Json, Font, Encrypted, Other }

/// <summary>
/// 네트워크에서 캡처한 리소스 1건. 본문은 메모리가 아니라 세션 캐시 파일(CachePath)에 보관한다.
/// </summary>
public class CapturedResource : INotifyPropertyChanged
{
    public required int Id { get; init; }
    public required string Url { get; init; }
    public required string Mime { get; init; }
    public required long Size { get; init; }
    public required ResourceCategory Category { get; init; }
    /// <summary>점 없는 확장자 (예: "png"). URL에 없으면 내용 기준으로 추정한 값.</summary>
    public required string Extension { get; init; }
    /// <summary>Spine 세부 구분 등 보조 정보 (예: "atlas", "skeleton json").</summary>
    public string SubType { get; init; } = "";
    public required string CachePath { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.Now;
    public string CapturedAtText => CapturedAt.ToString("HH:mm:ss");

    public string Host => new Uri(Url).Host;

    public string FileName
    {
        get
        {
            var name = Uri.UnescapeDataString(new Uri(Url).AbsolutePath.Split('/').LastOrDefault() ?? "");
            return string.IsNullOrEmpty(name) ? Host : name;
        }
    }

    public string CategoryLabel => CategoryLabels[Category] + (SubType.Length > 0 ? $" ({SubType})" : "");

    public string SizeText => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:0.0} KB",
        _ => $"{Size / 1048576.0:0.00} MB"
    };

    public static readonly IReadOnlyDictionary<ResourceCategory, string> CategoryLabels = new Dictionary<ResourceCategory, string>
    {
        [ResourceCategory.Image] = "이미지",
        [ResourceCategory.Audio] = "오디오",
        [ResourceCategory.Video] = "비디오",
        [ResourceCategory.Spine] = "Spine",
        [ResourceCategory.Json] = "JSON",
        [ResourceCategory.Font] = "폰트",
        [ResourceCategory.Encrypted] = "암호화",
        [ResourceCategory.Other] = "기타",
    };

    // ---------- 바인딩 상태 ----------

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    private string _status = "";
    /// <summary>"", "저장됨", "건너뜀", "실패" 등</summary>
    public string Status { get => _status; set => Set(ref _status, value); }

    private ImageSource? _thumbnail;
    private bool _thumbnailTried;
    /// <summary>목록용 썸네일. 화면에 보일 때(바인딩될 때) 처음 한 번만 디코딩한다.</summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (!_thumbnailTried && Category == ResourceCategory.Image)
            {
                _thumbnailTried = true;
                _thumbnail = LoadBitmap(CachePath, 96);
            }
            return _thumbnail;
        }
    }

    public static BitmapSource? LoadBitmap(string path, int decodeWidth = 0)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.EndInit();
            bmp.Freeze();

            // Windows WebP 디코더는 투명도가 있는 WebP를 원본 크기로 읽을 때 형식을 Bgr32(투명도 없음)로 알려준다.
            // 픽셀의 4번째 바이트에는 실제 알파 값이 들어 있으므로 Bgra32로 다시 지정하면 정상 표시된다.
            // (축소 디코딩 시에는 Bgra32로 나와서 썸네일은 원래 정상)
            if (bmp.Format == PixelFormats.Bgr32 && IsWebp(path))
                return ReinterpretAsBgra32(bmp);

            return bmp;
        }
        catch
        {
            return null; // WPF가 못 읽는 포맷(일부 avif 등)
        }
    }

    private static bool IsWebp(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> h = stackalloc byte[12];
            return fs.Read(h) == 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8);
        }
        catch { return false; }
    }

    private static BitmapSource ReinterpretAsBgra32(BitmapSource src)
    {
        int stride = src.PixelWidth * 4;
        var pixels = new byte[stride * src.PixelHeight];
        src.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(src.PixelWidth, src.PixelHeight, src.DpiX, src.DpiY,
                                         PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
