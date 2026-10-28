using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UsoSCTheater.Character;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// Iris 트랜지션 가운데 아이콘 후보 (2026-10-27).
    /// 시나리오 시작 때 1번 만들고(Build), 트랜지션마다 후보 중 랜덤 1개(TryPick). 후보 Sprite는 미리 읽어 두므로 Pick은 Random.Range뿐
    /// 후보 규칙 (위에서부터 처음으로 후보가 생기는 규칙 사용):
    ///   0. ScenarioData.xml Iris 직접 지정 (유닛 ID 또는 캐릭터 ID)
    ///   1. 멤버 전원이 등장인물에 들어 있는 유닛 (게스트가 있어도 됨, 2개 이상이면 그중 랜덤)
    ///   2. 등장인물 전원 (유닛 없는 게스트도 이때만 포함)
    ///   후보가 없으면 EffectManager 인스펙터 기본 아이콘
    /// 아이콘: Resources/IrisIcon/{유닛 ID 또는 캐릭터 ID} (파일 없는 후보는 제외 + 경고)
    /// </summary>
    public class IrisIconSet
    {
        public const string IconRoot = "IrisIcon";

        private readonly List<string> keys = new List<string>();
        private readonly List<Sprite> sprites = new List<Sprite>();
        private readonly List<bool> isUnit = new List<bool>();   //[Iris 아이콘] 후보별 종류 (유닛 = UnitData에 있는 Id → 배경 없이 표시)

        public int Count => sprites.Count;
        public string Rule { get; private set; } = "후보 없음";   // 로그용: 어느 규칙으로 정했는지
        public IReadOnlyList<string> Keys => keys;

        public static IrisIconSet Build(ScenarioEntry entry)
        {
            var set = new IrisIconSet();
            if (entry == null)
            {
                set.Rule = "시나리오 정보 없음 → 기본 아이콘";
                return set;
            }

            //0. 직접 지정
            if (entry.irisIcons != null && entry.irisIcons.Count > 0)
            {
                set.AddAll(entry.irisIcons);
                if (set.Count > 0) { set.Rule = "직접 지정(Iris)"; return set; }
                Debug.LogWarning($"[IrisIconSet] {entry.folderName}: Iris 직접 지정 아이콘을 하나도 찾지 못함 → 자동 판정");
            }

            List<string> characters = entry.characters ?? new List<string>();

            //1. 전원 등장한 유닛
            List<string> fullUnits = CharacterDatabase.GetUnits().Where(u => u.IsFullyIn(characters)).Select(u => u.Id).ToList();
            if (fullUnits.Count > 0)
            {
                set.AddAll(fullUnits);
                if (set.Count > 0) { set.Rule = $"유닛 전원 등장 ({string.Join(", ", fullUnits)})"; return set; }
            }

            //2. 등장인물 전원
            set.AddAll(characters);
            set.Rule = set.Count > 0 ? "등장인물 랜덤" : "후보 없음 → 기본 아이콘";
            return set;
        }

        //트랜지션마다 호출. 후보가 없으면 false (호출 쪽은 기본 아이콘 유지)
        //unit = true면 유닛 아이콘 (배경 원 없이 1배), false면 멤버 사인 (배경 원 위에 여백 배율)
        public bool TryPick(out Sprite sprite, out string key, out bool unit)   //[Iris 아이콘] out bool unit 추가
        {
            sprite = null;
            key = null;
            unit = false;
            if (sprites.Count == 0) return false;

            int index = UnityEngine.Random.Range(0, sprites.Count);
            sprite = sprites[index];
            key = keys[index];
            unit = isUnit[index];
            return true;
        }

        private void AddAll(IEnumerable<string> ids)
        {
            foreach (string raw in ids)
            {
                string id = raw?.Trim();
                if (string.IsNullOrEmpty(id) || keys.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;

                Sprite sprite = Resources.Load<Sprite>($"{IconRoot}/{id}");
                if (sprite == null)
                {
                    Debug.LogWarning($"[IrisIconSet] 아이콘 없음 → 후보에서 제외: Resources/{IconRoot}/{id}");
                    continue;
                }
                keys.Add(id);
                sprites.Add(sprite);
                isUnit.Add(CharacterDatabase.FindUnit(id) != null);   //[Iris 아이콘] 직접 지정에 유닛·멤버가 섞여도 각각 판정
            }
        }
    }
}
