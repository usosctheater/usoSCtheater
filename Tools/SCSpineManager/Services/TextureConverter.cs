using System.IO;
using SkiaSharp;

namespace SCSpineManager.Services;

/// <summary>
/// 텍스처를 PNG로 맞춘다. 서버의 data.png 중 실제로는 WebP인 파일이 있음 (Unity는 WebP 임포트 불가).
/// - 이미 PNG면 그대로 (재인코딩 없음)
/// - 그 외 형식은 SkiaSharp로 디코딩 → PNG 인코딩. 알파는 곱하지 않은(Unpremul) 상태로 유지해 색 손실을 막음
/// </summary>
public static class TextureConverter
{
    public static string DetectFormat(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
                           && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpeg";
        return "unknown";
    }

    /// <summary>PNG 바이트 반환. 디코딩 실패 시 예외</summary>
    public static byte[] ToPng(byte[] data, out string sourceFormat)
    {
        sourceFormat = DetectFormat(data);
        if (sourceFormat == "png") return data;

        using var codec = SKCodec.Create(new SKMemoryStream(data))
                          ?? throw new InvalidDataException($"이미지를 읽을 수 없음 (형식: {sourceFormat})");
        var info = codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw new InvalidDataException($"이미지 디코딩 실패: {result}");

        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100)
                            ?? throw new InvalidDataException("PNG 인코딩 실패");
        return encoded.ToArray();
    }
}
