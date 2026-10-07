using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SCResourceGrabber.Models;
using SCResourceGrabber.Services;

namespace SCResourceGrabber;

/// <summary>
/// 리소스 상세 창. 메인 창과 독립된 일반 창이라 크기를 자유롭게 바꿀 수 있고,
/// 이미지·비디오는 창 크기에 맞춰 함께 늘어나고 줄어든다 (1:1 토글 시 원본 픽셀 크기 + 스크롤).
/// </summary>
public partial class ResourceDetailWindow : Window
{
    private readonly Action<CapturedResource> _save;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private BitmapSource? _bitmap;
    private bool _isPlaying;

    public CapturedResource Resource { get; }

    public ResourceDetailWindow(CapturedResource resource, Action<CapturedResource> save, HotkeyMap hotkeys)
    {
        InitializeComponent();
        Resource = resource;
        _save = save;
        Title = $"{resource.FileName} — 리소스 상세";
        InfoText.Text = $"{resource.FileName}  ·  {resource.CategoryLabel}  ·  {resource.SizeText}  ·  {resource.Mime}";
        InfoText.ToolTip = resource.Url;

        _timer.Tick += (_, _) => UpdateTime();
        Closed += (_, _) => { _timer.Stop(); Media.Close(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // 저장 단축키(기본 Ctrl+S) → 이 리소스 저장
        PreviewKeyDown += (_, e) =>
        {
            if (hotkeys.Resolve(e) == HotkeyMap.SaveChecked) { _save(Resource); e.Handled = true; }
        };

        LoadContent();
    }

    private void LoadContent()
    {
        switch (Resource.Category)
        {
            case ResourceCategory.Image:
                _bitmap = CapturedResource.LoadBitmap(Resource.CachePath);
                if (_bitmap == null) { ShowText("(이 이미지 포맷은 미리보기를 지원하지 않습니다)"); break; }
                DetailImage.Source = _bitmap;
                ActualSizeToggle.Visibility = Visibility.Visible;
                InfoText.Text += $"  ·  {_bitmap.PixelWidth}×{_bitmap.PixelHeight}";
                FitWindowTo(_bitmap.PixelWidth, _bitmap.PixelHeight);
                break;

            case ResourceCategory.Audio:
            case ResourceCategory.Video:
                ImageScroller.Visibility = Visibility.Collapsed;
                MediaBar.Visibility = Visibility.Visible;
                if (Resource.Category == ResourceCategory.Video) Media.Visibility = Visibility.Visible;
                else
                {
                    AudioLabel.Text = "♪\n" + Resource.FileName;
                    AudioLabel.Visibility = Visibility.Visible;
                    Width = 520; Height = 260;
                }
                Media.Source = new Uri(Resource.CachePath);
                Play();
                break;

            case ResourceCategory.Encrypted:
                ShowText($"(암호화된 데이터 — 게임이 내부에서 풀어서 사용하는 형식이라 내용을 표시할 수 없습니다)\n\n크기: {Resource.SizeText}");
                break;

            case ResourceCategory.Json:
            case ResourceCategory.Spine when Resource.Extension is "json" or "atlas":
            case ResourceCategory.Other:
                ShowText(ReadText(Resource.CachePath));
                break;

            default:
                ShowText("(미리보기 없음)");
                break;
        }
    }

    /// <summary>처음 열 때 창 크기를 이미지에 맞춤 (작업 영역의 85%를 넘지 않게).</summary>
    private void FitWindowTo(int pixelWidth, int pixelHeight)
    {
        var area = SystemParameters.WorkArea;
        const double chromeW = 40, chromeH = 110; // 테두리·상단 바 여유분
        double scale = Math.Min(1.0, Math.Min(area.Width * 0.85 / (pixelWidth + chromeW), area.Height * 0.85 / (pixelHeight + chromeH)));
        Width = Math.Max(MinWidth, pixelWidth * scale + chromeW);
        Height = Math.Max(MinHeight, pixelHeight * scale + chromeH);
    }

    private void ActualSize_Changed(object sender, RoutedEventArgs e)
    {
        bool actual = ActualSizeToggle.IsChecked == true;
        if (actual && _bitmap != null)
        {
            // 파일의 DPI 정보와 무관하게 픽셀 1:1 (화면 배율 100% 기준)
            DetailImage.Stretch = System.Windows.Media.Stretch.Fill;
            DetailImage.Width = _bitmap.PixelWidth;
            DetailImage.Height = _bitmap.PixelHeight;
        }
        else
        {
            DetailImage.Stretch = System.Windows.Media.Stretch.Uniform;
            DetailImage.Width = double.NaN;
            DetailImage.Height = double.NaN;
        }
        var bar = actual ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ImageScroller.HorizontalScrollBarVisibility = bar;
        ImageScroller.VerticalScrollBarVisibility = bar;
    }

    // ---------------- 텍스트 ----------------

    private void ShowText(string text)
    {
        ImageScroller.Visibility = Visibility.Collapsed;
        DetailText.Visibility = Visibility.Visible;
        DetailText.Text = text;
    }

    private static string ReadText(string path)
    {
        const int maxBytes = 2 * 1024 * 1024;
        try
        {
            var info = new FileInfo(path);
            string text;
            using (var fs = File.OpenRead(path))
            {
                var buf = new byte[Math.Min(maxBytes, info.Length)];
                int n = fs.Read(buf, 0, buf.Length);
                text = Encoding.UTF8.GetString(buf, 0, n);
            }
            if (info.Length > maxBytes) return text + "\n\n… (2MB 이후 생략)";

            // JSON은 보기 좋게 들여쓰기 (실패하면 원문)
            string t = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
            if (t.StartsWith('{') || t.StartsWith('['))
            {
                try
                {
                    using var jd = JsonDocument.Parse(t);
                    return JsonSerializer.Serialize(jd.RootElement, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                }
                catch { }
            }
            return text;
        }
        catch (Exception ex) { return "읽기 실패: " + ex.Message; }
    }

    // ---------------- 오디오 / 비디오 ----------------

    private void Play() { Media.Play(); _isPlaying = true; _timer.Start(); }
    private void Pause() { Media.Pause(); _isPlaying = false; }

    private void PlayPause_Click(object sender, RoutedEventArgs e) { if (_isPlaying) Pause(); else Play(); }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        Media.Stop();
        _isPlaying = false;
        UpdateTime();
    }

    private void Media_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (Media.NaturalDuration.HasTimeSpan)
            SeekSlider.Maximum = Media.NaturalDuration.TimeSpan.TotalSeconds;
        if (Resource.Category == ResourceCategory.Video && Media.NaturalVideoWidth > 0)
        {
            InfoText.Text += $"  ·  {Media.NaturalVideoWidth}×{Media.NaturalVideoHeight}";
            FitWindowTo(Media.NaturalVideoWidth, Media.NaturalVideoHeight + 40);
        }
        UpdateTime();
    }

    private void Media_MediaEnded(object sender, RoutedEventArgs e)
    {
        Media.Stop();
        _isPlaying = false;
        UpdateTime();
    }

    private void SeekSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e) =>
        Media.Position = TimeSpan.FromSeconds(SeekSlider.Value);

    private void UpdateTime()
    {
        var pos = Media.Position;
        var total = Media.NaturalDuration.HasTimeSpan ? Media.NaturalDuration.TimeSpan : TimeSpan.Zero;
        if (Mouse.LeftButton != MouseButtonState.Pressed) SeekSlider.Value = pos.TotalSeconds;
        TimeText.Text = $@"{pos:m\:ss} / {total:m\:ss}";
        if (!_isPlaying) _timer.Stop();
    }

    // ---------------- 동작 ----------------

    private void Save_Click(object sender, RoutedEventArgs e) => _save(Resource);
    private void CopyUrl_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(Resource.Url);
}
