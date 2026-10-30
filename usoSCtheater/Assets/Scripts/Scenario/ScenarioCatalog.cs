using System;
using System.Collections.Generic;
using System.Linq;   //[다국어] SequenceEqual
using UnityEngine;

namespace UsoSCTheater.Scenario
{
    /// <summary>[다국어] 기본 언어가 아닌 언어의 제목·태그 1벌 (카탈로그 동기화가 자동 저장 — 실행 중에는 고르기만)</summary>
    [Serializable]
    public class ScenarioTranslation
    {
        public string language;                                  // 언어 코드 (ja …)
        public string scenarioTitle = "";                        // mainTitle_{언어} — 처음으로 값이 있는 막
        public List<string> actTitles = new List<string>();      // subTitle_{언어} — actNames 순서 (없으면 "")
        public List<string> tags = new List<string>();           // ScenarioData.xml Tag_{언어}

        public bool SameAs(ScenarioTranslation other) =>
            other != null && language == other.language && scenarioTitle == other.scenarioTitle
            && actTitles.SequenceEqual(other.actTitles) && tags.SequenceEqual(other.tags);
    }

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
        public List<string> irisIcons = new List<string>();      //[Iris 아이콘] ScenarioData.xml Iris 속성 = 유닛 ID 또는 캐릭터 ID. 비어 있으면 자동 판정 (자동)

        //[엔딩 SD] 막 XML에서 사용한 CG 키 (막 순서·등장 순서, 중복 없음, none 제외) — 동기화 때 자동
        public List<string> spineKeys = new List<string>();

        //[다국어] 기본 언어가 아닌 언어별 제목·태그 (자동). 위의 scenarioTitle·actTitles·tags = 기본 언어(한국어)
        public List<ScenarioTranslation> translations = new List<ScenarioTranslation>();

        //[목록 UI] 표시명 (비어 있으면 폴더명)
        public string DisplayNameOrFolder => string.IsNullOrEmpty(displayName) ? folderName : displayName;

        //[다국어] 현재 언어의 번역 (기본 언어이거나 없으면 null)
        private ScenarioTranslation CurrentTranslation =>
            Localization.Current.IsDefault ? null : translations?.Find(t => t.language == Localization.Current.Code);

        //[다국어] 표시용 시나리오 제목: 현재 언어 → 비어 있으면 기본 언어
        public string GetScenarioTitle()
        {
            string t = CurrentTranslation?.scenarioTitle;
            return !string.IsNullOrEmpty(t) ? t : scenarioTitle;
        }

        //[다국어] 표시용 태그: 현재 언어 태그가 있으면 그것, 없으면 기본 언어
        public List<string> GetTags()
        {
            var t = CurrentTranslation;
            return t != null && t.tags.Count > 0 ? t.tags : tags;
        }

        //[목록 UI] 막 제목 (없으면 null) //[다국어] 현재 언어 → 기본 언어
        public string GetActTitle(int index)
        {
            var t = CurrentTranslation;
            if (t != null && index >= 0 && index < t.actTitles.Count && !string.IsNullOrEmpty(t.actTitles[index])) return t.actTitles[index];
            return (actTitles != null && index >= 0 && index < actTitles.Count && !string.IsNullOrEmpty(actTitles[index])) ? actTitles[index] : null;
        }
    }

    /// <summary>
    /// 시나리오 목록. Resources/Data/ScenarioCatalog.asset 1개만 사용한다.
    /// 빌드(exe)에서는 빌드 시점의 스냅샷을 읽기만 한다 (런타임 갱신 없음).
    /// </summary>
    public class ScenarioCatalog : ScriptableObject
    {
        public const string ResourcePath = ResourcePaths.Data + "/ScenarioCatalog";   // Resources.Load 경로 //[리소스 정리] 폴더는 ResourcePaths
        public const string ScenarioRoot = ResourcePaths.Scenario;                    // 시나리오 루트 폴더 (Resources/Scenario) //[용어 정리] SceneRoot → ScenarioRoot, 경로 "Scene" → "Scenario"
        public const string ThumbnailRoot = ResourcePaths.ScenarioThumbnail;     //[목록 UI] 썸네일 폴더 (Resources/ScenarioThumbnail/{폴더명}, Sprite)

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
