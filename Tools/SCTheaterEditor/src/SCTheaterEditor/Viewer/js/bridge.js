// C#(WebView2)과 메시지 주고받기. 메시지 형식의 기준은 Core/Stage/StageMessages.cs
const host = window.chrome && window.chrome.webview;

/** JS → C# */
export function post(msg) {
  if (host) host.postMessage(msg);
}

/** C# → JS */
export function onMessage(handler) {
  if (host) host.addEventListener("message", (e) => { if (e.data) handler(e.data); });
}
