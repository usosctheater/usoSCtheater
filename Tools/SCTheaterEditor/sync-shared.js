// 추가(공유 라이브러리): 사본 동기화 — 원본(게임 패키지 Runtime) → TE src/Shared, 한 방향. 공유 코드를 고친 같은 단계에서 실행 (핸드오프)
// 사용: node sync-shared.js          → 복사 (.cs만, 하위 폴더 유지, 원본에 없는 사본 .cs는 삭제, csproj·bin·obj는 건드리지 않음)
//       node sync-shared.js --check  → 비교만 (같으면 0, 다르면 1 — 빌드 경고용). 원본이 없으면 건너뜀(0)
'use strict';
const fs = require('fs'), path = require('path');
const teRoot = __dirname;
const src = path.resolve(teRoot, '../../usoSCtheater/Packages/com.usosctheater.shared/Runtime');
const dst = path.join(teRoot, 'src', 'Shared');
const check = process.argv.includes('--check');

function list(dir, base = dir) {   // .cs 상대 경로 (/ 구분), bin·obj 제외
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) return (e.name === 'bin' || e.name === 'obj') ? [] : list(p, base);
    return e.name.endsWith('.cs') ? [path.relative(base, p).replace(/\\/g, '/')] : [];
  });
}
const read = p => fs.readFileSync(p, 'utf8').replace(/^﻿/, '').replace(/\r\n/g, '\n');   // 줄바꿈·BOM 차이는 같은 것으로

if (!fs.existsSync(src)) { console.log(`[sync-shared] 원본 패키지 없음 — 건너뜀 (${src})`); process.exit(0); }
const a = list(src), b = list(dst);
const added = a.filter(f => !b.includes(f));
const removed = b.filter(f => !a.includes(f));
const changed = a.filter(f => b.includes(f) && read(path.join(src, f)) !== read(path.join(dst, f)));
const diff = [...added.map(f => '추가 ' + f), ...changed.map(f => '변경 ' + f), ...removed.map(f => '삭제 ' + f)];

if (check) {
  console.log(diff.length ? '[sync-shared] 사본이 원본과 다름: ' + diff.join(', ') : '[sync-shared] 사본이 원본과 같음');
  process.exit(diff.length ? 1 : 0);
}
for (const f of [...added, ...changed]) {
  const t = path.join(dst, f);
  fs.mkdirSync(path.dirname(t), { recursive: true });
  fs.copyFileSync(path.join(src, f), t);
}
for (const f of removed) fs.unlinkSync(path.join(dst, f));
console.log(diff.length ? '[sync-shared]\n  ' + diff.join('\n  ') : '[sync-shared] 변경 없음');
