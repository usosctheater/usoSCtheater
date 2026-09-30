using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UsoSCTheater.Scenario;

/// <summary>
/// 시나리오 루트(Resources/Scene) 하위 폴더를 스캔해 ScenarioCatalog를 자동 동기화한다. (에디터 전용)
/// - 시나리오 루트 아래 에셋이 추가/삭제/이동되면 자동 실행
/// - 수동: Tools > Scenario > Sync Catalog
/// - 빌드 직전 1회 실행, 카탈로그가 없으면 에디터 로드 시 생성
/// 병합 규칙: folderName 기준. displayName / hidden / 순서는 유지, actNames만 갱신.
/// [용어 정리] SceneFolder → ScenarioRootFolder, sceneNames → actNames
/// 새 폴더는 끝에 추가, 없어진 폴더는 제거.
/// 빌드(exe)는 빌드 시점 카탈로그를 그대로 사용 — 시나리오 추가는 재빌드로 반영.
/// </summary>
public class ScenarioCatalogSync : AssetPostprocessor
{
    private const string ScenarioRootFolder = "Assets/Resources/Scene";   //폴더 이름 변경(2단계) 시 "Assets/Resources/Scenario"로 수정
    private const string CatalogAssetPath = "Assets/Resources/Data/ScenarioCatalog.asset";

    private static bool syncPending = false;

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

    private static bool IsRelated(string[] paths) => paths.Any(p => p.StartsWith(ScenarioRootFolder + "/", StringComparison.Ordinal));

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

        //현재 폴더 상태 스캔: 폴더명 → 막 파일명 목록
        var found = new Dictionary<string, List<string>>();
        foreach (string folder in AssetDatabase.GetSubFolders(ScenarioRootFolder))
        {
            string name = Path.GetFileName(folder);

            //Resources.LoadAll은 하위 폴더까지 재귀 로드 → 시나리오 폴더 안의 하위 폴더는 재생 순서를 깨뜨림
            if (AssetDatabase.GetSubFolders(folder).Length > 0)
                Debug.LogWarning($"[ScenarioCatalogSync] '{name}' 안에 하위 폴더가 있습니다. ScenarioPlayer가 하위 폴더 막까지 함께 로드합니다.");

            found[name] = AssetDatabase.FindAssets("t:TextAsset", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                         && Path.GetDirectoryName(p).Replace('\\', '/') == folder)   //직속 파일만
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.Ordinal)                             //ScenarioPlayer 정렬과 동일
                .ToList();
        }

        //병합: 기존 항목 순서/수동 값 유지
        var result = new List<ScenarioEntry>();
        var added = new HashSet<string>();
        foreach (var entry in catalog.scenarios)
        {
            if (entry == null || !found.TryGetValue(entry.folderName ?? "", out var acts))
            {
                changed = true;   //폴더 삭제됨 → 제거
                continue;
            }
            if (entry.actNames == null || !entry.actNames.SequenceEqual(acts))
            {
                entry.actNames = acts;
                changed = true;
            }
            result.Add(entry);
            added.Add(entry.folderName);
        }
        foreach (var kv in found.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (added.Contains(kv.Key)) continue;
            result.Add(new ScenarioEntry { folderName = kv.Key, displayName = kv.Key, hidden = false, actNames = kv.Value });
            changed = true;
        }

        if (changed)
        {
            catalog.scenarios = result;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        if (verbose || changed)
            Debug.Log($"[ScenarioCatalogSync] 동기화 {(changed ? "완료(변경 있음)" : "완료(변경 없음)")}: "
                    + string.Join(", ", result.Select(e => $"{e.folderName}[{e.actNames.Count}]{(e.hidden ? "(hidden)" : "")}")));
    }
}

/// <summary>빌드 직전 카탈로그 최신화</summary>
public class ScenarioCatalogBuildSync : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => ScenarioCatalogSync.Sync(true);
}
