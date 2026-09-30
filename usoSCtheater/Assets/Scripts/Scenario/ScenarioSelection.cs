using UnityEngine;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 목록 씬 → main 씬으로 선택값을 넘기는 정적 저장소.
    /// 이 프로젝트는 Enter Play Mode Options로 Domain Reload가 꺼져 있어 static 값이 Play 세션 간 유지되므로
    /// Play 시작마다 SubsystemRegistration 시점에 초기화한다.
    /// </summary>
    public static class ScenarioSelection
    {
        public static string ScenarioFolder { get; private set; }   // 예: "IL"
        public static string StartSceneName { get; private set; }   // 예: "IL03" (null이면 첫 씬)

        public static bool HasSelection => !string.IsNullOrEmpty(ScenarioFolder);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Clear();

        public static void Select(string scenarioFolder, string startSceneName = null)
        {
            ScenarioFolder = scenarioFolder;
            StartSceneName = string.IsNullOrEmpty(startSceneName) ? null : startSceneName;
        }

        public static void Clear()
        {
            ScenarioFolder = null;
            StartSceneName = null;
        }

        //"IL" → "Scene/IL"
        public static string GetScenePath(string scenarioFolder) => $"{ScenarioCatalog.SceneRoot}/{scenarioFolder}";
    }
}
