using System;
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
    /// [목록 UI] 선택 → 시작 버튼 흐름은 폐지. 기존 선택 API와 임시 디버그 GUI는 파일 하단에 주석 처리 (UI 구현 완료 후 삭제)
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

        /* [임시 GUI] UI 구현 완료 후 삭제 ─────────────────────────────────────────
           선택 → 시작 버튼 흐름 + OnGUI 디버그 화면. 정식 목록 UI(PlayButton 즉시 재생)로 대체됨.

        [Header("임시 디버그 GUI")]
        [SerializeField] private bool showDebugGUI = true;        // 정식 GUI 연결 후 끄기

        public event Action OnSelectionChanged;                   // 정식 GUI 갱신용

        public int SelectedScenarioIndex { get; private set; } = -1;
        public int SelectedActIndex { get; private set; } = -1;   // -1 = 모두 재생

        public ScenarioEntry SelectedScenario =>
            (SelectedScenarioIndex >= 0 && SelectedScenarioIndex < scenarios.Count) ? scenarios[SelectedScenarioIndex] : null;

        //LoadCatalog 끝에 있던 직전 선택 복원
        //int prev = ScenarioSelection.HasSelection
        //    ? scenarios.FindIndex(s => s.folderName == ScenarioSelection.ScenarioFolder)
        //    : -1;
        //SelectScenario(prev >= 0 ? prev : (scenarios.Count > 0 ? 0 : -1));

        //→ ScenarioEntry.DisplayNameOrFolder로 대체
        public static string GetDisplayName(ScenarioEntry entry) =>
            string.IsNullOrEmpty(entry.displayName) ? entry.folderName : entry.displayName;

        public void SelectScenario(int index)
        {
            bool valid = index >= 0 && index < scenarios.Count;
            SelectedScenarioIndex = valid ? index : -1;
            SelectedActIndex = -1;   //시나리오를 바꾸면 재생 범위는 모두 재생으로 초기화
            OnSelectionChanged?.Invoke();
        }

        public void SelectScenarioByFolder(string folderName)
        {
            int index = scenarios.FindIndex(s => s.folderName == folderName);
            if (index < 0) Debug.LogWarning($"[ScenarioSelect] 목록에 없는 시나리오: {folderName}");
            SelectScenario(index);
        }

        //-1 = 모두 재생, 0 이상 = 해당 막만 단일 재생
        public void SelectAct(int actIndex)
        {
            var scenario = SelectedScenario;
            if (scenario == null) return;

            SelectedActIndex = (actIndex >= 0 && actIndex < scenario.actNames.Count) ? actIndex : -1;
            OnSelectionChanged?.Invoke();
        }

        public void StartPlayAll()
        {
            SelectAct(-1);
            StartSelected();
        }

        public void StartSelected()
        {
            var scenario = SelectedScenario;
            if (scenario == null)
            {
                Debug.LogWarning("[ScenarioSelect] 선택된 시나리오가 없습니다.");
                return;
            }

            string startAct = SelectedActIndex >= 0 ? scenario.actNames[SelectedActIndex] : null;
            Debug.Log($"[ScenarioSelect] 시작 요청: {scenario.folderName} / {startAct ?? "모두 재생"}");
            SceneTransitionManager.GoToCommunicationScene(scenario.folderName, startAct);
        }

        private Vector2 debugScroll;

        void OnGUI()
        {
            if (!showDebugGUI) return;

            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var label = new GUIStyle(GUI.skin.label) { fontSize = 28 };
            var button = new GUIStyle(GUI.skin.button) { fontSize = 26, alignment = TextAnchor.MiddleLeft };
            var height = GUILayout.Height(56);

            GUILayout.BeginArea(new Rect(40, 40, 900, 1000));
            debugScroll = GUILayout.BeginScrollView(debugScroll);

            GUILayout.Label("[임시] 시나리오 선택", label);
            if (scenarios.Count == 0) GUILayout.Label("표시할 시나리오가 없습니다 (카탈로그 확인)", label);

            for (int i = 0; i < scenarios.Count; i++)
            {
                var s = scenarios[i];
                string mark = i == SelectedScenarioIndex ? "▶ " : "   ";
                string hiddenTag = s.hidden ? " [hidden]" : "";
                if (GUILayout.Button($"{mark}{GetDisplayName(s)}  ({s.actNames.Count}막){hiddenTag}", button, height))
                    SelectScenario(i);
            }

            var selected = SelectedScenario;
            if (selected != null)
            {
                GUILayout.Space(24);
                GUILayout.Label("재생 범위 (모두 재생 = 엔딩까지 / 막 선택 = 그 막만)", label);

                if (GUILayout.Button($"{(SelectedActIndex < 0 ? "▶ " : "   ")}모두 재생", button, height))
                    SelectAct(-1);

                for (int j = 0; j < selected.actNames.Count; j++)
                {
                    string mark = j == SelectedActIndex ? "▶ " : "   ";
                    if (GUILayout.Button($"{mark}{selected.actNames[j]}", button, height))
                        SelectAct(j);
                }

                GUILayout.Space(24);
                if (GUILayout.Button("시작", button, GUILayout.Height(72)))
                    StartSelected();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        ─────────────────────────────────────────────────────────────────────────── */
    }
}
