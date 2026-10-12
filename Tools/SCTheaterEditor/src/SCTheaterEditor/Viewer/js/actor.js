// 캐릭터 1명: Spine 불러오기(세트별 참조 횟수 관리) + 게임 재생 규칙(CGManager와 동일) + 립 재생
// 재생 규칙
// - 트랙: face_ → 1, lip_ → 2, arm_ → 3, 그 외(eye_ 포함) → 0      (CGManager.GetTrackIndex)
// - loop_start 이벤트가 있으면 1회 재생 → 끝나면 loop_start 시점부터 다시 1회 재생을 반복 (EndLoop)
// - wait, blank는 무한 반복 (FORCE_LOOP_ANIMS)
// - 그 외에는 1회 재생 후 마지막 동작 유지 (Complete 시 timeScale = 0)
// - 문자열 재생 시 전체 트랙 초기화 + 기본 자세 (ClearTracks + SetToSetupPose)

const FORCE_LOOP_ANIMS = new Set(["wait", "blank"]);
let trackCount = 4;

/** C#이 내려준 설정 (트랙 수 등) */
export function configure(cfg) {
  if (cfg && cfg.trackCount) trackCount = cfg.trackCount;
}

export function trackIndex(name) {
  const lower = name.toLowerCase();
  if (lower.startsWith("face_")) return 1;
  if (lower.startsWith("lip_")) return 2;
  if (lower.startsWith("arm_")) return 3;
  return 0;
}

/** 애니메이션 안 이벤트 시간 (없으면 -1). CGManager.GetLoopStartTime과 같은 기준 */
export function findEventTime(anim, eventName) {
  for (const t of anim.timelines) {
    if (t.events && t.frames) {
      for (let i = 0; i < t.events.length; i++)
        if (t.events[i].data.name === eventName) return t.frames[i];
    }
  }
  return -1;
}

// ------------------------------------------------------------------ 세트 데이터 (참조 횟수)
// 같은 세트를 여러 캐릭터가 써도 한 번만 불러오고, 마지막 사용자가 내려갈 때 해제

const cache = new Map();   // jsonUrl → { count, promise: Promise<{ spineData, atlas }> }

async function loadData(folderUrl, jsonUrl, atlasFile) {
  const atlasText = await (await fetch(folderUrl + encodeURIComponent(atlasFile))).text();
  // atlas 안의 페이지명(cb_xxx.png)은 같은 폴더 기준
  const atlas = await new Promise((resolve, reject) => {
    const a = new PIXI.spine.TextureAtlas(atlasText,
      (page, cb) => cb(PIXI.BaseTexture.from(folderUrl + encodeURIComponent(page))),
      (loaded) => loaded ? resolve(loaded) : reject(new Error("atlas 읽기 실패")));
    if (a.pages && a.pages.length && !a.pages.some(p => !p.baseTexture)) resolve(a);   // 동기 완료된 경우
  });
  const res = await PIXI.Assets.load({ src: jsonUrl, data: { spineAtlas: atlas } });
  return { spineData: res.spineData, atlas };
}

async function acquire(folderUrl, jsonFile, atlasFile) {
  const key = folderUrl + encodeURIComponent(jsonFile);
  let entry = cache.get(key);
  if (!entry) {
    entry = { count: 0, promise: loadData(folderUrl, key, atlasFile) };
    cache.set(key, entry);
    entry.promise.catch(() => { if (cache.get(key) === entry) cache.delete(key); });
  }
  entry.count++;
  try {
    return { key, data: await entry.promise };
  } catch (e) {
    entry.count--;
    throw e;
  }
}

function release(key) {
  const entry = cache.get(key);
  if (!entry || --entry.count > 0) return;
  cache.delete(key);
  entry.promise.then((d) => {
    PIXI.Assets.unload(key);
    d.atlas.dispose();
  }).catch(() => {});
}

/** 지금 불러와 둔 세트 수 (확인용) */
export function cachedSetCount() { return cache.size; }

// ------------------------------------------------------------------ 캐릭터

export class SpineActor {
  constructor(id) {
    this.id = id;
    this.container = new PIXI.Container();   // 무대가 위치·스케일을 정하는 상자 (원점 = 발 위치)
    this.spine = null;
    this.key = null;
    this.lip = null;          // { moving, still, seconds(<0 = 계속), elapsed, phase: "moving" | "still" }
    this.paused = false;
    this.clock = 0;           // 추가: 재생 시작(play·reset) 후 경과 초 (일시정지 반영, 시점 이동 시 그 초로)
    this.destroyed = false;
  }

