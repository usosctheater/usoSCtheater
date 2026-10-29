using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;   //[ScenarioData 자동 추가] StringBuilder · UTF8Encoding
using System.Xml;   //[목록 UI] 막 헤더(mainTitle/subTitle) 읽기
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UsoSCTheater.Scenario;
using UsoSCTheater;   //[리소스 정리] ResourcePaths

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
    private const string ScenarioRootFolder = ResourcePaths.AssetsRoot + "/" + ResourcePaths.Scenario;   //[리소스 정리] 폴더는 ResourcePaths   //[용어 정리] "Assets/Resources/Scene" → "Assets/Resources/Scenario" (ScenarioCatalog.ScenarioRoot와 일치해야 함)
    private const string CatalogAssetPath = ResourcePaths.AssetsRoot + "/" + ResourcePaths.Data + "/ScenarioCatalog.asset";

    //[시나리오 데이터] 시나리오별 태그/등장인물 문서 (동기화 때만 읽음)
    //양식: <Scenario Id="IL" Tag="a, b" Character="x, y" /> — 요소/속성 이름, Id 모두 대소문자 무시
    private const string ScenarioDataPath = ResourcePaths.AssetsRoot + "/" + ResourcePaths.Data + "/ScenarioData.xml";
    private const string DataScenarioTag = "Scenario";
    private const string DataIdAttr = "Id";
    private const string DataTagAttr = "Tag";
    private const string DataCharacterAttr = "Character";
    private const string DataIrisAttr = "Iris";                   //[Iris 아이콘] 직접 지정 (유닛 ID 또는 캐릭터 ID, 쉼표 구분)
    private static readonly char[] DataListSeparator = { ',' };   // Tag / Character / Iris 목록 구분자

    //[엔딩 SD] 막 XML에서 사용한 CG 키 수집
    private const string ActCgAttr = "CG";
    private const string ActCgNone = "none";
    private const string ActTypeAttr = "Type";          //[LD 불러오기] CGGROUP 그룹 이름 제외용
    private const string ActNameAttr = "Name";
    private const string ActCgGroupType = "CGGROUP";

    private static bool syncPending = false;

    //[목록 UI] 폴더 스캔 결과
    private class ScanResult
    {
        public List<string> acts = new List<string>();
        public List<string> actTitles = new List<string>();
        public string scenarioTitle = "";
        public List<string> tags = new List<string>();         //[시나리오 데이터]
        public List<string> characters = new List<string>();   //[시나리오 데이터]
        public List<string> irisIcons = new List<string>();    //[Iris 아이콘]
        public List<string> spineKeys = new List<string>();    //[엔딩 SD]
    }

    //[시나리오 데이터] ScenarioData.xml의 시나리오 1개
    private class ScenarioDataItem
    {
        public List<string> tags = new List<string>();
        public List<string> characters = new List<string>();
        public List<string> irisIcons = new List<string>();    //[Iris 아이콘]
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
                ReadActHeader(path, out string mainTitle, out string subTitle, scan.spineKeys);   //[엔딩 SD] CG 키도 함께 수집
                scan.acts.Add(Path.GetFileNameWithoutExtension(path));
                scan.actTitles.Add(subTitle);
                if (string.IsNullOrEmpty(scan.scenarioTitle) && !string.IsNullOrEmpty(mainTitle)) scan.scenarioTitle = mainTitle;
            }
            found[name] = scan;
        }

        //[시나리오 데이터] 태그/등장인물 병합 (문서가 없거나 파싱 실패면 null → 전부 빈 값, 개별 경고 생략)
        var data = ReadScenarioData();
        //[ScenarioData 자동 추가] 폴더는 있는데 항목이 없는 시나리오 → 빈 항목을 문서 끝에 추가하고 다시 읽음 (사용자는 속성만 기입)
        if (data != null && AppendMissingScenarioData(found.Keys.Where(id => !data.ContainsKey(id)).ToList()))
            data = ReadScenarioData();
        if (data != null)
        {
            foreach (var kv in found)
            {
                if (data.TryGetValue(kv.Key, out var item))
                {
                    kv.Value.tags = item.tags;
                    kv.Value.characters = item.characters;
                    kv.Value.irisIcons = item.irisIcons;   //[Iris 아이콘]
                }
                //[ScenarioData 자동 추가] 경고 → 오류 (자동 추가 뒤에도 없음 = 쓰기 실패 등)
                else Debug.LogError($"[ScenarioCatalogSync] '{kv.Key}': ScenarioData.xml에 항목 없음 (자동 추가 실패) → 태그/등장인물 비움");
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
            if (entry.irisIcons == null || !entry.irisIcons.SequenceEqual(scan.irisIcons))   //[Iris 아이콘]
            {
                entry.irisIcons = scan.irisIcons;
                changed = true;
            }
            if (entry.spineKeys == null || !entry.spineKeys.SequenceEqual(scan.spineKeys))   //[엔딩 SD]
            {
                entry.spineKeys = scan.spineKeys;
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
                irisIcons = kv.Value.irisIcons, spineKeys = kv.Value.spineKeys,   //[Iris 아이콘] [엔딩 SD]
            });
            changed = true;
        }

        //[LD 불러오기] 추가: 막이 쓰는 CG 키가 인덱스에 LD로 등록돼 있는지 검사 (빌드는 막지 않고 경고만)
        ValidateSpineKeys(merged);

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

    //[LD 불러오기] 인덱스(CharacterSpineData.xml)에 LD로 없는 CG 키 경고 — 게임에서는 그 캐릭터가 나오지 않음
    //TE 막 저장 → Register Spine 등록 빠뜨림을 플레이 전에 발견. 빌드 직전 동기화에서도 실행 (빌드는 막지 않음, 2026-10-28 결정)
    private static void ValidateSpineKeys(List<ScenarioEntry> entries)
    {
        UsoSCTheater.Character.CharacterDatabase.Reload();   //등록 도구가 바꾼 인덱스 반영
        foreach (ScenarioEntry e in entries)
        {
            if (e.spineKeys == null) continue;
            var missing = e.spineKeys
                .Where(k => !UsoSCTheater.Character.CharacterDatabase.TryGetSpine(k, UsoSCTheater.Character.SpineKind.LD, out _))
                .ToList();
            if (missing.Count > 0)
                Debug.LogWarning($"[ScenarioCatalogSync] '{e.folderName}': 등록되지 않은 LD Spine {missing.Count}개 → Tools > Spine > Register Spine\n  {string.Join("\n  ", missing)}");
        }
    }

    //[리소스 정리] 막의 BG·IMAGE 키가 실제 파일로 있는지 (없으면 경고, 빌드는 막지 않음 — Spine 검사와 같은 방침)
    private static void ValidateActResourceKeys(string assetPath, XmlDocument doc)
    {
        var missing = new List<string>();
        foreach (XmlNode node in doc.GetElementsByTagName("*"))
        {
            string type = ActXml.GetAttrIgnoreCase(node, ActTypeAttr);
            string folder = string.Equals(type, "BG", StringComparison.OrdinalIgnoreCase) ? ResourcePaths.BG
                          : string.Equals(type, "IMAGE", StringComparison.OrdinalIgnoreCase) ? ResourcePaths.ScenarioImage : null;
            if (folder == null) continue;

            string key = ActXml.GetAttrIgnoreCase(node, "ResourceKey");
            if (string.IsNullOrEmpty(key)) key = ActXml.GetAttrIgnoreCase(node, "Key");   //DialogManager.GetResourceKey와 같은 폴백
            if (string.IsNullOrWhiteSpace(key)) continue;
            string entry = $"{type.ToUpperInvariant()} {key.Trim()}";
            if (!ResourceFileExists(folder, key.Trim()) && !missing.Contains(entry)) missing.Add(entry);
        }
        if (missing.Count > 0)
            Debug.LogWarning($"[ScenarioCatalogSync] {Path.GetFileNameWithoutExtension(assetPath)}: 리소스 파일 없음 {missing.Count}개 (BG → Resources/{ResourcePaths.BG}, IMAGE → Resources/{ResourcePaths.ScenarioImage})\n  {string.Join("\n  ", missing)}");
    }

    //[리소스 정리] Resources/{folder}/{key}.* 파일이 있는지 (.meta 제외)
    private static bool ResourceFileExists(string folder, string key)
    {
        string full = $"{ResourcePaths.AssetsRoot}/{folder}/{key}";
        string dir = Path.GetDirectoryName(full);
        return Directory.Exists(dir)
            && Directory.GetFiles(dir, Path.GetFileName(full) + ".*").Any(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase));
    }

    //[목록 UI] 막 XML 루트의 mainTitle / subTitle 읽기 (파싱 실패 시 빈 값 + 경고)
    //[엔딩 SD] spineKeys: 막 안 모든 요소의 CG 값(쉼표 구분)을 등장 순서대로 추가 (중복·none 제외, 대소문자 무시)
    private static void ReadActHeader(string assetPath, out string mainTitle, out string subTitle, List<string> spineKeys)
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

            //[엔딩 SD] 사용한 CG 키 수집
            //[LD 불러오기] 수정: CGGROUP 그룹 이름은 Spine 키가 아니므로 제외 (멤버 키는 CGGROUP 줄의 CG로 수집됨)
            var groupNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode node in doc.GetElementsByTagName("*"))
            {
                if (string.Equals(ActXml.GetAttrIgnoreCase(node, ActTypeAttr), ActCgGroupType, StringComparison.OrdinalIgnoreCase))
                    groupNames.Add(ActXml.GetAttrIgnoreCase(node, ActNameAttr).Split(',')[0].Trim());
            }

            foreach (XmlNode node in doc.GetElementsByTagName("*"))
            {
                foreach (string key in SplitList(ActXml.GetAttrIgnoreCase(node, ActCgAttr)))
                {
                    if (string.Equals(key, ActCgNone, StringComparison.OrdinalIgnoreCase)) continue;
                    if (groupNames.Contains(key)) continue;   //[LD 불러오기] 추가: 그룹 이름 제외
                    if (!spineKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) spineKeys.Add(key);
                }
            }

            ValidateActResourceKeys(assetPath, doc);   //[리소스 정리] BG·IMAGE 키 파일 존재 검사
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
            Debug.LogError($"[ScenarioCatalogSync] {ScenarioDataPath} 없음 → 태그/등장인물 없이 동기화");   //[ScenarioData 자동 추가] 경고 → 오류 (파일은 새로 만들지 않음)
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

                //[Iris 아이콘] 직접 지정 (유닛 ID 또는 캐릭터 ID, 소문자로 저장)
                foreach (string value in SplitList(ActXml.GetAttrIgnoreCase(node, DataIrisAttr)))
                {
                    string iconId = value.ToLowerInvariant();
                    if (!item.irisIcons.Contains(iconId)) item.irisIcons.Add(iconId);
                }

                //속성 이름 오타 확인 (예: Charactor → 읽히지 않으므로 경고)
                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (!IsName(attr, DataIdAttr) && !IsName(attr, DataTagAttr) && !IsName(attr, DataCharacterAttr) && !IsName(attr, DataIrisAttr))   //[Iris 아이콘] Iris 허용
                        Debug.LogWarning($"[ScenarioCatalogSync] ScenarioData.xml '{id}': 알 수 없는 속성 '{attr.Name}' → 무시");
                }
                result[id] = item;
            }
        }
        catch (XmlException e)
        {
            Debug.LogError($"[ScenarioCatalogSync] ScenarioData.xml 파싱 실패 — {e.Message}");   //[ScenarioData 자동 추가] 경고 → 오류
            return null;
        }
        return result;
    }

    //[ScenarioData 자동 추가] 빈 항목을 루트 닫는 태그 앞에 덧붙임. 사람이 쓴 서식·주석은 건드리지 않고 텍스트만 추가 (기존 항목은 다시 쓰지 않음)
    //속성 목록 = 아래 상수 (Id + Tag·Character·Iris) — 속성이 늘면 여기에 추가, 기존 항목은 생략 가능 + 빈 값으로 읽음
    private static readonly string[] ScenarioDataTemplateAttrs = { DataTagAttr, DataCharacterAttr, DataIrisAttr };

    private static bool AppendMissingScenarioData(List<string> ids)
    {
        if (ids.Count == 0) return false;
        try
        {
            byte[] bytes = File.ReadAllBytes(ScenarioDataPath);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text = new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";

            int close = text.LastIndexOf("</", StringComparison.Ordinal);   //루트 닫는 태그
            if (close < 0)
            {
                Debug.LogError("[ScenarioCatalogSync] ScenarioData.xml에 루트 닫는 태그가 없어 항목을 추가하지 못함");
                return false;
            }

            var sb = new StringBuilder();
            foreach (string id in ids.OrderBy(i => i, StringComparer.Ordinal))
            {
                sb.Append($"    <{DataScenarioTag}{nl}        {DataIdAttr}=\"{id}\"{nl}");
                foreach (string attr in ScenarioDataTemplateAttrs) sb.Append($"        {attr}=\"\"{nl}");
                sb.Append($"    />{nl}");
            }

            string before = text.Substring(0, close).TrimEnd() + nl;
            string result = before + sb + text.Substring(close);
            File.WriteAllText(ScenarioDataPath, result, new UTF8Encoding(bom));
            AssetDatabase.ImportAsset(ScenarioDataPath);
            Debug.Log($"[ScenarioCatalogSync] ScenarioData.xml에 빈 항목 추가: {string.Join(", ", ids)} → Tag·Character 등 속성을 채워 주세요");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScenarioCatalogSync] ScenarioData.xml 항목 자동 추가 실패 — {e.Message}");
            return false;
        }
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
