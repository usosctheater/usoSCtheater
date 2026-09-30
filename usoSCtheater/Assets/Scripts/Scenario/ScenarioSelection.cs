using UnityEngine;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 목록 씬(ScenarioSelectScene) → CommunicationScene으로 선택값을 넘기는 정적 저장소.
    /// 이 프로젝트는 Enter Play Mode Options로 Domain Reload가 꺼져 있어 static 값이 Play 세션 간 유지되므로
    /// Play 시작마다 SubsystemRegistration 시점에 초기화한다.
    /// [용어 정리] StartSceneName → StartActName, GetScenePath → GetScenarioPath
    /// </summary>
    public static class ScenarioSelection
    {
        public static string ScenarioFolder { get; private set; }   // 예: "IL"
        public static string StartActName { get; private set; }     // 예: "IL03" (null이면 모두 재생)

        public static bool HasSelection => !string.IsNullOrEmpty(ScenarioFolder);

        //[재생 범위] 모두 재생(첫 막 → 마지막 막 → 엔딩 씬) 여부. false면 단일 막 재생(그 막만 → 목록 씬)
        //선택값이 없을 때(CommunicationScene 직접 Play)도 모두 재생으로 취급
        public static bool IsPlayAll => string.IsNullOrEmpty(StartActName);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Clear();

        public static void Select(string scenarioFolder, string startActName = null)
        {
            ScenarioFolder = scenarioFolder;
            StartActName = string.IsNullOrEmpty(startActName) ? null : startActName;
        }

        public static void Clear()
        {
            ScenarioFolder = null;
            StartActName = null;
        }

        //"IL" → "Scenario/IL" (시나리오 루트 + 폴더명)
        public static string GetScenarioPath(string scenarioFolder) => $"{ScenarioCatalog.ScenarioRoot}/{scenarioFolder}";
    }
}
