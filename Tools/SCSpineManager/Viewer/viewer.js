// SCSpineManager 뷰어 렌더링
// 메시지 (C# → JS):  { type: "load", folderUrl, json, atlas, title }
//                    { type: "play", anims: [...], lip }   — 게임 방식: 전체 초기화 후 문자열 전체 재생 (CGManager.PlayAnimation)
//                                                              lip = { moving, still, seconds } (seconds < 0 = 계속 말함), 없으면 립 재생 안 함
//                    { type: "setTrack", track, anim }      — 디버그 방식: 그 트랙만 교체 (anim이 null이면 트랙 비움)
//                    { type: "reset" }                      — 전체 트랙 초기화 + 기본 자세
//                    { type: "skin", name }                  — 스킨 변경 (cb_ 등 스킨이 여러 개인 데이터)
//                    { type: "pause", paused }               — 재생/일시정지 (전체 timeScale 0/1, SpineDebugScene과 같음)
//                    { type: "seek", value }                 — 일시정지 중 시점 이동: 모든 트랙을 (각 길이 × value)로 (0~1)
//                    { type: "bones" }                       — 기본 자세와 달라진 본 목록 요청
// 메시지 (JS → C#):  { type: "ready" } / { type: "loaded", ... } / { type: "error", message }
//                    { type: "state", tracks, paused, progress, lip } — 트랙 상태 (10회/초)
//                    { type: "bones", bones: [...] }        — 본 목록 응답
//                    { type: "key", key }                    — 뷰어 화면에 포커스가 있을 때 누른 단축키 (C#에서 처리)
"use strict";

const host = window.chrome && window.chrome.webview;
const post = (msg) => { if (host) host.postMessage(msg); };

const stageEl = document.getElementById("stage");
const messageEl = document.getElementById("message");

const app = new PIXI.Application({
  resizeTo: stageEl,
  backgroundAlpha: 0,            // 투명 (뒤의 체크무늬가 보임)
  antialias: true,
  autoDensity: true,
  resolution: window.devicePixelRatio || 1,
});
stageEl.appendChild(app.view);

// world: 확대·이동을 적용하는 컨테이너. Spine은 이 안에 둔다
const world = new PIXI.Container();
app.stage.addChild(world);

const current = { spine: null, jsonUrl: null, atlas: null, fitBounds: null, loadId: 0, paused: false };

// 립 재생 상태 (CGManager.LipSyncCoroutine: 보이스 길이 동안 moving → 이후 still)
// { moving, still, seconds(<0 = 계속), elapsed, phase: "moving" | "still" }
let lip = null;

// ------------------------------------------------------------------ 불러오기

async function loadSet(msg) {
  const loadId = ++current.loadId;
  messageEl.textContent = `불러오는 중… ${msg.title || ""}`;
  try {
    const atlasText = await (await fetch(msg.folderUrl + encodeURIComponent(msg.atlas))).text();
    // atlas 안의 페이지명(cb_xxx.png)은 같은 폴더 기준
    const atlas = await new Promise((resolve, reject) => {
      const a = new PIXI.spine.TextureAtlas(atlasText,
        (page, cb) => cb(PIXI.BaseTexture.from(msg.folderUrl + encodeURIComponent(page))),
        (loaded) => loaded ? resolve(loaded) : reject(new Error("atlas 읽기 실패")));
      if (a.pages && a.pages.length && !a.pages.some(p => !p.baseTexture)) resolve(a);   // 동기 완료된 경우
    });
    const jsonUrl = msg.folderUrl + encodeURIComponent(msg.json);
    const res = await PIXI.Assets.load({ src: jsonUrl, data: { spineAtlas: atlas } });
    if (loadId !== current.loadId) return;   // 그사이 다른 세트를 골랐으면 버림

    unloadCurrent();
    lip = null;
    const spine = new PIXI.spine.Spine(res.spineData);
    spine.autoUpdate = true;
    world.addChild(spine);
    current.spine = spine;
    current.jsonUrl = jsonUrl;
    current.atlas = atlas;
    spine.state.timeScale = current.paused ? 0 : 1;   // 일시정지 상태 유지

    // 추가: 스킨 선택 — default 스킨에는 머리 등 일부만 들어 있는 데이터가 있음(cb_: default/normal/normal_j/reverse/reverse_j)
    //       normal이 있으면 normal, 없으면 default가 아닌 첫 스킨, 그것도 없으면 default
    const skinNames = res.spineData.skins.map(s => s.name);
    const skin = skinNames.includes("normal") ? "normal" : (skinNames.find(n => n !== "default") || "default");
    applySkin(skin);

    // 화면 맞춤 기준은 기본 자세(setup pose)의 범위 (스킨 적용 후)
    spine.skeleton.setToSetupPose();
    spine.skeleton.updateWorldTransform();
    current.fitBounds = spine.getLocalBounds().clone();
    fitToView();

    messageEl.textContent = "";
    post({
      type: "loaded",
      title: msg.title,
      version: res.spineData.version,
      width: res.spineData.width,
      height: res.spineData.height,
      bones: res.spineData.bones.length,
      skins: skinNames,
      skin,
      animations: res.spineData.animations.map(a => ({
        name: a.name,
        duration: a.duration,
        loopStart: findEventTime(a, "loop_start"),
      })),
    });
  } catch (e) {
    if (loadId !== current.loadId) return;
    messageEl.textContent = `불러오기 실패: ${e.message || e}`;
    post({ type: "error", message: String(e && e.message || e) });
  }
}

