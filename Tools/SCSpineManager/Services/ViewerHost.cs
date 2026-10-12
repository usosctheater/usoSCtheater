using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SCSpineManager.Services;

/// <summary>
/// 뷰어 탭의 WebView2 연결.
/// - https://viewer.scsm/   : exe에 포함된 뷰어 페이지(Viewer\viewer.html·viewer.js·lib) — 요청을 가로채 리소스에서 제공
/// - https://spinedata.scsm/: Spine 저장 루트(SpineData) 폴더를 그대로 연결 — 받은 json·atlas·png를 읽음
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

    /// <summary>JS에서 온 메시지 ("ready"는 내부에서 처리하고 넘기지 않음)</summary>
    public event Action<JsonElement>? MessageReceived;

    public ViewerHost(WebView2 web) => _web = web;

    public async Task InitializeAsync(string dataFolder)
    {
        // WebView2 사용자 데이터: 실행 파일 옆이 아닌 %LocalAppData%\SCSpineManager\WebView2
        string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SCSpineManager", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userData);
        await _web.EnsureCoreWebView2Async(env);

        var core = _web.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;    // Ctrl+휠은 페이지 확대가 아닌 뷰어 확대로 쓰기 위해

        Directory.CreateDirectory(dataFolder);
        core.SetVirtualHostNameToFolderMapping(DataHost, dataFolder, CoreWebView2HostResourceAccessKind.Allow);

        core.AddWebResourceRequestedFilter($"https://{AppHost}/*", CoreWebView2WebResourceContext.All);
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
        catch { return; }

        if (msg.TryGetProperty("type", out var t) && t.GetString() == "ready")
        {
            _ready = true;
            while (_pending.Count > 0) _web.CoreWebView2.PostWebMessageAsJson(_pending.Dequeue());
            return;
        }
        MessageReceived?.Invoke(msg);
    }

    /// <summary>https://viewer.scsm/{경로} → 포함 리소스 "viewer/{경로}"</summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var core = _web.CoreWebView2;
        string path = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath.TrimStart('/'));
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("viewer/" + path);
        e.Response = stream == null
            ? core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "")
            : core.Environment.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {ContentType(path)}");
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };
}