  /** 불러오기. 성공하면 C#에 보낼 정보를 돌려줌 (그사이 내려졌으면 null) */
  async load(msg) {
    const { key, data } = await acquire(msg.folderUrl, msg.json, msg.atlas);
    if (this.destroyed) { release(key); return null; }
    this.key = key;
    const spine = new PIXI.spine.Spine(data.spineData);
    spine.autoUpdate = true;
    this.container.addChild(spine);
    this.spine = spine;
    spine.state.timeScale = this.paused ? 0 : 1;

    // 스킨: normal이 있으면 normal, 없으면 default가 아닌 첫 스킨, 그것도 없으면 default (cb_는 default에 머리만 있음)
    const skinNames = data.spineData.skins.map(s => s.name);
    const skin = skinNames.includes("normal") ? "normal" : (skinNames.find(n => n !== "default") || "default");
    this.applySkin(skin);
    spine.skeleton.setToSetupPose();
    spine.skeleton.updateWorldTransform();

    return {
      version: data.spineData.version || "",
      width: data.spineData.width,
      height: data.spineData.height,
      boneCount: data.spineData.bones.length,
      skins: skinNames,
      skin,
      animations: data.spineData.animations.map(a => ({
        name: a.name,
        duration: a.duration,
        loopStart: findEventTime(a, "loop_start"),
      })),
    };
  }

  destroy() {
    this.destroyed = true;
    if (this.spine) {
      this.container.removeChild(this.spine);
      this.spine.destroy({ children: true });
      this.spine = null;
    }
    if (this.key) { release(this.key); this.key = null; }
    this.container.destroy({ children: true });
  }

  /** 스킨 적용 (default는 스킨 없음 상태) — 붙어 있던 부착물을 새 스킨 기준으로 다시 설정 */
  applySkin(name) {
    const spine = this.spine;
    if (!spine) return;
    if (name && name !== "default") spine.skeleton.setSkinByName(name);
    else spine.skeleton.setSkin(null);
    spine.skeleton.setSlotsToSetupPose();
  }