/** 스킨 적용 (default는 스킨 없음 상태) — 붙어 있던 부착물을 새 스킨 기준으로 다시 설정 */
function applySkin(name) {
  const spine = current.spine;
  if (!spine) return;
  if (name && name !== "default") spine.skeleton.setSkinByName(name);
  else spine.skeleton.setSkin(null);
  spine.skeleton.setSlotsToSetupPose();
}

function unloadCurrent() {
  if (current.spine) {
    world.removeChild(current.spine);
    current.spine.destroy({ children: true });
    current.spine = null;
  }
  if (current.jsonUrl) { PIXI.Assets.unload(current.jsonUrl); current.jsonUrl = null; }
  if (current.atlas) { current.atlas.dispose(); current.atlas = null; }
}

/** 애니메이션 안 이벤트 시간 (없으면 -1). CGManager.GetLoopStartTime과 같은 기준 */
function findEventTime(anim, eventName) {
  for (const t of anim.timelines) {
    if (t.events && t.frames) {
      for (let i = 0; i < t.events.length; i++)
        if (t.events[i].data.name === eventName) return t.frames[i];
    }
  }
  return -1;
}

// ------------------------------------------------------------------ 재생 규칙 (CGManager와 동일)
// - 트랙: face_ → 1, lip_ → 2, arm_ → 3, 그 외(eye_ 포함) → 0      (CGManager.GetTrackIndex)
// - loop_start 이벤트가 있으면 1회 재생 → 끝나면 loop_start 시점부터 다시 1회 재생을 반복 (EndLoop)
// - wait, blank는 무한 반복 (FORCE_LOOP_ANIMS)
// - 그 외에는 1회 재생 후 마지막 동작 유지 (Complete 시 timeScale = 0)
// - 문자열 재생 시 전체 트랙 초기화 + 기본 자세 (ClearTracks + SetToSetupPose)

const FORCE_LOOP_ANIMS = new Set(["wait", "blank"]);

function trackIndex(name) {
  const lower = name.toLowerCase();
  if (lower.startsWith("face_")) return 1;
  if (lower.startsWith("lip_")) return 2;
  if (lower.startsWith("arm_")) return 3;
  return 0;
}

/** 애니메이션 1개를 지정 트랙에 게임 규칙대로 건다. 없는 이름이면 false */
function applyAnim(track, name) {
  const spine = current.spine;
  const anim = spine.spineData.findAnimation(name);
  if (!anim) return false;

  const loopStart = findEventTime(anim, "loop_start");
  if (loopStart >= 0) {
    const restart = () => {
      const again = spine.state.setAnimation(track, name, false);
      again.trackTime = loopStart;
      again.listener = { complete: restart };
    };
    spine.state.setAnimation(track, name, false).listener = { complete: restart };
  } else if (FORCE_LOOP_ANIMS.has(name)) {
    spine.state.setAnimation(track, name, true);
  } else {
    const entry = spine.state.setAnimation(track, name, false);
    entry.listener = { complete: (e) => { e.timeScale = 0; } };
  }
  return true;
}

