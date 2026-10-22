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
/// - https://backgrounds.scsm/ 배경 폴더 (변경(5회차): 게임 Resources\BG — 요청마다 지금 폴더를 물어 Resources 폴더를 바꿔도 재시작 불필요)
/// - https://resources.scsm/   추가(6회차): 게임 Resources 중 Audio\ 아래만 (Range 요청 지원)
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
    private Func<string?> _bgFolder = () => null;   // 변경(5회차): 고정 경로 → 지금 배경 폴더를 알려 주는 함수
    private Func<string?> _resFolder = () => null;  // 추가(6회차): 지금 게임 Resources 폴더 (오디오)
    private string? _sourceViewerFolder;

    public event Action<StageEvent>? EventReceived;

    /// <summary>
    /// WebView2 연결·초기화 (처음 1회). WebView2 런타임이 없으면 예외. 변경(5회차): backgroundFolder = 지금 배경 폴더(없으면 null).
    /// 추가(6회차): resourcesFolder = 지금 게임 Resources 폴더 (오디오, 없으면 null)
    /// </summary>
    public Task AttachAsync(WebView2 web, string dataFolder, Func<string?> backgroundFolder, Func<string?> resourcesFolder) =>
        _init ??= InitAsync(web, dataFolder, backgroundFolder, resourcesFolder);

    private async Task InitAsync(WebView2 web, string dataFolder, Func<string?> backgroundFolder, Func<string?> resourcesFolder)
    {
        _web = web;
        _bgFolder = backgroundFolder;
        _resFolder = resourcesFolder;
        string userData = Path.Combine(AppSettings.LocalDataFolder, "WebView2");
        // 변경(6회차): 소리를 사용자 클릭 없이 재생 (C#이 보내는 재생 명령으로 바로 소리 나도록)
        var options = new CoreWebView2EnvironmentOptions($"--disk-cache-size={DiskCacheBytes} --autoplay-policy=no-user-gesture-required");
        var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
        await web.EnsureCoreWebView2Async(env);

        var core = web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;   // 추가(4-2): F5(새로 고침) 등 브라우저 단축키 끔 — F5는 스크립트 → 무대

        Directory.CreateDirectory(dataFolder);
        // (삭제, 5회차) Directory.CreateDirectory(backgroundFolder) — 게임 폴더는 만들지 않음
        _dataRoot = Root(dataFolder);
        _sourceViewerFolder = FindSourceViewerFolder();
        if (_sourceViewerFolder != null) AppLog.Shared.Info($"[디버그] 무대 페이지를 소스 폴더에서 읽음: {_sourceViewerFolder}");

        foreach (var host in new[] { StageUrls.AppHost, StageUrls.DataHost, StageUrls.BackgroundHost, StageUrls.ResourcesHost })   // 변경(6회차)
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
        try { HandleWebMessage(e); }   // 변경(6회차): 기존 내용을 HandleWebMessage로 옮김
        catch (Exception ex) { AppLog.Shared.Error("무대 메시지 처리 중 오류", ex); }   // 추가: WebView2 콜백에서 난 예외는 전역 처리를 거치지 않고 TE를 종료시킴(E-005)
    }

    private void HandleWebMessage(CoreWebView2WebMessageReceivedEventArgs e)
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
        try { HandleWebResource(e); }   // 변경(6회차): 기존 내용을 HandleWebResource로 옮김
        catch (Exception ex) { AppLog.Shared.Error($"무대 파일 요청 처리 중 오류: {e.Request.Uri}", ex); }   // 추가(E-005 방어)
    }

    private void HandleWebResource(CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = _web!.CoreWebView2.Environment;
        var uri = new Uri(e.Request.Uri);
        string path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        const string noStore = "Cache-Control: no-store\nAccess-Control-Allow-Origin: *";

        // 추가(6회차): 게임 Resources — 오디오만 제공 (Audio\ 밖은 404). Range 요청 지원 → 큰 BGM도 앞부분부터 재생·탐색
        if (uri.Host.Equals(StageUrls.ResourcesHost, StringComparison.OrdinalIgnoreCase))
        {
            string? range = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;
            e.Response = path.StartsWith("Audio/", StringComparison.OrdinalIgnoreCase) && _resFolder() is { Length: > 0 } res && Directory.Exists(res)
                ? ServeFile(env, Root(res), path, range)
                : env.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *");
            return;
        }

        byte[]? bytes;
        if (uri.Host.Equals(StageUrls.DataHost, StringComparison.OrdinalIgnoreCase))
            bytes = ReadUnder(_dataRoot, path);
        else if (uri.Host.Equals(StageUrls.BackgroundHost, StringComparison.OrdinalIgnoreCase))
            bytes = _bgFolder() is { Length: > 0 } bg && Directory.Exists(bg) ? ReadUnder(Root(bg), path) : null;   // 변경(5회차)
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
            if (ResolveUnder(root, relativePath) is not { } full) return null;   // 변경(6회차): 경로 검사를 ResolveUnder로 분리
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

    /// <summary>추가(6회차): 루트 안의 있는 파일이면 전체 경로, 아니면(../ 등으로 루트 밖, 없음, 잘못된 경로) null</summary>
    private static string? ResolveUnder(string root, string relativePath)
    {
        try
        {
            string full = Path.GetFullPath(Path.Combine(root, relativePath));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>추가(6회차): 파일 제공 — Range가 있으면 그 부분만 206, 없으면 전체 200. 루트 밖·없는 파일은 404</summary>
    private static CoreWebView2WebResourceResponse ServeFile(CoreWebView2Environment env, string root, string relativePath, string? range)
    {
        const string common = "Cache-Control: no-store\nAccess-Control-Allow-Origin: *\nAccept-Ranges: bytes";
        if (ResolveUnder(root, relativePath) is not { } full)
            return env.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *");
        try
        {
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = fs.Length;
            string type = ContentType(relativePath);
            if (TryParseRange(range, length, out long start, out long end))
            {
                var part = new byte[end - start + 1];
                fs.Position = start;
                fs.ReadExactly(part);
                return env.CreateWebResourceResponse(new MemoryStream(part), 206, "Partial Content",
                    $"Content-Type: {type}\nContent-Length: {part.Length}\nContent-Range: bytes {start}-{end}/{length}\n{common}");
            }
            var all = new byte[length];
            fs.ReadExactly(all);
            return env.CreateWebResourceResponse(new MemoryStream(all), 200, "OK", $"Content-Type: {type}\nContent-Length: {length}\n{common}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Shared.Warn($"무대 파일 읽기 실패: {relativePath} — {ex.Message}");
            return env.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *");
        }
    }

    /// <summary>추가(6회차): Range 헤더 "bytes=a-b" / "bytes=a-" / "bytes=-n" (여러 구간이면 첫 구간만). 해석할 수 없으면 false(전체)</summary>
    private static bool TryParseRange(string? range, long length, out long start, out long end)
    {
        start = 0;
        end = length - 1;
        if (range is null || length == 0 || !range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        string[] p = range[6..].Split(',')[0].Trim().Split('-');
        if (p.Length != 2) return false;
        if (p[0].Length == 0)
        {
            if (!long.TryParse(p[1], out long n) || n <= 0) return false;
            start = Math.Max(0, length - n);
        }
        else
        {
            if (!long.TryParse(p[0], out start) || start >= length) return false;
            if (p[1].Length > 0 && long.TryParse(p[1], out long last)) end = Math.Min(last, length - 1);
        }
        return start <= end;
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
        ".mp3" => "audio/mpeg",                  // 추가(6회차): 오디오
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        _ => "application/octet-stream",
    };
}
