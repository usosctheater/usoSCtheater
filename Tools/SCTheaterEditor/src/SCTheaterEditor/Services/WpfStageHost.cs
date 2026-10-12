using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SCTheaterEditor.Stage;

namespace SCTheaterEditor.Services;

/// <summary>
/// 스크립트 탭 무대(WebView2)와의 연결 — IStageHost 구현 (이전 ViewerHost를 바꾼 것).
/// 가상 호스트 (모두 요청을 가로채 직접 제공):
/// - https://viewer.scsm/      무대 페이지(stage.html·js·lib). 배포 exe는 내장 리소스, 디버그 빌드는 소스 폴더(Viewer\)에서 바로 읽음
///                              → JS를 고친 뒤 다시 빌드하지 않고 개발자 도구에서 새로 고침만 하면 됨
/// - https://spinedata.scsm/   Spine 저장 루트 (캐시 금지, 저장 루트 밖 경로 차단)
/// - https://backgrounds.scsm/ 배경 폴더 (exe 옆 Backgrounds\)
/// 키: Ctrl·Alt 조합은 WPF WebView2 컨트롤이 WPF 키 이벤트로 넘겨 줌(창의 PreviewKeyDown), 그 밖의 단축키는 페이지가 key 메시지로 넘김
/// </summary>
public sealed class WpfStageHost : IStageHost
{
    /// <summary>WebView2 디스크 캐시 상한 (기본값은 제한 없이 커짐)</summary>
    private const long DiskCacheBytes = 64L * 1024 * 1024;

    private WebView2? _web;
    private Task? _init;
    private bool _ready;
    private string _dataRoot = "";
    private string _bgRoot = "";
    private string? _sourceViewerFolder;

    public event Action<StageEvent>? EventReceived;

    /// <summary>WebView2 연결·초기화 (처음 1회). WebView2 런타임이 없으면 예외</summary>
    public Task AttachAsync(WebView2 web, string dataFolder, string backgroundFolder) =>
        _init ??= InitAsync(web, dataFolder, backgroundFolder);

    private async Task InitAsync(WebView2 web, string dataFolder, string backgroundFolder)
    {
        _web = web;
        string userData = Path.Combine(AppSettings.LocalDataFolder, "WebView2");
        var options = new CoreWebView2EnvironmentOptions($"--disk-cache-size={DiskCacheBytes}");
        var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
        await web.EnsureCoreWebView2Async(env);

        var core = web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;

        Directory.CreateDirectory(dataFolder);
        Directory.CreateDirectory(backgroundFolder);
        _dataRoot = Root(dataFolder);
        _bgRoot = Root(backgroundFolder);
        _sourceViewerFolder = FindSourceViewerFolder();
        if (_sourceViewerFolder != null) AppLog.Shared.Info($"[디버그] 무대 페이지를 소스 폴더에서 읽음: {_sourceViewerFolder}");

        foreach (var host in new[] { StageUrls.AppHost, StageUrls.DataHost, StageUrls.BackgroundHost })
            core.AddWebResourceRequestedFilter($"https://{host}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.WebMessageReceived += OnWebMessageReceived;

        core.Navigate($"https://{StageUrls.AppHost}/stage.html");
    }

    public void Send(StageCommand command)
    {
        if (_ready && _web?.CoreWebView2 != null) _web.CoreWebView2.PostWebMessageAsJson(command.ToJson());
    }

    /// <summary>개발자 도구 (무대 페이지 오류 확인, 디버그 빌드에서는 F5로 JS 새로 고침)</summary>
    public void OpenDevTools() => _web?.CoreWebView2?.OpenDevToolsWindow();

    // ------------------------------------------------------------------ 메시지

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        StageEvent? ev;
        try { ev = StageEvents.Parse(JsonDocument.Parse(e.WebMessageAsJson).RootElement); }
        catch (JsonException ex)
        {
            AppLog.Shared.Warn($"무대 메시지 해석 실패: {ex.Message}");
            return;
        }
        if (ev is StageReady) _ready = true;   // 새로 고침해도 다시 ready
        if (ev != null) EventReceived?.Invoke(ev);
    }

    // ------------------------------------------------------------------ 파일 제공

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = _web!.CoreWebView2.Environment;
        var uri = new Uri(e.Request.Uri);
        string path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        const string noStore = "Cache-Control: no-store\nAccess-Control-Allow-Origin: *";

        byte[]? bytes;
        if (uri.Host.Equals(StageUrls.DataHost, StringComparison.OrdinalIgnoreCase))
            bytes = ReadUnder(_dataRoot, path);
        else if (uri.Host.Equals(StageUrls.BackgroundHost, StringComparison.OrdinalIgnoreCase))
            bytes = ReadUnder(_bgRoot, path);
        else
            bytes = ReadPage(path);

        e.Response = bytes == null
            ? env.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *")
            : env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", $"Content-Type: {ContentType(path)}\n{noStore}");
    }

    /// <summary>무대 페이지 파일: 디버그 빌드는 소스 폴더, 아니면 내장 리소스 "viewer/{경로}"</summary>
    private byte[]? ReadPage(string path)
    {
        if (_sourceViewerFolder != null) return ReadUnder(_sourceViewerFolder, path);
        using var stream = typeof(WpfStageHost).Assembly.GetManifestResourceStream("viewer/" + path);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>루트 안의 파일만 읽음 (../ 등으로 루트 밖을 가리키면 null). 쓰는 중인 파일도 읽을 수 있게 공유 모드로 엶</summary>
    private static byte[]? ReadUnder(string root, string relativePath)
    {
        try
        {
            string full = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[fs.Length];
            fs.ReadExactly(buffer);
            return buffer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppLog.Shared.Warn($"무대 파일 읽기 실패: {relativePath} — {ex.Message}");
            return null;
        }
    }

    private static string Root(string folder) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;

    /// <summary>디버그 빌드: 이 소스 파일 기준 ..\Viewer 폴더가 있으면 그곳 (배포 exe에서는 null)</summary>
    private static string? FindSourceViewerFolder([CallerFilePath] string thisFile = "")
    {
#if DEBUG
        string folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile) ?? "", "..", "Viewer"));
        return File.Exists(Path.Combine(folder, "stage.html")) ? Root(folder) : null;
#else
        return null;
#endif
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".txt" => "text/plain; charset=utf-8",   // .atlas.txt
        _ => "application/octet-stream",
    };
}