/** 게임 방식: 전체 초기화 후 목록 전체를 접두어 기준 트랙에 재생 */
function playAnims(names, lipInfo) {
  const spine = current.spine;
  if (!spine) return;
  spine.state.clearTracks();
  spine.skeleton.setToSetupPose();
  for (const name of names) applyAnim(trackIndex(name), name);
  startLip(lipInfo);   // 게임도 PlayAnimation 다음에 립 코루틴을 시작함 (lip 트랙을 덮어씀)
}

// ------------------------------------------------------------------ 립 재생

function startLip(info) {
  lip = null;
  const spine = current.spine;
  if (!spine || !info || !info.moving || !spine.spineData.findAnimation(info.moving)) return;
  spine.state.setAnimation(2, info.moving, true);
  lip = { moving: info.moving, still: info.still, seconds: info.seconds, elapsed: 0, phase: "moving" };
}

/** 말하기 끝 → 멈춤 립 (없으면 lip 트랙을 그 자리에서 정지 — 게임과 같음) */
function switchLipToStill() {
  const spine = current.spine;
  if (!spine || !lip) return;
  if (lip.still && spine.spineData.findAnimation(lip.still)) spine.state.setAnimation(2, lip.still, true);
  else { const e = spine.state.tracks[2]; if (e) e.timeScale = 0; }
  lip.phase = "still";
}

// 말하는 시간 계산 (일시정지·속도 반영)
app.ticker.add(() => {
  const spine = current.spine;
  if (!spine || !lip || lip.phase !== "moving" || lip.seconds < 0) return;
  lip.elapsed += (app.ticker.deltaMS / 1000) * spine.state.timeScale;
  if (lip.elapsed >= lip.seconds) switchLipToStill();
});

// ------------------------------------------------------------------ 재생 제어

function setPaused(paused) {
  current.paused = !!paused;
  if (current.spine) current.spine.state.timeScale = current.paused ? 0 : 1;
}

/** 일시정지 중 시점 이동: 각 트랙을 자기 길이 × value 지점으로 (SpineDebugManager.OnSliderChanged와 같음) */
function seek(value) {
  const spine = current.spine;
  if (!spine) return;
  for (let i = 0; i < 4; i++) {
    const e = spine.state.tracks[i];
    if (!e || !e.animation) continue;
    if (i === 2 && lip) continue;   // 립 트랙은 아래에서 말하는 시간 기준으로 처리
    e.trackTime = e.animation.duration * value;
  }
  // 립: 기본 트랙 시점을 "말하기 시작 후 경과 시간"으로 보고 moving/still 결정
  if (lip && lip.seconds >= 0) {
    const t0 = spine.state.tracks[0];
    const elapsed = t0 && t0.animation ? t0.animation.duration * value : 0;
    lip.elapsed = elapsed;
    if (elapsed >= lip.seconds && lip.phase === "moving") switchLipToStill();
    else if (elapsed < lip.seconds && lip.phase === "still") {
      spine.state.setAnimation(2, lip.moving, true);
      lip.phase = "moving";
    }
    const e2 = spine.state.tracks[2];
    if (e2 && e2.animation && lip.phase === "moving") e2.trackTime = elapsed;
  }
}

/** 기본 자세와 달라진 본 (회전 0.1도, 위치 0.1, 크기 0.01 이상 차이) */
function changedBones() {
  const spine = current.spine;
  if (!spine) return [];
  const out = [];
  for (const b of spine.skeleton.bones) {
    const d = b.data;
    const dr = b.rotation - d.rotation, dx = b.x - d.x, dy = b.y - d.y, dsx = b.scaleX - d.scaleX, dsy = b.scaleY - d.scaleY;
    if (Math.abs(dr) > 0.1 || Math.abs(dx) > 0.1 || Math.abs(dy) > 0.1 || Math.abs(dsx) > 0.01 || Math.abs(dsy) > 0.01)
      out.push({ name: d.name, rotation: b.rotation, dr, dx, dy, dsx, dsy });
  }
  return out;
}

