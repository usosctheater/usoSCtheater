using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;   //[목록 UI] 막 헤더(mainTitle/subTitle) 읽기
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UsoSCTheater.Scenario;

/// <summary>
/// 시나리오 루트(Resources/Scenario) 하위 폴더를 스캔해 ScenarioCatalog를 자동 동기화한다. (에디터 전용)
/// - 시나리오 루트 아래 에셋이 추가/삭제/이동되면 자동 실행
/// - 수동: Tools > Scenario > Sync Catalog
/// - 빌드 직전 1회 실행, 카탈로그가 없으면 에디터 로드 시 생성
/// 병합 규칙: folderName 기준. displayName / hidden / 순서는 유지, actNames / actTitles / scenarioTitle만 갱신.
/// [목록 UI] 막 XML 루트의 mainTitle(시나리오 제목: 처음으로 값이 있는 막) / subTitle(막 제목)도 함께 저장.
/// [시나리오 데이터] Resources/Data/ScenarioData.xml의 태그(tags) / 등장인물(characters)도 함께 저장. 이 문서가 바뀌어도 자동 실행.
/// [용어 정리] SceneFolder → ScenarioRootFolder, sceneNames → actNames
/// 새 폴더는 끝에 추가, 없어진 폴더는 제거.
/// 빌드(exe)는 빌드 시점 카탈로그를 그대로 사용 — 시나리오 추가는 재빌드로 반영.
/// </summary>
public class ScenarioCatalogSync : AssetPostprocessor
{
    private const string ScenarioRootFolder = "Assets/Resources/Scenario";   //[용어 정리] "Assets/Resources/Scene" → "Assets/Resources/Scenario" (ScenarioCatalog.ScenarioRoot와 일치해야 함)
    private const string CatalogAssetPath = "Assets/Resources/Data/ScenarioCatalog.asset";

    //[시나리오 데이터] 시나리오별 태그/등장인물 문서 (동기화 때만 읽음)
    //양식: <Scenario Id="IL" Tag="a, b" Character="x, y" /> — 요소/속성 이름, Id 모두 대소문자 무시
    private const string ScenarioDataPath = "Assets/Resources/Data/ScenarioData.xml";
    private const string DataScenarioTag = "Scenario";
    private const string DataIdAttr = "Id";
    private const string DataTagAttr = "Tag";
    private const string DataCharacterAttr = "Character";
    private static readonly char[] DataListSeparator = { ',' };   // Tag / Character 목록 구분자

    private static bool syncPending = false;

    //[목록 UI] 폴더 스캔 결과
    private class ScanResult
    {
        public List<string> acts = new List<string>();
        public List<string> actTitles = new List<string>();
        public string scenarioTitle = "";
        public List<string> tags = new List<string>();         //[시나리오 데이터]
        public List<string> characters = new List<string>();   //[시나리오 데이터]
    }

    //[시나리오 데이터] ScenarioData.xml의 시나리오 1개
    private class ScenarioDataItem
    {
        public List<string> tags = new List<string>();
        public List<string> characters = new List<string>();
    }

