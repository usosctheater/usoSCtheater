using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UsoSCTheater;
using UsoSCTheater.Scenario;

/// <summary>
/// [다국어] 다국어 검사 메뉴 (에디터 전용, 경고만 — 빌드를 막지 않음). 빠뜨림을 사람이 기억하지 않고 도구가 알려 줌 (핸드오프 4장 다국어 원칙)
/// - Tools > Localization > Check Translations: 원문은 있는데 번역 속성({속성}_{언어})이 없는 곳 (막 XML·ScenarioData 태그·UI 문구)
/// - Tools > Localization > Check UI Texts: 씬·프리팹 TMP의 UI 문구 적용 상태
/// </summary>
public static class LocalizationCheck
{
    private const string PlaceholderPrefix = "(견본)";   // 코드가 덮어쓰는 견본 글자 표시 (UI 문구 규칙 5)
    private static readonly Regex CjkLetters = new Regex(@"[぀-ヿ㄰-㆏一-鿿가-힣]");   // 한글·가나·한자
    private static readonly Regex CodeIdRef = new Regex(@"UIText\.(?:Get|Format)\(\s*""([^""]+)""");                  // 코드에서 쓰는 Id
    private const string SkipFolder = "Assets/TextMesh Pro/";   // TMP 예제

    private static string DataFile(string name) => $"{ResourcePaths.AssetsRoot}/{ResourcePaths.Data}/{name}.xml";

    // ── 번역 누락 ─────────────────────────────────────────────────────
    [MenuItem("Tools/Localization/Check Translations")]
    private static void CheckTranslations()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[LocalizationCheck] Play 중에는 실행하지 않음"); return; }   //[언어별 이미지] 추가: 씬을 열기 때문
        var targets = UsoSCTheater.Localization.Languages.Where(l => !l.IsDefault).ToList();
        if (targets.Count == 0) { Debug.Log("[LocalizationCheck] 번역 검사: 기본 언어 말고 다른 언어가 없음"); return; }

        var report = new List<string>();
        CheckActs(targets, report);
        CheckDataFile(DataFile("ScenarioData"), "Scenario", "Tag", "ScenarioData 태그", targets, report);
        CheckDataFile(DataFile("UITextData"), "Text", UIText.TextAttr, "UI 문구", targets, report);
        CheckLocalizedImages(targets, report);   //[언어별 이미지] 추가