/** 디버그 방식: 지정 트랙만 교체 (나머지 트랙은 계속 재생) */
function setTrack(track, name) {
  const spine = current.spine;
  if (!spine) return;
  if (track === 2) lip = null;   // 립 트랙을 직접 바꾸면 립 재생 중지
  if (name) { applyAnim(track, name); return; }
  spine.state.clearTrack(track);
  if (track === 0) spine.skeleton.setToSetupPose();   // 기본 트랙을 비우면 기본 자세로 (디버그 씬과 같음)
}

function resetAll() {
  const spine = current.spine;
  if (!spine) return;
  lip = null;
  spine.state.clearTracks();
  spine.skeleton.setToSetupPose();
}

/** 트랙 상태를 C#으로 (10회/초) — progress는 기본 트랙(없으면 첫 트랙) 진행률 0~1 */
setInterval(() => {
  const spine = current.spine;
  if (!spine) return;
  const tracks = [];
  for (let i = 0; i < 4; i++) {
    const e = spine.state.tracks[i];
    if (!e || !e.animation) { tracks.push(null); continue; }
    const duration = e.animation.duration;
    tracks.push({
      name: e.animation.name,
      time: e.loop && duration > 0 ? e.trackTime % duration : Math.min(e.trackTime, duration),
      duration,
      loop: e.loop,
      holding: e.timeScale === 0,
    });
  }
  const first = tracks.find(t => t);
  post({
    type: "state",
    tracks,
    paused: current.paused,
    progress: first && first.duration > 0 ? first.time / first.duration : 0,
    lip: lip ? { phase: lip.phase, elapsed: lip.elapsed, seconds: lip.seconds, moving: lip.moving, still: lip.still } : null,
  });
}, 100);

// ------------------------------------------------------------------ 화면 맞춤·확대·이동

function fitToView() {
  const b = current.fitBounds;
  if (!current.spine || !b || b.width <= 0 || b.height <= 0) return;
  const w = app.screen.width, h = app.screen.height;
  const scale = Math.min(w * 0.9 / b.width, h * 0.9 / b.height);
  world.scale.set(scale);
  world.position.set(w / 2 - (b.x + b.width / 2) * scale, h / 2 - (b.y + b.height / 2) * scale);
}

// 휠: 커서 위치 기준 확대/축소
app.view.addEventListener("wheel", (e) => {
  e.preventDefault();
  const factor = e.deltaY < 0 ? 1.1 : 1 / 1.1;
  const next = Math.min(50, Math.max(0.02, world.scale.x * factor));
  const k = next / world.scale.x;
  world.position.set(e.offsetX - (e.offsetX - world.x) * k, e.offsetY - (e.offsetY - world.y) * k);
  world.scale.set(next);
}, { passive: false });

// 드래그: 이동 / 더블클릭: 화면 맞춤
let drag = null;
app.view.addEventListener("pointerdown", (e) => {
  if (e.button !== 0) return;
  drag = { x: e.clientX, y: e.clientY, wx: world.x, wy: world.y };
  app.view.setPointerCapture(e.pointerId);
});
app.view.addEventListener("pointermove", (e) => {
  if (!drag) return;
  world.position.set(drag.wx + e.clientX - drag.x, drag.wy + e.clientY - drag.y);
});
app.view.addEventListener("pointerup", () => { drag = null; });
app.view.addEventListener("dblclick", fitToView);

// ------------------------------------------------------------------ 메시지

if (host) {
  host.addEventListener("message", (e) => {
    const msg = e.data;
    if (!msg) return;
    switch (msg.type) {
      case "load": loadSet(msg); break;
      case "play": playAnims(msg.anims || [], msg.lip); break;
      case "setTrack": setTrack(msg.track, msg.anim); break;
      case "reset": resetAll(); break;
      case "skin": applySkin(msg.name); break;
      case "pause": setPaused(msg.paused); break;
      case "seek": seek(msg.value); break;
      case "bones": post({ type: "bones", bones: changedBones() }); break;
    }
  });
}
// 뷰어 화면에 포커스가 있을 때의 단축키는 C#으로 넘겨 같은 곳에서 처리
window.addEventListener("keydown", (e) => {
  if (e.repeat || e.ctrlKey || e.altKey) return;
  post({ type: "key", key: e.key });
});

post({ type: "ready" });
