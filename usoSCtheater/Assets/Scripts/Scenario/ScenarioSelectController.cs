using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;          //[목록 UI] ScrollRect, LayoutRebuilder
using UsoSCTheater.SceneFlow;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 목록 씬(ScenarioSelectScene) 컨트롤러.
    /// 카탈로그의 시나리오마다 ScenarioListItem을 생성하고, 각 PlayButton 클릭 시 바로 재생한다.
    /// [재생 범위] ActListItem_All = 모두 재생(전체 막 → 엔딩 씬) / ActListItem = 그 막만 → 목록 씬
    /// [목록 UI] 선택 → 시작 버튼 흐름은 폐지 (PlayButton 즉시 재생)
    /// </summary>
    public class ScenarioSelectController : MonoBehaviour
    {
        [Header("목록 UI")]   //[목록 UI]
        [SerializeField] private ScrollRect scenarioScrollRect;        // ScenarioListPanel/Scroll View
        [SerializeField] private ScenarioListItem scenarioItemPrefab;  // ScenarioListItem 프리팹
        [SerializeField] private Sprite defaultThumbnail;              // 썸네일이 없을 때 표시

        [Header("목록 옵션")]
        [SerializeField] private bool showHiddenInEditor = true;  // 에디터에서만 hidden 시나리오도 표시 (빌드에서는 항상 숨김)
        [SerializeField] private bool restoreScrollToLastScenario = true;   //[목록 UI] 목록 복귀 시 직전 재생 시나리오 위치로 스크롤 (온/오프 비교용)

        private readonly List<ScenarioEntry> scenarios = new List<ScenarioEntry>();
        public IReadOnlyList<ScenarioEntry> Scenarios => scenarios;

        private readonly List<ScenarioListItem> items = new List<ScenarioListItem>();   //[목록 UI]

        void Awake()
        {
            LoadCatalog();
        }

        //[목록 UI] UI 생성은 Start (레이아웃/캔버스 준비 이후)
        void Start()
        {
            BuildList();
            if (restoreScrollToLastScenario) RestoreScrollToLastScenario();
        }

        // ── 목록 데이터 ──────────────────────────────────────────────────
        private void LoadCatalog()
        {
            scenarios.Clear();

            var catalog = ScenarioCatalog.Load();
            if (catalog == null) return;

            bool includeHidden = false;
#if UNITY_EDITOR
            includeHidden = showHiddenInEditor;
#endif
            foreach (var entry in catalog.scenarios)
            {
                if (entry == null || string.IsNullOrEmpty(entry.folderName)) continue;
                if (entry.hidden && !includeHidden) continue;
                if (entry.actNames == null || entry.actNames.Count == 0)
                {
                    Debug.LogWarning($"[ScenarioSelect] '{entry.folderName}'에 막 파일이 없어 목록에서 제외합니다.");
                    continue;
                }
                scenarios.Add(entry);
            }
            Debug.Log($"[ScenarioSelect] 시나리오 {scenarios.Count}개 표시");

            //[목록 UI] 직전 선택 복원(SelectScenario) 제거 → 선택 상태가 없어져 스크롤 위치 복원(RestoreScrollToLastScenario)으로 대체
        }

        // ── 목록 UI 생성 ─────────────────────────────────────────────────
        private void BuildList()
        {
            if (scenarioScrollRect == null || scenarioItemPrefab == null)
            {
                Debug.LogError("[ScenarioSelect] Scenario Scroll Rect 또는 Scenario Item Prefab 미연결 — 목록을 만들 수 없습니다.");
                return;
            }

            RectTransform content = scenarioScrollRect.content;

            //견본/이전 항목 제거 (비활성화 후 Destroy → 같은 프레임 레이아웃 계산에서 즉시 제외)
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            items.Clear();

            foreach (var entry in scenarios)
            {
                ScenarioEntry e = entry;   //클로저 캡처용
                ScenarioListItem item = Instantiate(scenarioItemPrefab, content);
                item.name = $"ScenarioListItem_{e.folderName}";
                item.Setup(e, defaultThumbnail, () => PlayAll(e), actIndex => PlayAct(e, actIndex));
                items.Add(item);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        // ── 재생 (PlayButton → 바로 재생) ────────────────────────────────
        public void PlayAll(ScenarioEntry entry)
        {
            Debug.Log($"[ScenarioSelect] 재생 요청: {entry.folderName} / 모두 재생");
            SceneTransitionManager.GoToCommunicationScene(entry.folderName, null);
        }

        public void PlayAct(ScenarioEntry entry, int actIndex)
        {
            if (actIndex < 0 || actIndex >= entry.actNames.Count)
            {
                Debug.LogWarning($"[ScenarioSelect] 잘못된 막 인덱스: {entry.folderName} / {actIndex}");
                return;
            }
            Debug.Log($"[ScenarioSelect] 재생 요청: {entry.folderName} / {entry.actNames[actIndex]}");
            SceneTransitionManager.GoToCommunicationScene(entry.folderName, entry.actNames[actIndex]);
        }

        // ── 목록 복귀 시 스크롤 위치 복원 ────────────────────────────────
        //ScenarioSelection은 목록 복귀 후에도 직전 선택값을 유지 (Play 시작 시에만 초기화) → 처음 실행 시에는 맨 위
        private void RestoreScrollToLastScenario()
        {
            if (!ScenarioSelection.HasSelection || scenarioScrollRect == null) return;

            ScenarioListItem target = items.Find(i => i.FolderName == ScenarioSelection.ScenarioFolder);
            if (target == null) return;

            Canvas.ForceUpdateCanvases();
            RectTransform content = scenarioScrollRect.content;
            RectTransform viewport = scenarioScrollRect.viewport != null ? scenarioScrollRect.viewport : (RectTransform)scenarioScrollRect.transform;

            float scrollable = content.rect.height - viewport.rect.height;
            if (scrollable <= 0f) return;

            //항목 윗변이 Content 윗변에서 떨어진 거리 → 그 위치가 화면 맨 위에 오도록
            Vector3 itemTopWorld = target.Rect.TransformPoint(new Vector3(0f, target.Rect.rect.yMax, 0f));
            float offset = content.rect.yMax - content.InverseTransformPoint(itemTopWorld).y;

            scenarioScrollRect.verticalNormalizedPosition = 1f - Mathf.Clamp01(offset / scrollable);
            Debug.Log($"[ScenarioSelect] 직전 시나리오 위치로 스크롤: {target.FolderName}");
        }
    }
}
