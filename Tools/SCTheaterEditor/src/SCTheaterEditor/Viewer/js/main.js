// 무대 페이지 시작점: C# 메시지 → 무대·캐릭터 동작 연결
// 메시지 형식의 기준은 C# Core/Stage/StageMessages.cs (type 값이 같아야 함)
import { post, onMessage } from "./bridge.js";
import { configure } from "./actor.js";
import { Stage } from "./stage.js";
import * as audio from "./audio.js";   // 추가(6회차)

const stage = new Stage(document.getElementById("stage"));
const messageEl = document.getElementById("message");

/** 캐릭터 1명에 대한 동작 (없으면 무시) */
function withActor(id, fn) {
  const e = stage.get(id);
  if (e && e.loaded) fn(e.actor);
}

onMessage((msg) => {
  switch (msg.type) {
    case "config": configure(msg); stage.configure(msg); break;
    case "background": stage.setBackground(msg.url); break;
    case "addActor": stage.add(msg); break;
    case "removeActor": stage.remove(msg.id); break;
    case "place": stage.place(msg); break;
    case "show": stage.show(msg.id, msg.visible); break;
    case "preview": stage.setPreview(msg.id, msg.preview); break;
    case "select": stage.select(msg.id); break;
    case "play": withActor(msg.id, a => a.play(msg.anims || [], msg.lip)); break;
    case "setTrack": withActor(msg.id, a => a.setTrack(msg.track, msg.anim)); break;
    case "reset": withActor(msg.id, a => a.reset()); break;
    case "skin": withActor(msg.id, a => a.applySkin(msg.name)); break;
    case "pause": stage.setPaused(msg.paused, msg.id); break;     // 변경: id 없음 = 무대 전체
    case "seek": stage.seek(msg.id, msg.seconds); break;           // 변경: 실제 초, id 없음 = 무대 전체
    case "watchBones": stage.watchBones = !!msg.on; break;         // 추가
    case "bones": withActor(msg.id, a => post({ type: "bones", id: msg.id, bones: a.changedBones() })); break;
    case "audioPlay": audio.play(msg); break;                         // 추가(6회차)
    case "audioStop": audio.stop(msg.channel); break;
    case "audioSeek": audio.seek(msg.channel, msg.seconds); break;
    case "audioPause": audio.pause(msg.channel, msg.paused); break;   // 추가(8회차)
    case "seekEnd": stage.seekEnd(msg.id); break;                     // 추가(8회차)
  }
});

// 무대에 포커스가 있을 때의 단축키는 C#으로 넘겨 같은 곳에서 처리 (Ctrl·Alt 조합은 C#이 직접 받음)
const FORWARD_KEYS = new Set([" ", "r", "R", "Enter", "Escape", "Delete", "F5"]);   // 변경(4-2): F5 스크립트 → 무대
window.addEventListener("keydown", (e) => {
  if (e.repeat || e.ctrlKey || e.altKey || !FORWARD_KEYS.has(e.key)) return;
  e.preventDefault();
  post({ type: "key", key: e.key, shift: e.shiftKey });
});

window.addEventListener("error", (e) => { messageEl.textContent = `오류: ${e.message}`; });
post({ type: "ready" });
