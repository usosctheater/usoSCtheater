// audio_hook.js — SCResourceGrabber가 페이지 생성 시점에 주입하는 스크립트.
// 받은 오디오 데이터의 URL을 기억해 두었다가, 재생되는 순간 어떤 URL인지 프로그램에 알린다.
//   fetch / XHR 응답(ArrayBuffer, Blob) → URL
//   decodeAudioData(ArrayBuffer) → AudioBuffer → URL
//   AudioBufferSourceNode.start / HTMLMediaElement.play → 'play' 메시지, 종료 시 'end' 메시지
// 게임 동작에 영향을 주지 않도록 모든 훅은 예외를 삼키고 원래 함수를 그대로 호출한다.
(() => {
    if (window.__scrgAudioHook) return;
    window.__scrgAudioHook = true;

    const post = (m) => {
        try { window.chrome && window.chrome.webview && window.chrome.webview.postMessage(JSON.stringify(m)); } catch (e) { }
    };
    const abs = (u) => { try { return new URL(u, location.href).href; } catch (e) { return String(u); } };
    const frameId = Math.random().toString(36).slice(2, 8);
    let seq = 0;
    const nextId = () => frameId + ':' + (++seq);

    const abUrl = new WeakMap();     // ArrayBuffer → url
    const blobUrl = new WeakMap();   // Blob → url
    const objUrl = new Map();        // blob:... → url
    const bufUrl = new WeakMap();    // AudioBuffer → url
    const stats = { decodeMapped: 0, decodeUnmapped: 0, startMapped: 0, startUnmapped: 0, mediaPlay: 0 };

    // ---------- fetch ----------
    try {
        const origFetch = window.fetch;
        if (origFetch) {
            window.fetch = function (input) {
                const reqUrl = abs(typeof input === 'string' ? input : (input && input.url) || String(input));
                return origFetch.apply(this, arguments).then((resp) => {
                    try {
                        const u = resp.url || reqUrl;
                        const ab = resp.arrayBuffer, bl = resp.blob;
                        resp.arrayBuffer = function () { return ab.call(this).then((x) => { try { abUrl.set(x, u); } catch (e) { } return x; }); };
                        resp.blob = function () { return bl.call(this).then((x) => { try { blobUrl.set(x, u); } catch (e) { } return x; }); };
                    } catch (e) { }
                    return resp;
                });
            };
        }
    } catch (e) { }

    // ---------- XHR (응답을 읽는 순간 매핑) ----------
    try {
        const XP = XMLHttpRequest.prototype;
        const open = XP.open;
        XP.open = function (method, url) { try { this.__scrgUrl = abs(url); } catch (e) { } return open.apply(this, arguments); };
        const desc = Object.getOwnPropertyDescriptor(XP, 'response');
        if (desc && desc.get) {
            Object.defineProperty(XP, 'response', {
                configurable: true, enumerable: desc.enumerable,
                get: function () {
                    const r = desc.get.call(this);
                    try {
                        const u = this.responseURL || this.__scrgUrl;
                        if (u && r) {
                            if (r instanceof ArrayBuffer) abUrl.set(r, u);
                            else if (r instanceof Blob) blobUrl.set(r, u);
                        }
                    } catch (e) { }
                    return r;
                }
            });
        }
    } catch (e) { }

    // ---------- Blob → ArrayBuffer / object URL ----------
    try {
        const bab = Blob.prototype.arrayBuffer;
        if (bab) {
            Blob.prototype.arrayBuffer = function () {
                const u = blobUrl.get(this);
                return bab.call(this).then((x) => { if (u) try { abUrl.set(x, u); } catch (e) { } return x; });
            };
        }
        const cou = URL.createObjectURL;
        URL.createObjectURL = function (obj) {
            const r = cou.apply(this, arguments);
            try { const u = obj && blobUrl.get(obj); if (u) objUrl.set(r, u); } catch (e) { }
            return r;
        };
    } catch (e) { }

    // ---------- Web Audio ----------
    try {
        const BAC = window.BaseAudioContext || window.AudioContext || window.webkitAudioContext;
        if (BAC && BAC.prototype.decodeAudioData) {
            const decode = BAC.prototype.decodeAudioData;
            BAC.prototype.decodeAudioData = function (ab, ok, err) {
                let u;
                try { u = abUrl.get(ab); } catch (e) { }
                if (u) stats.decodeMapped++; else stats.decodeUnmapped++;
                const wrapOk = typeof ok === 'function'
                    ? function (buf) { if (u) try { bufUrl.set(buf, u); } catch (e) { } return ok.apply(this, arguments); }
                    : ok;
                const p = decode.call(this, ab, wrapOk, err);
                if (p && typeof p.then === 'function')
                    return p.then((buf) => { if (u) try { bufUrl.set(buf, u); } catch (e) { } return buf; });
                return p;
            };
        }
        if (window.AudioBufferSourceNode) {
            const SP = AudioBufferSourceNode.prototype;
            const start = SP.start;
            SP.start = function () {
                try {
                    const u = this.buffer && bufUrl.get(this.buffer);
                    if (u) {
                        stats.startMapped++;
                        const id = nextId();
                        post({ type: 'play', id: id, url: u, loop: !!this.loop, api: 'webaudio' });
                        this.addEventListener('ended', () => post({ type: 'end', id: id }));
                    } else {
                        stats.startUnmapped++;
                        if (stats.startUnmapped <= 5 && this.buffer)
                            post({ type: 'log', msg: `매핑 안 된 오디오 재생 (길이 ${this.buffer.duration.toFixed(2)}s)` });
                    }
                } catch (e) { }
                return start.apply(this, arguments);
            };
        }
    } catch (e) { }

    // ---------- <audio>/<video> ----------
    try {
        const MP = HTMLMediaElement.prototype;
        const play = MP.play;
        MP.play = function () {
            try {
                let src = this.currentSrc || this.src;
                if (src) {
                    src = objUrl.get(src) || abs(src);
                    if (!src.startsWith('blob:') && !src.startsWith('data:')) {
                        stats.mediaPlay++;
                        const id = nextId();
                        post({ type: 'play', id: id, url: src, loop: !!this.loop, api: 'media' });
                        const end = () => {
                            post({ type: 'end', id: id });
                            this.removeEventListener('pause', end);
                            this.removeEventListener('ended', end);
                            this.removeEventListener('emptied', end);
                        };
                        this.addEventListener('pause', end);
                        this.addEventListener('ended', end);
                        this.addEventListener('emptied', end);
                    }
                }
            } catch (e) { }
            return play.apply(this, arguments);
        };
    } catch (e) { }

    // 진단용: 설치 확인 + 주기적 통계 (변화가 있을 때만)
    post({ type: 'log', msg: 'audio hook installed: ' + location.href });
    let last = '';
    setInterval(() => {
        const s = JSON.stringify(stats);
        if (s !== last) { last = s; post({ type: 'log', msg: 'stats ' + location.host + ' ' + s }); }
    }, 10000);
})();
