using System.Collections.Concurrent;
using System.IO;
using Microsoft.Web.WebView2.Core;
using SCResourceGrabber.Models;

namespace SCResourceGrabber.Services;

/// <summary>
/// WebView2의 WebResourceResponseReceived 이벤트로 응답 본문을 받아 세션 캐시에 기록한다.
/// DevTools를 열 필요 없이, 내장 브라우저가 받은 데이터를 그대로 가져온다.
/// </summary>
public sealed class ResourceCapture
{
    private readonly CoreWebView2 _core;
    private readonly AppSettings _settings;
    private readonly ConcurrentDictionary<string, byte> _seen = new();
    private int _nextId;

    public string CacheFolder { get; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>UI 스레드에서 호출된다.</summary>
    public event Action<CapturedResource>? Captured;
    /// <summary>실패/건너뜀 등 진단용 로그.</summary>
    public event Action<string>? Log;

    public ResourceCapture(CoreWebView2 core, AppSettings settings, string cacheFolder)
    {
        _core = core;
        _settings = settings;
        CacheFolder = cacheFolder;
        Directory.CreateDirectory(cacheFolder);
        _core.WebResourceResponseReceived += OnResponseReceived;
    }

    /// <summary>목록을 비울 때 호출. 같은 URL을 다시 캡처할 수 있게 된다.</summary>
    public void ResetSeen() => _seen.Clear();

    private async void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (!IsEnabled) return;

        string url = e.Request.Uri;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(e.Request.Method, "GET", StringComparison.OrdinalIgnoreCase)) return;

        Uri uri;
        try { uri = new Uri(url); } catch { return; }
        if (!_settings.MatchesHost(uri.Host)) return;

        int status = e.Response.StatusCode;
        if (status == 206)
        {
            Log?.Invoke($"부분 응답(206)이라 건너뜀: {url}");
            return;
        }
        if (status < 200 || status >= 300) return;

        // 같은 URL은 한 번만 (await 전에 등록해서 동시 중복 방지)
        if (!_seen.TryAdd(url, 0)) return;

        try
        {
            string mime = e.Response.Headers.Contains("Content-Type")
                ? e.Response.Headers.GetHeader("Content-Type")
                : "";

            using Stream? stream = await e.Response.GetContentAsync();
            if (stream == null)
            {
                _seen.TryRemove(url, out _);
                return;
            }

            byte[] data;
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                data = ms.ToArray();
            }
            if (data.Length == 0) return;

            var result = ResourceClassifier.Classify(url, mime, data);
            int id = Interlocked.Increment(ref _nextId);
            string cachePath = Path.Combine(CacheFolder, $"{id:D6}.{result.Extension}");
            await File.WriteAllBytesAsync(cachePath, data);

            Captured?.Invoke(new CapturedResource
            {
                Id = id,
                Url = url,
                Mime = mime,
                Size = data.Length,
                Category = result.Category,
                Extension = result.Extension,
                SubType = result.SubType,
                CachePath = cachePath,
            });
        }
        catch (Exception ex)
        {
            _seen.TryRemove(url, out _);
            Log?.Invoke($"캡처 실패: {url} ({ex.Message})");
        }
    }
}
