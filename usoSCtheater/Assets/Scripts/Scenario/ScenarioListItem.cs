using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 시나리오 목록의 한 항목 (ScenarioListItem 프리팹 루트).
    /// 썸네일 / 제목 / (추후) 태그·등장인물 / 막 목록(모두 재생 + 막별 재생)을 채운다.
    /// 항목 크기는 고정. 막 행 높이는 ActList의 VerticalLayoutGroup + 행 LayoutElement(Min~Preferred)가 자동 조절.
    /// </summary>
    public class ScenarioListItem : MonoBehaviour
    {
        [Header("시나리오 정보")]
        [SerializeField] private Image thumbnail;
        [SerializeField] private TextMeshProUGUI scenarioTitle;
        [SerializeField] private RectTransform tagArea;             // 추후 태그 프리팹 생성 위치
        [SerializeField] private RectTransform characterIconArea;   // 추후 캐릭터 아이콘 프리팹 생성 위치

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

            Sprite sprite = Resources.Load<Sprite>($"{ScenarioCatalog.ThumbnailRoot}/{entry.folderName}");
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

            string title = entry.scenarioTitle;
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

        // ── 태그 (추후 구현) ──
        // 시나리오 데이터 파일에서 태그 목록을 읽어 tagArea에 태그 프리팹을 생성한다.
        private void ApplyTags(ScenarioEntry entry) { }

        // ── 등장인물 (추후 구현) ──
        // 시나리오 데이터 파일에서 등장인물 목록을 읽어 characterIconArea에 캐릭터 아이콘 프리팹을 생성한다.
        private void ApplyCharacters(ScenarioEntry entry) { }

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
