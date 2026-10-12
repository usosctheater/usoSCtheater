using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SCTheaterEditor.Services;

/// <summary>
/// 뷰어 탭의 WebView2 연결.
/// - https://viewer.scsm/   : exe에 포함된 뷰어 페이지(Viewer\viewer.html·viewer.js·lib) — 요청을 가로채 리소스에서 제공
/// - https://spinedata.scsm/: Spine 저장 루트(SpineData)의 파일 — 변경: 폴더 연결 대신 요청을 가로채 직접 제공하고
///   캐시 금지(no-store) 헤더를 붙임 → 같은 이름으로 다시 받은 파일이 옛 캐시로 보이지 않음
/// - 메시지: C# → JS는 Post(객체 → JSON), JS → C#은 MessageReceived 이벤트
///   페이지가 "ready"를 보내기 전에 Post한 메시지는 모아 두었다가 ready 때 보냄
/// </summary>
public sealed class ViewerHost
{
    public const string AppHost = "viewer.scsm";
    public const string DataHost = "spinedata.scsm";

    private readonly WebView2 _web;
    private readonly Queue<string> _pending = new();
    private bool _ready;
    private string _dataRoot = "";   // 추가: Spine 저장 루트 (끝에 \ 포함, 경로 이탈 검사용)

    /// <summary>추가: WebView2 디스크 캐시 상한 (기본값은 제한 없이 커짐)</summary>
    private const long DiskCacheBytes = 64L * 1024 * 1024;

    /// <summary>JS에서 온 메시지 ("ready"는 내부에서 처리하고 넘기지 않음)</summary>
    public event Action<JsonElement>? MessageReceived;

    public ViewerHost(WebView2 web) => _web = web;

    public async Task InitializeAsync(string dataFolder)
    {
        // 변경: WebView2 사용자 데이터 %LocalAppData%\SCTheaterEditor\WebView2 (실행 파일 옆이 아님), 디스크 캐시 상한 지정
        string userData = Path.Combine(AppSettings.LocalDataFolder, "WebView2");
        var options = new CoreWebView2EnvironmentOptions($"--disk-cache-size={DiskCacheBytes}");
        var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;    // Ctrl+휠은 페이지 확대가 아닌 뷰어 확대로 쓰기 위해

        // 변경: 폴더 연결(SetVirtualHostNameToFolderMapping) 대신 두 호스트 모두 요청을 가로채 제공
        Directory.CreateDirectory(dataFolder);
        _dataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataFolder)) + Path.DirectorySeparatorChar;
        core.AddWebResourceRequestedFilter($"https://{AppHost}/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter($"https://{DataHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.WebMessageReceived += OnWebMessageReceived;

        core.Navigate($"https://{AppHost}/viewer.html");
    }

    /// <summary>JS로 메시지 보내기 (익명 객체 가능)</summary>
    public void Post(object message)
    {
        string json = JsonSerializer.Serialize(message);
        if (_ready) _web.CoreWebView2.PostWebMessageAsJson(json);
        else _pending.Enqueue(json);
    }

    /// <summary>저장 루트 기준 상대 폴더 → 뷰어에서 쓸 폴더 URL (끝에 / 포함, 경로 조각마다 인코딩)</summary>
    public static string FolderUrl(string relativeFolder) =>
        $"https://{DataHost}/" + string.Concat(relativeFolder
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(seg => Uri.EscapeDataString(seg) + "/"));

    /// <summary>개발자 도구 열기 (뷰어 문제 확인용)</summary>
    public void OpenDevTools() => _web.CoreWebView2?.OpenDevToolsWindow();

    // ------------------------------------------------------------------

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement msg;
        try { msg = JsonDocument.Parse(e.WebMessageAsJson).RootElement.Clone(); }
        catch (JsonException ex)
        {
            // 변경: 조용히 버리지 않고 로그 (페이지 쪽 메시지 형식 오류)
            AppLog.Shared.Warn($"뷰어 메시지 해석 실패: {ex.Message}");
            return;
        }

        if (msg.TryGetProperty("type", out var t) && t.GetString() == "ready")
        {
            _ready = true;
            while (_pending.Count > 0) _web.CoreWebView2.PostWebMessageAsJson(_pending.Dequeue());
            return;
        }
        MessageReceived?.Invoke(msg);
    }

    /// <summary>
    /// https://viewer.scsm/{경로} → 포함 리소스 "viewer/{경로}"
    /// 추가: https://spinedata.scsm/{경로} → 저장 루트의 파일 (캐시 금지, 다른 출처의 페이지가 읽을 수 있도록 CORS 허용)
    /// </summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = _web.CoreWebView2.Environment;
        var uri = new Uri(e.Request.Uri);
        string path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        if (uri.Host.Equals(DataHost, StringComparison.OrdinalIgnoreCase))
        {
            byte[]? bytes = ReadDataFile(path);
            e.Response = bytes == null
                ? env.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *")
                : env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK",
                    $"Content-Type: {ContentType(path)}\nCache-Control: no-store\nAccess-Control-Allow-Origin: *");
            return;
        }

        var stream = typeof(ViewerHost).Assembly.GetManifestResourceStream("viewer/" + path);
        e.Response = stream == null
            ? env.CreateWebResourceResponse(null, 404, "Not Found", "")
            : env.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {ContentType(path)}");
    }

    /// <summary>추가: 저장 루트 안의 파일만 읽음 (../ 등으로 루트 밖을 가리키면 null). 쓰는 중인 파일도 읽을 수 있게 공유 모드로 엶</summary>
    private byte[]? ReadDataFile(string relativePath)
    {
        try
        {
            string full = Path.GetFullPath(Path.Combine(_dataRoot, relativePath));
            if (!full.StartsWith(_dataRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
            using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[fs.Length];
            fs.ReadExactly(buffer);
            return buffer;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            AppLog.Shared.Warn($"뷰어 파일 읽기 실패: {relativePath} — {ex.Message}");
            return null;
        }
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".png" => "image/png",
        ".txt" => "text/plain; charset=utf-8",   // 추가: .atlas.txt
        _ => "application/octet-stream",
    };
}