    // ── 자동 트리거 ──────────────────────────────────────────────────────
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (!IsRelated(imported) && !IsRelated(deleted) && !IsRelated(moved) && !IsRelated(movedFrom)) return;
        RequestSync();
    }

    [InitializeOnLoadMethod]
    private static void EnsureCatalogExists()
    {
        if (!File.Exists(CatalogAssetPath)) RequestSync();
    }

    //임포트 도중 에셋 생성/저장을 피하기 위해 다음 에디터 틱으로 미룸 (여러 번 호출돼도 1회만 실행)
    private static void RequestSync()
    {
        if (syncPending) return;
        syncPending = true;
        EditorApplication.delayCall += () => { syncPending = false; Sync(false); };
    }

    private static bool IsRelated(string[] paths) => paths.Any(p =>
        p.StartsWith(ScenarioRootFolder + "/", StringComparison.Ordinal)
        || string.Equals(p, ScenarioDataPath, StringComparison.OrdinalIgnoreCase));   //[시나리오 데이터] 데이터 문서 변경도 감지

    [MenuItem("Tools/Scenario/Sync Catalog")]
    private static void SyncMenu() => Sync(true);

    // ── 동기화 본체 ──────────────────────────────────────────────────────
    public static void Sync(bool verbose)
    {
        if (!AssetDatabase.IsValidFolder(ScenarioRootFolder))
        {
            Debug.LogWarning($"[ScenarioCatalogSync] {ScenarioRootFolder} 폴더가 없습니다.");
            return;
        }

        var catalog = AssetDatabase.LoadAssetAtPath<ScenarioCatalog>(CatalogAssetPath);
        bool changed = false;
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<ScenarioCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
            changed = true;
            Debug.Log($"[ScenarioCatalogSync] 카탈로그 생성: {CatalogAssetPath}");
        }

        //현재 폴더 상태 스캔: 폴더명 → 막 파일명 / 제목
        var found = new Dictionary<string, ScanResult>(StringComparer.OrdinalIgnoreCase);   //[목록 UI] List<string> → ScanResult, [시나리오 데이터] 대소문자 무시
        foreach (string folder in AssetDatabase.GetSubFolders(ScenarioRootFolder))
        {
            string name = Path.GetFileName(folder);

            //Resources.LoadAll은 하위 폴더까지 재귀 로드 → 시나리오 폴더 안의 하위 폴더는 재생 순서를 깨뜨림
            if (AssetDatabase.GetSubFolders(folder).Length > 0)
                Debug.LogWarning($"[ScenarioCatalogSync] '{name}' 안에 하위 폴더가 있습니다. ScenarioPlayer가 하위 폴더 막까지 함께 로드합니다.");

            //[목록 UI] 경로까지 보관 → 헤더(mainTitle/subTitle) 읽기
            var actPaths = AssetDatabase.FindAssets("t:TextAsset", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                         && Path.GetDirectoryName(p).Replace('\\', '/') == folder)                //직속 파일만
                .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal)     //ScenarioPlayer 정렬과 동일
                .ToList();

            var scan = new ScanResult();
            foreach (string path in actPaths)
            {
                ReadActHeader(path, out string mainTitle, out string subTitle);
                scan.acts.Add(Path.GetFileNameWithoutExtension(path));
                scan.actTitles.Add(subTitle);
                if (string.IsNullOrEmpty(scan.scenarioTitle) && !string.IsNullOrEmpty(mainTitle)) scan.scenarioTitle = mainTitle;
            }
            found[name] = scan;
        }

        //[시나리오 데이터] 태그/등장인물 병합 (문서가 없거나 파싱 실패면 null → 전부 빈 값, 개별 경고 생략)
        var data = ReadScenarioData();
        if (data != null)
        {
            foreach (var kv in found)
            {
                if (data.TryGetValue(kv.Key, out var item))
                {
                    kv.Value.tags = item.tags;
                    kv.Value.characters = item.characters;
                }
                else Debug.LogWarning($"[ScenarioCatalogSync] '{kv.Key}': ScenarioData.xml에 항목 없음 → 태그/등장인물 비움");
            }
            foreach (string id in data.Keys)
            {
                if (!found.ContainsKey(id)) Debug.LogWarning($"[ScenarioCatalogSync] ScenarioData.xml의 Id '{id}'에 해당하는 시나리오 폴더 없음 → 무시");
            }
        }

        //병합: 기존 항목 순서/수동 값 유지
        var merged = new List<ScenarioEntry>();   //[목록 UI] result → merged (ScanResult와 구분)
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   //[시나리오 데이터] 대소문자 무시
        foreach (var entry in catalog.scenarios)
        {
            if (entry == null || !found.TryGetValue(entry.folderName ?? "", out var scan))
            {
                changed = true;   //폴더 삭제됨 → 제거
                continue;
            }
            //[목록 UI] 막 목록 + 제목까지 비교/갱신
            if (entry.actNames == null || !entry.actNames.SequenceEqual(scan.acts))
            {
                entry.actNames = scan.acts;
                changed = true;
            }
            if (entry.actTitles == null || !entry.actTitles.SequenceEqual(scan.actTitles))
            {
                entry.actTitles = scan.actTitles;
                changed = true;
            }
            if ((entry.scenarioTitle ?? "") != scan.scenarioTitle)
            {
                entry.scenarioTitle = scan.scenarioTitle;
                changed = true;
            }
            //[시나리오 데이터] 태그/등장인물 갱신
            if (entry.tags == null || !entry.tags.SequenceEqual(scan.tags))
            {
                entry.tags = scan.tags;
                changed = true;
            }
            if (entry.characters == null || !entry.characters.SequenceEqual(scan.characters))
            {
                entry.characters = scan.characters;
                changed = true;
            }
            merged.Add(entry);
            added.Add(entry.folderName);
        }
        foreach (var kv in found.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (added.Contains(kv.Key)) continue;
            merged.Add(new ScenarioEntry
            {
                folderName = kv.Key, displayName = kv.Key, hidden = false,
                actNames = kv.Value.acts, actTitles = kv.Value.actTitles, scenarioTitle = kv.Value.scenarioTitle,   //[목록 UI]
                tags = kv.Value.tags, characters = kv.Value.characters,   //[시나리오 데이터]
            });
            changed = true;
        }

        if (changed)
        {
            catalog.scenarios = merged;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        if (verbose || changed)
            Debug.Log($"[ScenarioCatalogSync] 동기화 {(changed ? "완료(변경 있음)" : "완료(변경 없음)")}: "
                    + string.Join(", ", merged.Select(e => $"{e.folderName}[{e.actNames.Count}]{(e.hidden ? "(hidden)" : "")}")));
    }

    //[목록 UI] 막 XML 루트의 mainTitle / subTitle 읽기 (파싱 실패 시 빈 값 + 경고)
    private static void ReadActHeader(string assetPath, out string mainTitle, out string subTitle)
    {
        mainTitle = "";
        subTitle = "";

        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
        if (asset == null) return;

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(asset.text);
            XmlNode root = ActXml.GetRoot(doc);
            //[제목 읽기] 대소문자 구분 없이 조회
            mainTitle = ActXml.GetAttrIgnoreCase(root, ActXml.MainTitleAttr);
            subTitle  = ActXml.GetAttrIgnoreCase(root, ActXml.SubTitleAttr);
        }
        catch (XmlException e)
        {
            Debug.LogWarning($"[ScenarioCatalogSync] XML 파싱 실패: {assetPath} — {e.Message}");
        }
    }

    //[시나리오 데이터] ScenarioData.xml 읽기 → Id(대소문자 무시) → 태그/등장인물
    //<Scenario Id="" Tag="a, b" Character="x, y" /> — 쉼표 구분 목록, 앞뒤 공백·빈 항목 무시. 문서 없음/파싱 실패 시 null + 경고
    private static Dictionary<string, ScenarioDataItem> ReadScenarioData()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ScenarioDataPath);
        if (asset == null)
        {
            Debug.LogWarning($"[ScenarioCatalogSync] {ScenarioDataPath} 없음 → 태그/등장인물 없이 동기화");
            return null;
        }

        var result = new Dictionary<string, ScenarioDataItem>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(asset.text);
            if (doc.DocumentElement == null) return result;

            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element || !IsName(node, DataScenarioTag)) continue;

                string id = ActXml.GetAttrIgnoreCase(node, DataIdAttr).Trim();
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[ScenarioCatalogSync] ScenarioData.xml: Id 없는 <{DataScenarioTag}> → 무시");
                    continue;
                }
                if (result.ContainsKey(id))
                {
                    Debug.LogWarning($"[ScenarioCatalogSync] ScenarioData.xml: Id '{id}' 중복 → 첫 항목만 사용");
                    continue;
                }

                var item = new ScenarioDataItem();
                foreach (string tag in SplitList(ActXml.GetAttrIgnoreCase(node, DataTagAttr)))
                    item.tags.Add(tag);

                foreach (string value in SplitList(ActXml.GetAttrIgnoreCase(node, DataCharacterAttr)))
                {
                    string charId = value.ToLowerInvariant();   //캐릭터 ID는 소문자로 저장
                    if (item.characters.Contains(charId)) Debug.LogWarning($"[ScenarioCatalogSync] ScenarioData.xml '{id}': 등장인물 '{charId}' 중복 → 무시");
                    else item.characters.Add(charId);
                }

                //속성 이름 오타 확인 (예: Charactor → 읽히지 않으므로 경고)
                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (!IsName(attr, DataIdAttr) && !IsName(attr, DataTagAttr) && !IsName(attr, DataCharacterAttr))
                        Debug.LogWarning($"[ScenarioCatalogSync] ScenarioData.xml '{id}': 알 수 없는 속성 '{attr.Name}' → 무시");
                }
                result[id] = item;
            }
        }
        catch (XmlException e)
        {
            Debug.LogWarning($"[ScenarioCatalogSync] ScenarioData.xml 파싱 실패 — {e.Message}");
            return null;
        }
        return result;
    }

    //[시나리오 데이터] 요소/속성 이름 대소문자 무시 비교 (XmlAttribute도 XmlNode)
    private static bool IsName(XmlNode node, string name) => string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase);

    //[시나리오 데이터] "a, b, , c" → ["a", "b", "c"]
    private static IEnumerable<string> SplitList(string value) =>
        (value ?? "").Split(DataListSeparator, StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => s.Trim())
                     .Where(s => s.Length > 0);
}

/// <summary>빌드 직전 카탈로그 최신화</summary>
public class ScenarioCatalogBuildSync : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => ScenarioCatalogSync.Sync(true);
}
