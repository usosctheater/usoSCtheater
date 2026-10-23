// 추가(6회차): 오디오 재생 — 채널마다 <audio> 하나 (같은 채널에 다시 재생하면 바꿈)
// 메시지 형식의 기준은 C# Core/Stage/StageMessages.cs
//   C# → JS: PlayAudio · StopAudio · SeekAudio · PauseAudio(8회차)
//   JS → C#: AudioStarted · AudioSeeked · AudioEnded · AudioError · AudioTime(8회차, 10회/초 — 재생 바)
import { post } from "./bridge.js";

const channels = new Map();   // name → { el, stopAt, ended }

function channel(name) {
  let c = channels.get(name);
  if (c) return c;
  const a = new Audio();
  a.preload = "auto";
  c = { el: a, stopAt: 0, ended: false };
  a.addEventListener("ended", () => { c.ended = true; post({ type: "audioEnded", channel: name }); });
  a.addEventListener("seeked", () => post({ type: "audioSeeked", channel: name, time: a.currentTime }));
  a.addEventListener("error", () => {
    if (!a.getAttribute("src")) return;   // 정지(src 비움)로 생긴 오류는 무시
    post({ type: "audioError", channel: name, url: a.src, message: a.error ? `code ${a.error.code} ${a.error.message || ""}` : "unknown" });
  });
  channels.set(name, c);
  return c;
}

export function play(msg) {
  const c = channel(msg.channel), a = c.el;
  a.pause();
  a.src = msg.url;
  a.loop = !!msg.loop;
  a.volume = Math.min(1, Math.max(0, msg.volume ?? 1));
  a.currentTime = msg.start || 0;
  c.stopAt = msg.stopAt > 0 ? msg.stopAt : 0;   // 추가(8회차): SE 줄의 재생 시간
  c.ended = false;
  a.play()
    .then(() => post({ type: "audioStarted", channel: msg.channel, url: msg.url, duration: Number.isFinite(a.duration) ? a.duration : -1 }))
    .catch((err) => post({ type: "audioError", channel: msg.channel, url: msg.url, message: err.message }));
}

/** 정지 (name이 null이면 전부) */
export function stop(name) {
  for (const [key, c] of channels) {
    if (name != null && key !== name) continue;
    c.el.pause();
    c.el.removeAttribute("src");
    c.el.load();
    c.ended = false;
  }
}

/** 시점 이동 (끝보다 뒤면 끝에서 멈춘 상태) */
export function seek(name, seconds) {
  const c = channels.get(name);
  if (!c || !c.el.getAttribute("src")) return;
  const end = c.stopAt > 0 ? c.stopAt : c.el.duration;
  c.el.currentTime = Number.isFinite(end) ? Math.min(seconds, end) : seconds;
  c.ended = Number.isFinite(end) && seconds >= end;
}

/** 추가(8회차): 일시정지 / 이어 재생 (끝난 채널은 이어 재생하지 않음) */
export function pause(name, paused) {
  const c = channels.get(name);
  if (!c || !c.el.getAttribute("src")) return;
  if (paused) c.el.pause();
  else if (!c.ended) c.el.play().catch((err) => post({ type: "audioError", channel: name, url: c.el.src, message: err.message }));
}

// 추가(8회차): 재생 시간 알림 (10회/초) + 재생 시간(stopAt)에서 끊기
setInterval(() => {
  for (const [name, c] of channels) {
    const a = c.el;
    if (!a.getAttribute("src")) continue;
    if (c.stopAt > 0 && !a.paused && a.currentTime >= c.stopAt) {
      a.pause();
      c.ended = true;
      post({ type: "audioEnded", channel: name });
    }
    const duration = c.stopAt > 0 ? c.stopAt : (Number.isFinite(a.duration) ? a.duration : 0);
    const time = Math.min(a.currentTime, duration || a.currentTime);
    const key = `${time.toFixed(2)}|${duration}|${a.paused}`;
    if (key === c.last) continue;   // 멈춰 있어 바뀐 게 없으면 보내지 않음
    c.last = key;
    post({ type: "audioTime", channel: name, time, duration, paused: a.paused });
  }
}, 100);
