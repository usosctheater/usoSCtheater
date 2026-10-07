using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace SCResourceGrabber.Services;

/// <summary>
/// 게임 페이지에 audio_hook.js를 주입하고, 페이지(및 iframe)에서 오는 재생/종료 메시지를 이벤트로 바꾼다.
/// 주입은 프로그램 시작 시 1회 — 추적 모드를 켜기 전에 받은 오디오도 재생 시점에 URL을 알 수 있다.
/// </summary>
public sealed class PageAudioMonitor
{
    private readonly CoreWebView2 _core;

    /// <summary>(재생 id, url, loop) — UI 스레드</summary>
    public event Action<string, string, bool>? Played;
    /// <summary>재생 id — UI 스레드</summary>
    public event Action<string>? Ended;
    /// <summary>페이지가 새로 열림(새로고침 등) — 재생 중 상태를 모두 해제해야 함</summary>
    public event Action? PageReset;
    /// <summary>진단 로그</summary>
    public event Action<string>? Log;

    public PageAudioMonitor(CoreWebView2 core) => _core = core;

    public async Task InitializeAsync()
    {
        string script = LoadScript();
        await _core.AddScriptToExecuteOnDocumentCreatedAsync(script);

        _core.WebMessageReceived += (_, e) => Handle(e.TryGetWebMessageAsString());
        // iframe 안에서 게임이 돌아도 메시지를 받도록 프레임마다 구독
        _core.FrameCreated += (_, e) =>
            e.Frame.WebMessageReceived += (_, me) => Handle(me.TryGetWebMessageAsString());
        _core.NavigationStarting += (_, _) => PageReset?.Invoke();
    }

    private static string LoadScript()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("audio_hook.js")
                      ?? throw new InvalidOperationException("audio_hook.js 리소스를 찾을 수 없습니다.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    private void Handle(string? json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            switch (type)
            {
                case "play":
                    string id = root.GetProperty("id").GetString() ?? "";
                    string url = root.GetProperty("url").GetString() ?? "";
                    bool loop = root.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.True;
                    string api = root.TryGetProperty("api", out var a) ? a.GetString() ?? "" : "";
                    Log?.Invoke($"play [{api}] {url}");
                    Played?.Invoke(id, url, loop);
                    break;
                case "end":
                    Ended?.Invoke(root.GetProperty("id").GetString() ?? "");
                    break;
                case "log":
                    Log?.Invoke(root.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "");
                    break;
            }
        }
        catch
        {
            // 다른 스크립트(게임 등)가 보낸 메시지일 수 있음 — 무시
        }
    }
}