        if (report.Count == 0) Debug.Log("[LocalizationCheck] 번역 검사: 누락 없음");
        else Debug.LogWarning($"[LocalizationCheck] 번역 검사: {report.Count}건 (번역 누락·언어별 이미지 문제)\n  {string.Join("\n  ", report)}");   //[언어별 이미지] 수정: 문구
    }

    //막 XML: 루트 mainTitle·subTitle, TEXT 줄 Name·Text (ScenarioCatalogSync에서 옮김)
    private static void CheckActs(List<LanguageInfo> targets, List<string> report)
    {
        string root = $"{ResourcePaths.AssetsRoot}/{ResourcePaths.Scenario}";
        foreach (string folder in AssetDatabase.GetSubFolders(root))
        {
            var paths = AssetDatabase.FindAssets("t:TextAsset", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(p).Replace('\\', '/') == folder)
                .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal);
            foreach (string path in paths)
            {
                XmlNode act = LoadRoot(path, doc => ActXml.GetRoot(doc));
                if (act == null) continue;
                foreach (var language in targets)
                {
                    int title = new[] { ActXml.MainTitleAttr, ActXml.SubTitleAttr }.Count(a => IsMissing(act, a, language));
                    int name = 0, text = 0;
                    foreach (XmlNode line in act.ChildNodes)
                    {
                        if (line.NodeType != XmlNodeType.Element || line.Name != "Line") continue;
                        if (!string.Equals(ActXml.GetAttrIgnoreCase(line, "Type"), "TEXT", StringComparison.OrdinalIgnoreCase)) continue;
                        if (IsMissing(line, "Name", language)) name++;
                        if (IsMissing(line, "Text", language)) text++;
                    }
                    if (title + name + text > 0)
                        report.Add($"{Path.GetFileName(folder)}/{Path.GetFileNameWithoutExtension(path)} [{language.Code}] 제목 {title} · Name {name} · Text {text}");
                }
            }
        }
    }

    //데이터 문서: 요소마다 attr 원문은 있는데 번역이 없는 Id
    private static void CheckDataFile(string path, string element, string attr, string label, List<LanguageInfo> targets, List<string> report)
    {
        XmlNode root = LoadRoot(path, doc => doc.DocumentElement);
        if (root == null) return;
        foreach (var language in targets)
        {
            var ids = root.ChildNodes.Cast<XmlNode>()
                .Where(n => n.NodeType == XmlNodeType.Element && n.Name == element && IsMissing(n, attr, language))
                .Select(n => ActXml.GetAttrIgnoreCase(n, "Id")).ToList();
            if (ids.Count > 0) report.Add($"{label} [{language.Code}] {ids.Count}개: {string.Join(", ", ids)}");
        }
    }

    // ── UI 문구 적용 상태 ─────────────────────────────────────────────
    [MenuItem("Tools/Localization/Check UI Texts")]
    private static void CheckUITexts()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[LocalizationCheck] Play 중에는 실행하지 않음"); return; }
        UIText.Reload();
        var known = new HashSet<string>(UIText.Ids, StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();

        ForEachInPrefabsAndScenes<TMP_Text>((where, tmp) => CheckObject(where, tmp, known, used, issues));   //[언어별 이미지] 수정: 프리팹·씬 반복을 공용 함수로

        //코드에서 쓰는 Id
        foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            foreach (Match m in CodeIdRef.Matches(File.ReadAllText(file))) used.Add(m.Groups[1].Value);

        foreach (string id in known.Where(id => !used.Contains(id)).OrderBy(id => id))
            issues.Add($"UITextData.xml: 쓰지 않는 Id '{id}'");

        if (issues.Count == 0) Debug.Log("[LocalizationCheck] UI 문구 검사: 문제 없음");
        else Debug.LogWarning($"[LocalizationCheck] UI 문구 검사: {issues.Count}건\n  {string.Join("\n  ", issues)}");
    }

    //[언어별 이미지] 수정: TMP 목록 → TMP 하나씩 (CheckObjects → CheckObject)
    private static void CheckObject(string where, TMP_Text tmp, HashSet<string> known, HashSet<string> used, List<string> issues)
    {
        string at = $"{where} :: {PathOf(tmp.transform)}";
        var localized = tmp.GetComponent<LocalizedText>();
        if (localized == null)
        {
            string text = tmp.text ?? "";
            if (CjkLetters.IsMatch(text) && !text.TrimStart().StartsWith(PlaceholderPrefix, StringComparison.Ordinal))
                issues.Add($"{at}: LocalizedText 없는 문구 '{Short(text)}' (UI 문구면 LocalizedText + Id, 코드가 덮어쓰는 견본이면 '{PlaceholderPrefix}'로 시작)");
            return;
        }
        string id = localized.TextId?.Trim() ?? "";
        if (id.Length == 0) issues.Add($"{at}: LocalizedText의 Id가 비어 있음");
        else if (!known.Contains(id)) issues.Add($"{at}: UITextData.xml에 없는 Id '{id}'");
        else used.Add(id);
    }

    //[언어별 이미지] 추가: LocalizedImage의 언어별 Sprite 빠짐·잘못된 코드·빈 Sprite
    private static void CheckLocalizedImages(List<LanguageInfo> targets, List<string> report)
    {
        ForEachInPrefabsAndScenes<LocalizedImage>((where, img) =>
        {
            string at = $"{where} :: {PathOf(img.transform)}";
            foreach (var entry in img.Sprites)
            {
                var language = UsoSCTheater.Localization.Find(entry.language?.Trim());
                if (language == null || language.IsDefault) report.Add($"{at}: 잘못된 언어 코드 '{entry.language}' (기본 언어가 아닌 LanguageData 코드, 예: ja)");
                else if (entry.sprite == null) report.Add($"{at}: [{language.Code}] Sprite가 비어 있음");
            }
            foreach (var language in targets)
            {
                bool has = img.Sprites.Any(e => e.sprite != null && string.Equals(e.language?.Trim(), language.Code, StringComparison.OrdinalIgnoreCase));
                if (!has) report.Add($"{at}: [{language.Code}] 언어별 이미지 없음");
            }
        });
    }

    //[언어별 이미지] 추가 (CheckUITexts에서 빼냄): 프리팹(TMP 예제 제외)·Build Settings 씬의 T 컴포넌트마다 action. 열려 있지 않은 씬은 잠깐 열었다가 닫음 (저장 안 함)
    private static void ForEachInPrefabsAndScenes<T>(Action<string, T> action) where T : Component
    {
        foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            if (path.StartsWith(SkipFolder, StringComparison.Ordinal)) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            foreach (var c in prefab.GetComponentsInChildren<T>(true)) action(path, c);
        }
        foreach (var buildScene in EditorBuildSettings.scenes.Where(s => s.enabled))
        {
            var scene = EditorSceneManager.GetSceneByPath(buildScene.path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);
            foreach (var go in scene.GetRootGameObjects())
                foreach (var c in go.GetComponentsInChildren<T>(true)) action(buildScene.path, c);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // ── 공용 ──
    private static XmlNode LoadRoot(string path, Func<XmlDocument, XmlNode> root)
    {
        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (asset == null) return null;
        try { var doc = new XmlDocument(); doc.LoadXml(asset.text); return root(doc); }
        catch (XmlException) { return null; }   // 파싱 오류는 동기화·실행 중 경고가 알려 줌
    }

    private static bool IsMissing(XmlNode node, string attr, LanguageInfo language) =>
        ActXml.GetAttrIgnoreCase(node, attr).Trim().Length > 0
        && ActXml.GetAttrIgnoreCase(node, UsoSCTheater.Localization.AttrName(attr, language)).Trim().Length == 0;

    private static string PathOf(Transform t) => t.parent == null ? t.name : $"{PathOf(t.parent)}/{t.name}";
    private static string Short(string s) { s = s.Replace("\n", " "); return s.Length > 20 ? s.Substring(0, 20) + "…" : s; }
}
