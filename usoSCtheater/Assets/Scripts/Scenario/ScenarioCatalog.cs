using System;
using System.Collections.Generic;
using UnityEngine;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 시나리오 1개 정보.
    /// folderName / actNames는 에디터 동기화(ScenarioCatalogSync)가 자동 갱신,
    /// displayName / hidden / 목록 순서는 인스펙터에서 수동 편집(동기화 시 유지).
    /// </summary>
    [Serializable]
    public class ScenarioEntry
    {
        public string folderName;                               // 시나리오 루트(ScenarioCatalog.ScenarioRoot) 하위 폴더명 (자동)
        public string displayName;                              // 목록 표시명 (수동, 기본값 = folderName)
        public bool hidden;                                     // true면 목록에서 숨김 (수동, 테스트 시나리오용)
        [UnityEngine.Serialization.FormerlySerializedAs("sceneNames")]   //[용어 정리] sceneNames → actNames
        public List<string> actNames = new List<string>();      // 막 파일명, Ordinal 정렬 = ScenarioPlayer 재생 순서 (자동)

        //[목록 UI] 카탈로그 동기화 시 막 XML 헤더에서 자동 저장 (비어 있으면 목록에서 대체 표시 + 경고 로그)
        public string scenarioTitle;                             // 막 순서상 처음으로 값이 있는 mainTitle (자동)
        public List<string> actTitles = new List<string>();      // actNames와 같은 순서의 subTitle (자동)

        //[시나리오 데이터] Resources/Data/ScenarioData.xml에서 동기화 때 자동 저장 (인스펙터에서 고쳐도 동기화 시 덮어씀)
        public List<string> tags = new List<string>();           // 태그 문구, 표시 순서 (자동)
        public List<string> characters = new List<string>();     // 캐릭터 ID(소문자), 표시 순서 (자동)

        //[목록 UI] 표시명 (비어 있으면 폴더명)
        public string DisplayNameOrFolder => string.IsNullOrEmpty(displayName) ? folderName : displayName;

        //[목록 UI] 막 제목 (없으면 null)
        public string GetActTitle(int index) =>
            (actTitles != null && index >= 0 && index < actTitles.Count && !string.IsNullOrEmpty(actTitles[index])) ? actTitles[index] : null;
    }

    /// <summary>
    /// 시나리오 목록. Resources/Data/ScenarioCatalog.asset 1개만 사용한다.
    /// 빌드(exe)에서는 빌드 시점의 스냅샷을 읽기만 한다 (런타임 갱신 없음).
    /// </summary>
    public class ScenarioCatalog : ScriptableObject
    {
        public const string ResourcePath = "Data/ScenarioCatalog";   // Resources.Load 경로
        public const string ScenarioRoot = "Scenario";               // 시나리오 루트 폴더 (Resources/Scenario) //[용어 정리] SceneRoot → ScenarioRoot, 경로 "Scene" → "Scenario"
        public const string ThumbnailRoot = "ScenarioThumbnail";     //[목록 UI] 썸네일 폴더 (Resources/ScenarioThumbnail/{폴더명}, Sprite)

        public List<ScenarioEntry> scenarios = new List<ScenarioEntry>();

        public static ScenarioCatalog Load()
        {
            var catalog = Resources.Load<ScenarioCatalog>(ResourcePath);
            if (catalog == null) Debug.LogError($"[ScenarioCatalog] Resources/{ResourcePath} 없음 — Tools > Scenario > Sync Catalog 실행 필요");
            return catalog;
        }

        //[시나리오 데이터] 시나리오 ID(폴더명)는 대소문자 구분 없이 비교
        public ScenarioEntry Find(string folderName) =>
            scenarios.Find(s => string.Equals(s.folderName, folderName, StringComparison.OrdinalIgnoreCase));

        //목록 씬 표시용 (hidden 제외, 인스펙터 순서 유지)
        public List<ScenarioEntry> GetVisible() => scenarios.FindAll(s => !s.hidden);
    }
}
