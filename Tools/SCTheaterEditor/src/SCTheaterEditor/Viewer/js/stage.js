// 1920×1080 무대: 배경 + 캐릭터 여러 명 + 선택 표시 + 드래그
// 좌표: C#이 보내는 x·y는 화면 중심 기준 px (y는 위가 +), 무대 안에서는 왼쪽 위 (0,0) 기준으로 바꿔 둠
// 화면 표시는 창 크기에 맞춰 비율을 유지한 채 축소 (바깥은 어두운 여백)
import { post } from "./bridge.js";
import { SpineActor } from "./actor.js";

const SELECT_COLOR = 0x4da3ff;
const PREVIEW_COLOR = 0xffa040;
const DIM_TINT = 0xa0a0a0;     // 선택하지 않은 캐릭터 (살짝 어둡게)

export class Stage {
  constructor(el) {
    this.el = el;
    this.app = new PIXI.Application({
      resizeTo: el,
      backgroundAlpha: 0,
      antialias: true,
      autoDensity: true,
      resolution: window.devicePixelRatio || 1,
    });
    el.appendChild(this.app.view);

    this.width = 1920;
    this.height = 1080;
    this.unitScale = 1;
    this.watchBones = false;     // 추가: 본 목록 실시간 갱신 (선택한 캐릭터 상태 메시지에 본 목록 포함)
    this.entries = new Map();   // id → { actor, x, y, scale, label, preview, visible, loaded }
    this.selectedId = null;
    this.bgUrl = null;
    this.bgToken = 0;
    this.drag = null;

    this.view = new PIXI.Container();         // 무대 전체 (화면 맞춤 축소)
    this.frame = new PIXI.Graphics();         // 배경이 없을 때의 바탕 + 테두리
    this.bg = new PIXI.Sprite(PIXI.Texture.EMPTY);
    this.bg.visible = false;
    this.layer = new PIXI.Container();        // 캐릭터
    this.overlay = new PIXI.Graphics();       // 선택·미리보기 발밑 표시
    this.labels = new PIXI.Container();       // 이름표
    this.view.addChild(this.frame, this.bg, this.layer, this.overlay, this.labels);
    this.app.stage.addChild(this.view);

    this.drawFrame();
    this.fit();
    new ResizeObserver(() => this.fit()).observe(el);

    this.app.ticker.add(() => {
      const dt = this.app.ticker.deltaMS;
      for (const e of this.entries.values()) e.actor.tick(dt);
    });
    // 상태를 C#으로 (10회/초) — 선택한 캐릭터가 있으면 그 캐릭터 트랙 상태(+본 목록), 없으면 무대 전체 시간 (변경)
    setInterval(() => {
      if (this.selectedId != null) {
        const e = this.entries.get(this.selectedId);
        if (!e || !e.loaded) return;
        const s = e.actor.state();
        if (this.watchBones) s.bones = e.actor.changedBones();
        post({ type: "state", ...s });
        return;
      }
      const loaded = this.loadedEntries();
      if (loaded.length) post({ type: "stageTime", ...this.groupTime(loaded) });
    }, 100);

    this.bindPointer();
  }

  configure(cfg) {
    this.width = cfg.screenWidth || 1920;
    this.height = cfg.screenHeight || 1080;
    this.unitScale = cfg.unitScale || 1;
    this.drawFrame();
    this.fit();
    for (const id of this.entries.keys()) this.applyPlacement(id);
  }

  drawFrame() {
    this.frame.clear();
    this.frame.beginFill(0x55585e).drawRect(0, 0, this.width, this.height).endFill();
    this.frame.lineStyle(4, 0x1e1f22).drawRect(0, 0, this.width, this.height);
  }

  /** 창 크기에 맞춰 비율 유지 축소 */
  fit() {
    const sw = this.app.screen.width, sh = this.app.screen.height;
    if (sw <= 0 || sh <= 0) return;
    const s = Math.min(sw / this.width, sh / this.height) * 0.98;
    this.view.scale.set(s);
    this.view.position.set((sw - this.width * s) / 2, (sh - this.height * s) / 2);
  }

  // ---------------------------------------------------------------- 배경

