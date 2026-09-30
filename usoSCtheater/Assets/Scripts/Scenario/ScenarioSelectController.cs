using System;
using System.Collections.Generic;
using UnityEngine;
using UsoSCTheater.SceneFlow;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 목록 씬(ScenarioSelectScene) 컨트롤러.
    /// 카탈로그에서 시나리오 목록을 읽고, 시나리오/재생 범위 선택 → SceneTransitionManager로 CommunicationScene 이동.
    /// [재생 범위] 모두 재생(SelectedActIndex = -1): 전체 막 → 엔딩 씬 / 특정 막 선택: 그 막만 → 목록 씬
    /// 정식 GUI는 public 함수(SelectScenario / SelectAct / StartSelected / StartPlayAll)와
    /// OnSelectionChanged 이벤트에 연결한다. 정식 GUI 전까지는 임시 디버그 GUI(OnGUI)로 테스트.
    /// </summary>
    public class ScenarioSelectController : MonoBehaviour
    {
        [Header("목록 옵션")]
        [SerializeField] private bool showHiddenInEditor = true;  // 에디터에서만 hidden 시나리오도 표시 (빌드에서는 항상 숨김)

        [Header("임시 디버그 GUI")]
        [SerializeField] private bool showDebugGUI = true;        // 정식 GUI 연결 후 끄기

        public event Action OnSelectionChanged;                   // 정식 GUI 갱신용

        private readonly List<ScenarioEntry> scenarios = new List<ScenarioEntry>();
        public IReadOnlyList<ScenarioEntry> Scenarios => scenarios;

        public int SelectedScenarioIndex { get; private set; } = -1;
        public int SelectedActIndex { get; private set; } = -1;   // -1 = 모두 재생

        public ScenarioEntry SelectedScenario =>
            (SelectedScenarioIndex >= 0 && SelectedScenarioIndex < scenarios.Count) ? scenarios[SelectedScenarioIndex] : null;

        void Awake()
        {
            LoadCatalog();
        }

        // ── 목록 ─────────────────────────────────────────────────────────
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

            //직전 선택 복원 (엔딩 후 복귀 등), 없으면 첫 시나리오
            int prev = ScenarioSelection.HasSelection
                ? scenarios.FindIndex(s => s.folderName == ScenarioSelection.ScenarioFolder)
                : -1;
            SelectScenario(prev >= 0 ? prev : (scenarios.Count > 0 ? 0 : -1));
        }

        public static string GetDisplayName(ScenarioEntry entry) =>
            string.IsNullOrEmpty(entry.displayName) ? entry.folderName : entry.displayName;

        // ── 선택 (GUI 연결용) ────────────────────────────────────────────
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

        // ── 시작 (GUI 연결용) ────────────────────────────────────────────
        //[재생 범위] StartFromBeginning → StartPlayAll (모두 재생 → 엔딩까지)
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

            //선택값 저장 + 씬 이름/Build Settings 검사 + timeScale 복구 + 연타 방지는 매니저가 처리
            SceneTransitionManager.GoToCommunicationScene(scenario.folderName, startAct);
        }

        // ── 임시 디버그 GUI (정식 GUI 전 전환 테스트용) ──────────────────
        private Vector2 debugScroll;

        void OnGUI()
        {
            if (!showDebugGUI) return;

            //1080p 기준 크기로 스케일
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
    }
}