  /** 애니메이션 1개를 지정 트랙에 게임 규칙대로 건다. 없는 이름이면 false */
  applyAnim(track, name) {
    const spine = this.spine;
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

  /** 게임 방식: 전체 초기화 후 목록 전체를 접두어 기준 트랙에 재생 + 립 */
  play(names, lipInfo) {
    const spine = this.spine;
    if (!spine) return;
    spine.state.clearTracks();
    spine.skeleton.setToSetupPose();
    this.clock = 0;   // 추가
    for (const name of names) this.applyAnim(trackIndex(name), name);
    this.startLip(lipInfo);   // 게임도 PlayAnimation 다음에 립 코루틴을 시작함 (lip 트랙을 덮어씀)
  }

  /** 디버그 방식: 지정 트랙만 교체 (나머지 트랙은 계속 재생) */
  setTrack(track, name) {
    const spine = this.spine;
    if (!spine) return;
    if (track === 2) this.lip = null;   // 립 트랙을 직접 바꾸면 립 재생 중지
    if (name) { this.applyAnim(track, name); return; }
    spine.state.clearTrack(track);
    if (track === 0) spine.skeleton.setToSetupPose();   // 기본 트랙을 비우면 기본 자세로 (디버그 씬과 같음)
  }

  reset() {
    const spine = this.spine;
    if (!spine) return;
    this.lip = null;
    this.clock = 0;   // 추가
    spine.state.clearTracks();
    spine.skeleton.setToSetupPose();
  }

  // ---------------------------------------------------------------- 립 (CGManager.LipSyncCoroutine: 보이스 길이 동안 moving → 이후 still)

  startLip(info) {
    this.lip = null;
    const spine = this.spine;
    if (!spine || !info || !info.moving || !spine.spineData.findAnimation(info.moving)) return;
    spine.state.setAnimation(2, info.moving, true);
    this.lip = { moving: info.moving, still: info.still, seconds: info.seconds, elapsed: 0, phase: "moving" };
  }

  /** 말하기 끝 → 멈춤 립 (없으면 lip 트랙을 그 자리에서 정지 — 게임과 같음) */
  switchLipToStill() {
    const spine = this.spine, lip = this.lip;
    if (!spine || !lip) return;
    if (lip.still && spine.spineData.findAnimation(lip.still)) spine.state.setAnimation(2, lip.still, true);
    else { const e = spine.state.tracks[2]; if (e) e.timeScale = 0; }
    lip.phase = "still";
  }

  /** 매 프레임: 경과 시간·말하는 시간 계산 (일시정지 반영) */
  tick(deltaMS) {
    const spine = this.spine, lip = this.lip;
    if (!spine) return;
    this.clock += (deltaMS / 1000) * spine.state.timeScale;   // 추가
    if (!lip || lip.phase !== "moving" || lip.seconds < 0) return;
    lip.elapsed += (deltaMS / 1000) * spine.state.timeScale;
    if (lip.elapsed >= lip.seconds) this.switchLipToStill();
  }

  // ---------------------------------------------------------------- 재생 제어

  setPaused(paused) {
    this.paused = !!paused;
    if (this.spine) this.spine.state.timeScale = this.paused ? 0 : 1;
  }

  // 변경: 시점 이동을 "트랙별 자기 길이 × 비율"(디버그 씬 방식) → 실제 초 기준으로
  /**
   * 일시정지 중 시점 이동: 모든 트랙이 동시에 시작해 t초 지난 상태로 (게임과 같은 규칙)
   * - wait·blank(반복): t를 길이로 나눈 나머지 (엔진이 처리)
   * - loop_start: 끝까지 1회 → 그 뒤로는 loop_start~끝 구간 반복
   * - 그 외: 끝이면 마지막 동작 유지, 끝보다 앞이면 다시 재생 가능 상태로
   * - 립: t를 말하기 시작 후 경과 시간으로 보고 말하는 중/멈춤 결정
   */
  seekTo(t) {
    const spine = this.spine, lip = this.lip;
    if (!spine) return;
    t = Math.max(0, t);
    for (let i = 0; i < trackCount; i++) {
      const e = spine.state.tracks[i];
      if (!e || !e.animation) continue;
      if (i === 2 && lip) continue;   // 립 트랙은 아래에서 처리
      const d = e.animation.duration;
      if (e.loop) { e.trackTime = t; continue; }
      const ls = findEventTime(e.animation, "loop_start");
      if (ls >= 0) {
        e.trackTime = t <= d || d - ls <= 0 ? Math.min(t, d) : ls + (t - d) % (d - ls);
        continue;
      }
      e.trackTime = Math.min(t, d);
      e.timeScale = t >= d ? 0 : 1;
    }
    if (lip) {
      if (lip.seconds >= 0) {
        lip.elapsed = t;
        if (t >= lip.seconds && lip.phase === "moving") this.switchLipToStill();
        else if (t < lip.seconds && lip.phase === "still") {
          spine.state.setAnimation(2, lip.moving, true);
          lip.phase = "moving";
        }
      }
      const e2 = spine.state.tracks[2];
      if (e2 && e2.animation && lip.phase === "moving") e2.trackTime = t;
    }
    this.clock = t;
  }

  /** 추가: 지금 걸린 트랙 중 가장 긴 애니메이션 길이 (드래그 바 길이) */
  length() {
    const spine = this.spine;
    if (!spine) return 0;
    let max = 0;
    for (let i = 0; i < trackCount; i++) {
      const e = spine.state.tracks[i];
      if (e && e.animation) max = Math.max(max, e.animation.duration);
    }
    return max;
  }

  /** 기본 자세와 달라진 본 (회전 0.1도, 위치 0.1, 크기 0.01 이상 차이) */
  changedBones() {
    const spine = this.spine;
    if (!spine) return [];
    const out = [];
    for (const b of spine.skeleton.bones) {
      const d = b.data;
      const dr = b.rotation - d.rotation, dx = b.x - d.x, dy = b.y - d.y, dsx = b.scaleX - d.scaleX, dsy = b.scaleY - d.scaleY;
      if (Math.abs(dr) > 0.1 || Math.abs(dx) > 0.1 || Math.abs(dy) > 0.1 || Math.abs(dsx) > 0.01 || Math.abs(dsy) > 0.01)
        out.push({ name: d.name, dr, dx, dy, dsx, dsy });
    }
    return out;
  }

  /** 트랙 상태 (C#의 ActorState) — 변경: progress 대신 clock(경과 초)·length(가장 긴 트랙) */
  state() {
    const spine = this.spine;
    if (!spine) return null;
    const tracks = [];
    for (let i = 0; i < trackCount; i++) {
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
    const lip = this.lip;
    return {
      id: this.id,
      tracks,
      paused: this.paused,
      clock: this.clock,
      length: this.length(),
      lip: lip ? { phase: lip.phase, elapsed: lip.elapsed, seconds: lip.seconds, moving: lip.moving, still: lip.still } : null,
    };
  }
}
