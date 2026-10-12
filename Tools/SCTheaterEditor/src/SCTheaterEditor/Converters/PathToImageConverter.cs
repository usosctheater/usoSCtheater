using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.Converters;

/// <summary>
/// 파일 경로 → 이미지. 파일을 잠그지 않도록 메모리로 읽고(OnLoad), 실패하면 null (로그만 남김).
/// 텍스처 미리보기용
/// </summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;   // 같은 경로로 다시 받은 파일도 새로 읽음
            img.UriSource = new Uri(path);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            AppLog.Shared.Warn($"미리보기 실패: {Path.GetFileName(path)} — {ex.Message}");
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