  async setBackground(url) {
    const token = ++this.bgToken;
    const prev = this.bgUrl;
    this.bgUrl = url || null;
    if (!url) {
      this.bg.visible = false;
      this.bg.texture = PIXI.Texture.EMPTY;
    } else {
      try {
        const tex = await PIXI.Assets.load(url);
        if (token !== this.bgToken) return;
        this.bg.texture = tex;
        this.bg.width = this.width;
        this.bg.height = this.height;
        this.bg.visible = true;
      } catch (e) {
        if (token === this.bgToken) this.bg.visible = false;
        console.warn("배경 불러오기 실패", url, e);
        if (token === this.bgToken) post({ type: "backgroundError", url, message: String((e && e.message) || e) });   // 추가: TE 로그에 남김
      }
    }
    if (prev && prev !== url) PIXI.Assets.unload(prev).catch(() => {});
  }

  // ---------------------------------------------------------------- 캐릭터

  async add(msg) {
    if (this.entries.has(msg.id)) this.remove(msg.id);   // 다시 보내면 새로 (페이지 새로 고침 후 동기화 등)
    const actor = new SpineActor(msg.id);   // 변경: 새 캐릭터는 재생 상태로 시작 (일시정지는 캐릭터별)
    const label = new PIXI.Text(msg.label || "", {
      fontFamily: "Malgun Gothic", fontSize: 30, fill: 0xffffff, stroke: 0x000000, strokeThickness: 5,
    });
    label.anchor.set(0.5, 0);
    label.visible = false;
    this.labels.addChild(label);
    const entry = { actor, x: 0, y: -180, scale: 1, label, name: msg.label || "", preview: !!msg.preview, visible: true, loaded: false };
    this.entries.set(msg.id, entry);
    this.layer.addChild(actor.container);
    this.applyPlacement(msg.id);
    this.refreshMarks();

    try {
      const info = await actor.load(msg);
      if (!info || this.entries.get(msg.id) !== entry) return;   // 그사이 내려짐
      entry.loaded = true;
      // 발밑 표시 높이: 기본 자세에서 캐릭터 아래 끝 (stand는 원점이 허리 근처라 원점에 그리면 허리띠처럼 보임)
      const b = actor.spine.getLocalBounds();
      entry.localBottom = b.y + b.height;
      this.refreshMarks();
      post({ type: "actorLoaded", id: msg.id, ...info });
    } catch (e) {
      if (this.entries.get(msg.id) !== entry) return;
      post({ type: "actorError", id: msg.id, message: String((e && e.message) || e) });
    }
  }

  remove(id) {
    const e = this.entries.get(id);
    if (!e) return;
    this.entries.delete(id);
    this.layer.removeChild(e.actor.container);
    this.labels.removeChild(e.label);
    e.label.destroy();
    e.actor.destroy();
    if (this.selectedId === id) this.selectedId = null;
    this.refreshMarks();
  }

  place(msg) {
    const e = this.entries.get(msg.id);
    if (!e) return;
    e.x = msg.x; e.y = msg.y; e.scale = msg.scale;
    this.applyPlacement(msg.id);
    this.refreshMarks();
  }

  applyPlacement(id) {
    const e = this.entries.get(id);
    if (!e) return;
    e.actor.container.position.set(this.width / 2 + e.x, this.height / 2 - e.y);
    e.actor.container.scale.set(e.scale * this.unitScale);
  }

  show(id, visible) {
    const e = this.entries.get(id);
    if (!e) return;
    e.visible = !!visible;
    e.actor.container.visible = e.visible;
    this.refreshMarks();
  }

  setPreview(id, preview) {
    const e = this.entries.get(id);
    if (!e) return;
    e.preview = !!preview;
    this.refreshMarks();
  }

  select(id) {
    this.selectedId = id == null ? null : id;
    this.refreshMarks();
  }

  get(id) { return this.entries.get(id); }

  // ---------------------------------------------------------------- 재생 (변경: 캐릭터별 일시정지, id 없음 = 무대 전체)

  loadedEntries() { return [...this.entries.values()].filter(e => e.loaded); }

  /** 무대 전체 시간: 길이 = 모든 캐릭터·트랙 중 가장 긴 애니메이션, 시간 = 가장 많이 진행된 캐릭터 (길이에서 멈춤) */
  groupTime(list) {
    let length = 0, clock = 0;
    for (const e of list) {
      length = Math.max(length, e.actor.length());
      clock = Math.max(clock, e.actor.clock);
    }
    return { time: Math.min(clock, length), length, paused: list.every(e => e.actor.paused) };
  }

