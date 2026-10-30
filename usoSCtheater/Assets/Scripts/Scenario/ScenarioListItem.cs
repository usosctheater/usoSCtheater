using System;
using System.Collections.Generic;   //[태그/등장인물]
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UsoSCTheater.Character;       //[태그/등장인물] CharacterDatabase

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 시나리오 목록의 한 항목 (ScenarioListItem 프리팹 루트).
    /// 썸네일 / 제목 / 태그·등장인물 / 막 목록(모두 재생 + 막별 재생)을 채운다.
    /// 항목 크기는 고정. 막 행 높이는 ActList의 VerticalLayoutGroup + 행 LayoutElement(Min~Preferred)가 자동 조절.
    /// </summary>
    public class ScenarioListItem : MonoBehaviour
    {
        [Header("시나리오 정보")]
        [SerializeField] private Image thumbnail;
        [SerializeField] private TextMeshProUGUI scenarioTitle;
        [SerializeField] private RectTransform tagArea;             // 태그 생성 위치 (HorizontalLayoutGroup)        //[태그/등장인물] 주석 변경
        [SerializeField] private RectTransform characterIconArea;   // 등장인물 아이콘 생성 위치 (HorizontalLayoutGroup) //[태그/등장인물] 주석 변경

        //[태그/등장인물]
        [Header("태그 / 등장인물")]
        [SerializeField] private ScenarioTagItem tagItemPrefab;          // TagItem 프리팹
        [SerializeField] private CharacterIconItem characterIconPrefab;  // CharacterIcon 프리팹
        [SerializeField, Min(0)] private int maxTags = 4;                // 표시 최대 개수 (0 = 제한 없음), 초과분은 잘라내고 경고
        [SerializeField, Min(0)] private int maxCharacters = 5;          // 표시 최대 개수 (0 = 제한 없음), 초과분은 잘라내고 경고

        [Header("막 목록")]
        [SerializeField] private RectTransform actList;             // VerticalLayoutGroup
        [SerializeField] private ActListItem playAllItem;           // ActListItem_All (프리팹 내부 고정, 항상 존재)
        [SerializeField] private ActListItem actItemPrefab;         // ActListItem 프리팹

        public string FolderName { get; private set; }
        public RectTransform Rect => (RectTransform)transform;

        public void Setup(ScenarioEntry entry, Sprite defaultThumbnail, Action onPlayAll, Action<int> onPlayAct)
        {
            FolderName = entry.folderName;

            ApplyThumbnail(entry, defaultThumbnail);
            ApplyTitle(entry);
            ApplyTags(entry);
            ApplyCharacters(entry);

            if (playAllItem != null) playAllItem.Setup(null, onPlayAll);   //모두 재생: 문구는 프리팹 그대로
            else Debug.LogWarning($"[ScenarioListItem] {entry.folderName}: ActListItem_All(playAllItem) 미연결 — 모두 재생 불가");

            BuildActList(entry, onPlayAct);
        }

        // ── 썸네일: Resources/ScenarioThumbnail/{폴더명}, 없으면 기본 썸네일 ──
        private void ApplyThumbnail(ScenarioEntry entry, Sprite defaultThumbnail)
        {
            if (thumbnail == null) return;

            Sprite sprite = LocalizedSprites.Load(ScenarioCatalog.ThumbnailRoot, entry.folderName);   //[다국어] 수정: 언어별 썸네일 (F12 → 목록 다시 그리기 때 반영)
            if (sprite == null)
            {
                Debug.Log($"[ScenarioListItem] {entry.folderName}: 썸네일 없음 → 기본 썸네일 사용 (Resources/{ScenarioCatalog.ThumbnailRoot}/{entry.folderName})");
                sprite = defaultThumbnail;
            }
            thumbnail.sprite = sprite;
        }

        // ── 제목: 막 XML mainTitle(카탈로그), 없으면 표시명 + 경고 ──
        private void ApplyTitle(ScenarioEntry entry)
        {
            if (scenarioTitle == null) return;

            string title = entry.GetScenarioTitle();   //[다국어] (기존 entry.scenarioTitle)
            if (string.IsNullOrEmpty(title))
            {
                Debug.LogWarning($"[ScenarioListItem] {entry.folderName}: 시나리오 타이틀(mainTitle) 없음 → 표시명 '{entry.DisplayNameOrFolder}' 사용");
                title = entry.DisplayNameOrFolder;
            }
#if UNITY_EDITOR
            if (entry.hidden) title += " [hidden]";
#endif
            scenarioTitle.text = title;
        }

        // ── 태그: 카탈로그 tags 순서대로 TagItem 생성, maxTags 초과분은 생략 ──
        private void ApplyTags(ScenarioEntry entry)
        {
            List<string> tags = entry.GetTags() ?? new List<string>();   //[다국어] (기존 entry.tags)
            if (tagArea == null || tagItemPrefab == null)
            {
                if (tags.Count > 0) Debug.LogWarning($"[ScenarioListItem] {entry.folderName}: TagArea 또는 TagItem 프리팹 미연결 — 태그 생략");
                return;
            }

            ClearChildren(tagArea);

            int count = tags.Count;
            if (maxTags > 0 && count > maxTags)
            {
                Debug.LogWarning($"[ScenarioListItem] {entry.folderName}: 태그 {count}개 중 최대 {maxTags}개만 표시 (생략: {string.Join(", ", tags.GetRange(maxTags, count - maxTags))})");
                count = maxTags;
            }

            for (int i = 0; i < count; i++)
            {
                ScenarioTagItem item = Instantiate(tagItemPrefab, tagArea);
                item.name = $"TagItem_{i}";
                item.Setup(tags[i]);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(tagArea);
        }

        // ── 등장인물: 카탈로그 characters 순서대로 CharacterDatabase 아이콘 생성 ──
        // DB에 없거나 아이콘 미지정이면 건너뜀(경고), maxCharacters는 실제 표시되는 아이콘 기준
        private void ApplyCharacters(ScenarioEntry entry)
        {
            List<string> characters = entry.characters ?? new List<string>();
            if (characterIconArea == null || characterIconPrefab == null)
            {
                if (characters.Count > 0) Debug.LogWarning($"[ScenarioListItem] {entry.folderName}: CharacterIconArea 또는 CharacterIcon 프리팹 미연결 — 등장인물 생략");
                return;
            }

            ClearChildren(characterIconArea);
            if (characters.Count == 0) return;

            int shown = 0;
            for (int i = 0; i < characters.Count; i++)
            {
                string id = characters[i];
                if (maxCharacters > 0 && shown >= maxCharacters)
                {
                    Debug.LogWarning($"[ScenarioListItem] {entry.folderName}: 등장인물 최대 {maxCharacters}명만 표시 (생략: {string.Join(", ", characters.GetRange(i, characters.Count - i))})");
                    break;
                }

                //[캐릭터 DB] 변경: SO 조회 → static DB, 아이콘은 경로(Resources/CharacterIcon/{Id})로 불러옴
                CharacterEntry character = CharacterDatabase.Find(id);
                if (character == null)
                {
                    Debug.LogWarning($"[ScenarioListItem] {entry.folderName}: 등장인물 '{id}'가 CharacterData.xml에 없음 → 생략");
                    continue;
                }
                Sprite iconSprite = CharacterDatabase.LoadIcon(character.Id);
                if (iconSprite == null) continue;   //경고는 LoadIcon에서 (아이콘당 1번)

                CharacterIconItem icon = Instantiate(characterIconPrefab, characterIconArea);
                icon.name = $"CharacterIcon_{character.Id}";
                icon.Setup(character.Id, iconSprite);
                shown++;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(characterIconArea);
        }

        //[태그/등장인물] 생성 영역 비우기 (프리팹 안의 견본 포함). 비활성화 후 Destroy → 같은 프레임 레이아웃 계산에서 즉시 제외
        private static void ClearChildren(RectTransform area)
        {
            for (int i = area.childCount - 1; i >= 0; i--)
            {
                GameObject child = area.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        // ── 막 목록: ActListItem_All 다음에 막 수만큼 생성 ──
        private void BuildActList(ScenarioEntry entry, Action<int> onPlayAct)
        {
            if (actList == null || actItemPrefab == null)
            {
                Debug.LogWarning($"[ScenarioListItem] {entry.folderName}: ActList 또는 ActListItem 프리팹 미연결 — 막 목록 생성 생략");
                return;
            }

            //이전 생성 행 제거 (ActListItem_All 제외). 비활성화 후 Destroy → 같은 프레임 레이아웃 계산에서 즉시 제외
            for (int i = actList.childCount - 1; i >= 0; i--)
            {
                Transform child = actList.GetChild(i);
                if (playAllItem != null && child == playAllItem.transform) continue;
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }

            for (int i = 0; i < entry.actNames.Count; i++)
            {
                string title = entry.GetActTitle(i);
                if (title == null)
                {
                    Debug.LogWarning($"[ScenarioListItem] {entry.folderName}/{entry.actNames[i]}: 막 타이틀(subTitle) 없음 → 파일명 사용");
                    title = entry.actNames[i];
                }

                int actIndex = i;   //클로저 캡처용
                ActListItem row = Instantiate(actItemPrefab, actList);
                row.name = $"ActListItem_{entry.actNames[i]}";
                row.Setup(title, () => onPlayAct?.Invoke(actIndex));
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(actList);
            WarnIfOverflow(entry, entry.actNames.Count + 1);   //+1 = 모두 재생
        }

        //행이 Min Height까지 줄어도 ActList 높이를 넘으면 경고
        private void WarnIfOverflow(ScenarioEntry entry, int rows)
        {
            var layout = actList.GetComponent<VerticalLayoutGroup>();
            var element = actItemPrefab.GetComponent<LayoutElement>();
            if (layout == null || element == null) return;

            float need = rows * element.minHeight + layout.spacing * (rows - 1) + layout.padding.vertical;
            if (need > actList.rect.height)
                Debug.LogWarning($"[ScenarioListItem] {entry.folderName}: 막 {rows - 1}개가 ActList 높이({actList.rect.height})를 넘칩니다 (최소 필요 {need}). Min Height 또는 ActList 높이 조정 필요");
        }
    }
}
