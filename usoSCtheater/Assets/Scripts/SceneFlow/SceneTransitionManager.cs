using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UsoSCTheater.Scenario;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace UsoSCTheater.SceneFlow
{
    /// <summary>게임에서 전환 대상이 되는 Unity 씬. (SpineDebugScene 같은 개발용 씬은 제외)</summary>
    public enum SceneId
    {
        ScenarioSelect,
        Communication,
        Ending,
    }

    /// <summary>
    /// 모든 Unity 씬 전환의 단일 창구.
    /// 규칙: UnityEngine.SceneManagement.SceneManager.LoadScene은 이 클래스 밖에서 호출하지 않는다.
    /// 씬 추가 절차: SceneId 항목 추가 → GetSceneName에 이름 등록 → Build Settings 등록 → (필요 시) GoToXxxScene 함수 추가
    /// static 클래스라 어느 씬에서 Play해도 동작한다. Domain Reload 비활성 대응으로 Play 시작마다 상태 초기화.
    /// </summary>
    public static class SceneTransitionManager
    {
        //Unity 씬 이름은 여기서만 관리 (Build Settings / 씬 파일명과 일치해야 함)
        private static string GetSceneName(SceneId id) => id switch
        {
            SceneId.ScenarioSelect => "ScenarioSelectScene",
            SceneId.Communication  => "CommunicationScene",
            SceneId.Ending         => "EndingScene",
            _ => null,
        };

        public static bool IsLoading { get; private set; }

        //전환 직전 공통 처리 훅 (예: 녹화 종료). 인자 = 이동할 씬
        public static event Action<SceneId> OnBeforeLoad;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            IsLoading = false;
            OnBeforeLoad = null;
            UnitySceneManager.sceneLoaded -= HandleSceneLoaded;   //중복 구독 방지
            UnitySceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => IsLoading = false;

        // ── 전환 함수 (호출하는 쪽은 이것만 사용) ─────────────────────────
        public static void GoToScenarioSelectScene() => Load(SceneId.ScenarioSelect);

        //시나리오 선택값 저장 후 커뮤니케이션 씬으로. startActName null = 처음부터
        public static void GoToCommunicationScene(string scenarioFolder, string startActName = null)
        {
            if (string.IsNullOrEmpty(scenarioFolder))
            {
                Debug.LogError("[SceneTransitionManager] 시나리오 폴더가 비어 있어 전환을 취소합니다.");
                return;
            }
            if (!CanLoad(SceneId.Communication, out _)) return;   //실패 시 선택값을 바꾸지 않도록 먼저 검사

            ScenarioSelection.Select(scenarioFolder, startActName);
            Load(SceneId.Communication);
        }

        public static void GoToEndingScene() => Load(SceneId.Ending);

        // ── 공통 처리 ────────────────────────────────────────────────────
        public static bool Load(SceneId id)
        {
            if (!CanLoad(id, out string sceneName)) return false;

            IsLoading = true;
            Time.timeScale = 1f;   //백로그(빌드) 일시정지 상태로 넘어가도 다음 씬이 멈추지 않도록
            OnBeforeLoad?.Invoke(id);

            Debug.Log($"[SceneTransitionManager] {UnitySceneManager.GetActiveScene().name} → {sceneName}");
            UnitySceneManager.LoadScene(sceneName);
            return true;
        }

        private static bool CanLoad(SceneId id, out string sceneName)
        {
            sceneName = GetSceneName(id);

            if (IsLoading)
            {
                Debug.LogWarning($"[SceneTransitionManager] 전환 중이라 {id} 요청을 무시합니다.");
                return false;
            }
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError($"[SceneTransitionManager] {id}의 씬 이름이 등록되지 않았습니다.");
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneTransitionManager] '{sceneName}' 씬이 Build Settings에 없습니다.");
                return false;
            }
            return true;
        }
    }
}
