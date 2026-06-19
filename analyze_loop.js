const fs = require('fs');

const files = [
    'D:/usosctheater/usoSCtheater/Assets/Resources/spine/asahi/asahi_idol_3.json',
    'D:/usosctheater/usoSCtheater/Assets/Resources/spine/fuyuko/fuyuko.json',
    'D:/usosctheater/usoSCtheater/Assets/Resources/spine/fuyuko/fuyuko_idol_3.json',
    'D:/usosctheater/usoSCtheater/Assets/Resources/spine/mei/mei_idol_3.json',
];

// 트랙 수식어 접두사 (0번 트랙이 아닌 것들)
const excludePrefixes = ['lip_', 'face_', 'arm_', 'eye_', 'brow_', 'body_', 'head_', 'hand_', 'leg_'];

function hasLoopStart(anim) {
    if (!anim.events) return false;
    return anim.events.some(e => e.name === 'loop_start');
}

function isTrack0Anim(name) {
    return !excludePrefixes.some(p => name.startsWith(p));
}

files.forEach(path => {
    const fileName = path.split('/').pop();
    const d = JSON.parse(fs.readFileSync(path, 'utf8'));
    const anims = d.animations;

    const track0 = Object.keys(anims).filter(isTrack0Anim);
    const withLoop = track0.filter(n => hasLoopStart(anims[n]));
    const withoutLoop = track0.filter(n => !hasLoopStart(anims[n]));

    console.log(`\n=== ${fileName} ===`);
    console.log(`0번 트랙 애니메이션 총계: ${track0.length}개`);
    console.log(`loop_start 있음: ${withLoop.length}개 (${(withLoop.length/track0.length*100).toFixed(1)}%)`);
    console.log(`loop_start 없음: ${withoutLoop.length}개 (${(withoutLoop.length/track0.length*100).toFixed(1)}%)`);
    console.log(`  └ 목록: ${withoutLoop.join(', ')}`);
});
