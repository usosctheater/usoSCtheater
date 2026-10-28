using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SCResourceGrabber.Models;

namespace SCResourceGrabber.Services;

/// <summary>다운로드 창 이미지 처리 옵션 (마지막 설정을 settings.json에 기억)</summary>
public readonly record struct ImageOptions(bool ConvertToPng, bool Trim, bool Square, double PaddingPercent)
{
    /// <summary>픽셀을 다시 만들어야 하는지 (아니면 원본 파일 그대로 복사 가능)</summary>
    public bool Reshapes => Trim || Square || PaddingPercent > 0;
}

/// <summary>
/// 다운로드 창 이미지 처리: 읽기(webp 포함, WPF 코덱) → 투명 여백 잘라내기 → 정사각형 가운데 정렬 + 여백 → PNG 저장.
/// 2026-10-27 IrisIcon 사인 정리 방식과 같음 (알파 > 8을 내용으로 봄, 여백 = 긴 변 × 비율).
/// </summary>
public static class ImageProcessor
{
    private const byte AlphaThreshold = 8;

    /// <summary>옵션을 적용한 결과 (미리보기·저장 공용). 읽지 못하면 null</summary>
    public static BitmapSource? Process(string path, ImageOptions o)
    {
        BitmapSource? src = CapturedResource.LoadBitmap(path);
        if (src == null) return null;
        if (!o.Reshapes) return src;

        BitmapSource bgra = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);

        // 1. 내용 영역 (잘라내기를 끄면 전체)
        Int32Rect rect = o.Trim ? FindContent(px, w, h) : new Int32Rect(0, 0, w, h);

        // 2. 여백(긴 변 기준) + 정사각형
        int longSide = Math.Max(rect.Width, rect.Height);
        int pad = (int)Math.Round(longSide * Math.Max(0, o.PaddingPercent) / 100.0);
        int outW = o.Square ? longSide + pad * 2 : rect.Width + pad * 2;
        int outH = o.Square ? longSide + pad * 2 : rect.Height + pad * 2;
        int offX = (outW - rect.Width) / 2, offY = (outH - rect.Height) / 2;

        var dst = new byte[outW * outH * 4];   // 나머지는 투명(0)
        for (int y = 0; y < rect.Height; y++)
            Buffer.BlockCopy(px, (rect.Y + y) * stride + rect.X * 4, dst, ((offY + y) * outW + offX) * 4, rect.Width * 4);

        var result = BitmapSource.Create(outW, outH, 96, 96, PixelFormats.Bgra32, null, dst, outW * 4);
        result.Freeze();
        return result;
    }

    /// <summary>알파가 기준을 넘는 픽셀의 경계. 하나도 없으면 전체</summary>
    private static Int32Rect FindContent(byte[] px, int w, int h)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                if (px[row + x * 4 + 3] <= AlphaThreshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? new Int32Rect(0, 0, w, h) : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    public static void SavePng(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