  /** 재생/일시정지. 무대 전체 재생은 먼저 모두를 드래그 바 시점으로 맞춤 (바가 끝이면 0초부터) */
  setPaused(paused, id) {
    if (id != null) {
      const e = this.entries.get(id);
      if (e) e.actor.setPaused(paused);
      return;
    }
    if (!paused) {
      const loaded = this.loadedEntries();
      const { time, length } = this.groupTime(loaded);
      const t = length > 0 && time >= length ? 0 : time;
      for (const e of loaded) e.actor.seekTo(t);
    }
    for (const e of this.entries.values()) e.actor.setPaused(paused);
  }

  /** 일시정지 중 시점 이동 (실제 초). id 없음 = 무대 전체 */
  seek(id, seconds) {
    if (id != null) {
      const e = this.entries.get(id);
      if (e && e.loaded) e.actor.seekTo(seconds);
      return;
    }
    for (const e of this.loadedEntries()) e.actor.seekTo(seconds);
  }

  // ---------------------------------------------------------------- 선택 표시 (발밑 표시 + 이름표 + 나머지 살짝 어둡게)

  refreshMarks() {
    const g = this.overlay;
    g.clear();
    const anyFocus = this.selectedId != null || [...this.entries.values()].some(e => e.preview);
    for (const [id, e] of this.entries) {
      const c = e.actor.container;
      const focused = id === this.selectedId || e.preview;
      const spine = e.actor.spine;
      if (spine) spine.tint = !anyFocus || focused ? 0xffffff : DIM_TINT;
      e.label.visible = e.visible && focused;
      if (!e.visible || !focused) continue;

      const color = e.preview ? PREVIEW_COLOR : SELECT_COLOR;
      const rx = 170 * e.scale, ry = 28 * e.scale;
      // 캐릭터 아래 끝 (무대 밖으로 나가면 무대 아래쪽 가장자리)
      const footY = Math.min(c.y + (e.localBottom ?? 0) * c.scale.y, this.height - ry - 50);
      g.lineStyle(6, color, 0.95).beginFill(color, 0.25).drawEllipse(c.x, footY, rx, ry).endFill();
      e.label.text = e.preview ? `${e.name}  · 미리보기 (Enter 적용 / Esc 취소)` : e.name;
      e.label.style.fill = e.preview ? PREVIEW_COLOR : 0xffffff;
      e.label.position.set(c.x, footY + ry + 4);
    }
  }

  // ---------------------------------------------------------------- 클릭 선택·드래그 (놓을 때만 C#에 알림, 자리 결정은 C#)

  /** 화면 좌표의 맨 위 캐릭터 (보이는 것, 위에 그려진 것부터) */
  hitTest(sx, sy) {
    const list = [...this.entries.entries()].filter(([, e]) => e.visible && e.loaded);
    list.sort((a, b) => this.layer.getChildIndex(b[1].actor.container) - this.layer.getChildIndex(a[1].actor.container));
    for (const [id, e] of list) {
      const b = e.actor.container.getBounds();
      if (sx >= b.x && sx <= b.x + b.width && sy >= b.y && sy <= b.y + b.height) return id;
    }
    return null;
  }

  bindPointer() {
    const view = this.app.view;
    view.addEventListener("pointerdown", (ev) => {
      if (ev.button !== 0) return;
      const id = this.hitTest(ev.offsetX, ev.offsetY);
      post({ type: "clicked", id });
      if (id == null) return;
      const e = this.entries.get(id);
      this.layer.setChildIndex(e.actor.container, this.layer.children.length - 1);   // 잡은 캐릭터를 맨 위로
      this.drag = { id, startX: ev.offsetX, origX: e.actor.container.x, moved: false };
      view.setPointerCapture(ev.pointerId);
    });
    view.addEventListener("pointermove", (ev) => {
      const d = this.drag;
      if (!d) return;
      const dx = (ev.offsetX - d.startX) / this.view.scale.x;
      if (Math.abs(dx) > 4) d.moved = true;
      if (!d.moved) return;
      const e = this.entries.get(d.id);
      if (!e) { this.drag = null; return; }
      e.actor.container.x = d.origX + dx;
      this.refreshMarks();
    });
    view.addEventListener("pointerup", () => {
      const d = this.drag;
      this.drag = null;
      if (!d || !d.moved) return;
      const e = this.entries.get(d.id);
      if (e) post({ type: "dropped", id: d.id, x: e.actor.container.x - this.width / 2 });
    });
  }
}
