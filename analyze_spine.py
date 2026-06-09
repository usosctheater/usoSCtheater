import json

files = [
    r'D:\usosctheater\usoSCtheater\Assets\Resources\spine\fuyuko.json',
    r'D:\usosctheater\usoSCtheater\Assets\Resources\spine\asahi\asahi_idol_2.json',
]

for path in files:
    print(f"\n=== {path.split(chr(92))[-1]} ===")
    with open(path, encoding='utf-8') as f:
        data = json.load(f)
    print("Top-level keys:", list(data.keys()))
    
    # animations 구조 확인
    if 'animations' in data:
        anims = data['animations']
        print(f"Animation count: {len(anims)}")
        print("Animation names:", list(anims.keys())[:10])
        
        # 첫 번째 애니메이션 상세 구조
        first_name = list(anims.keys())[0]
        first = anims[first_name]
        print(f"\nFirst animation '{first_name}' keys:", list(first.keys()) if isinstance(first, dict) else type(first))
        
        # duration 정보가 있는지 확인
        for anim_name, anim_data in list(anims.items())[:3]:
            if isinstance(anim_data, dict):
                # 타임라인 최대값으로 duration 추정
                max_time = 0
                for track_key, track_val in anim_data.items():
                    if isinstance(track_val, list):
                        for item in track_val:
                            if isinstance(item, dict) and 'time' in item:
                                max_time = max(max_time, item['time'])
                    elif isinstance(track_val, dict):
                        for sub_key, sub_val in track_val.items():
                            if isinstance(sub_val, list):
                                for item in sub_val:
                                    if isinstance(item, dict) and 'time' in item:
                                        max_time = max(max_time, item['time'])
                print(f"  '{anim_name}' estimated duration: {max_time:.3f}s")
