// 추가(6회차): 오디오 재생 — 채널마다 <audio> 하나 (같은 채널에 다시 재생하면 바꿈)
// 메시지 형식의 기준은 C# Core/Stage/StageMessages.cs (PlayAudio · StopAudio · SeekAudio / AudioStarted · AudioSeeked · AudioEnded · AudioError)
import { post } from "./bridge.js";

const channels = new Map();

function channel(name) {
  let a = channels.get(name);
  if (a) return a;
  a = new Audio();
  a.preload = "auto";
  a.addEventListener("ended", () => post({ type: "audioEnded", channel: name }));
  a.addEventListener("seeked", () => post({ type: "audioSeeked", channel: name, time: a.currentTime }));
  a.addEventListener("error", () => {
    if (!a.getAttribute("src")) return;   // 정지(src 비움)로 생긴 오류는 무시
    post({ type: "audioError", channel: name, url: a.src, message: a.error ? `code ${a.error.code} ${a.error.message || ""}` : "unknown" });
  });
  channels.set(name, a);
  return a;
}

export function play(msg) {
  const a = channel(msg.channel);
  a.pause();
  a.src = msg.url;
  a.loop = !!msg.loop;
  a.volume = Math.min(1, Math.max(0, msg.volume ?? 1));
  a.currentTime = msg.start || 0;
  a.play()
    .then(() => post({ type: "audioStarted", channel: msg.channel, url: msg.url, duration: Number.isFinite(a.duration) ? a.duration : -1 }))
    .catch((err) => post({ type: "audioError", channel: msg.channel, url: msg.url, message: err.message }));
}

/** 정지 (name이 null이면 전부) */
export function stop(name) {
  for (const [key, a] of channels) {
    if (name != null && key !== name) continue;
    a.pause();
    a.removeAttribute("src");
    a.load();
  }
}

export function seek(name, seconds) {
  const a = channels.get(name);
  if (a) a.currentTime = seconds;
}
