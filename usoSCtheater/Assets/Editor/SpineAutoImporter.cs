using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Spine.Unity;

/// <summary>
/// Spine 자동 세팅 에디터 창
/// 메뉴: Tools > Spine Auto Setup
/// </summary>
public class SpineAutoImporter : EditorWindow
{
    // ── 탭 ───────────────────────────────────────────────────────────────
    private int selectedTab = 0;
    private readonly string[] tabLabels = new[] { "Auto Setup", "Atlas Converter" };

    // ── Auto Setup 탭 ────────────────────────────────────────────────────
    private Vector2 scrollPos;

    private struct SpineCandidate
    {
        public string spineName;        // ex) mei_sports_1
        public string idolName;         // ex) mei
        public string skeletonDataPath; // Assets/Resources/Spine/mei/mei_sports_1_SkeletonData.asset
        public string materialPath;     // Assets/Resources/Spine/mei/mei_sports_1_Material.mat
        public bool alreadyRegistered;
    }

    private List<SpineCandidate> candidates = new List<SpineCandidate>();
    private List<bool> selected = new List<bool>();
    private string scanLog = "";
    private bool scanned = false;

    // ── Atlas Converter 탭 ──────────────────────────────────────────────
    private string atlasSourceFolder = "";
    private string convertLog = "";

    // ── 상수 ─────────────────────────────────────────────────────────────
    private const string SPINE_RESOURCE_PATH = "Assets/Resources/Spine";
    private const string SHADER_NAME = "Spine/Straight Alpha/Skeleton Fill";
    private const string CANVAS_NAME = "Canvas";
    private const string CG_NAME = "CG";
    private const string CGMANAGER_NAME = "CGManager";
    private const string SORTING_LAYER = "CG";
    private const int ORDER_IN_LAYER = 1;

    // ─────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Spine Auto Setup")]
    public static void ShowWindow()
    {
        var window = GetWindow<SpineAutoImporter>("Spine Auto Setup");
        window.minSize = new Vector2(480f, 520f);
    }

    // ─────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        selectedTab = GUILayout.Toolbar(selectedTab, tabLabels);
        EditorGUILayout.Space(6);

        if (selectedTab == 0) DrawAutoSetupTab();
        else DrawAtlasConverterTab();
    }

    // ── Auto Setup 탭 그리기 ─────────────────────────────────────────────
    private void DrawAutoSetupTab()
    {
        EditorGUILayout.HelpBox(
            "Assets/Resources/Spine 폴더를 스캔하여 미등록 Spine을 찾고,\n" +
            "쉐이더 변경 / 씬 오브젝트 생성 / CGManager 등록을 자동으로 처리합니다.",
            MessageType.Info);

        EditorGUILayout.Space(4);

        if (GUILayout.Button("▶ 스캔", GUILayout.Height(32)))
        {
            RunScan();
        }

        if (!scanned) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("스캔 결과", EditorStyles.boldLabel);

        if (!string.IsNullOrEmpty(scanLog))
            EditorGUILayout.HelpBox(scanLog, MessageType.None);

        if (candidates.Count == 0)
        {
            EditorGUILayout.HelpBox("처리할 항목이 없습니다. 모두 등록되어 있거나 SkeletonData가 없습니다.", MessageType.Warning);
            return;
        }

        // 전체 선택/해제
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("전체 선택", GUILayout.Width(80))) for (int i = 0; i < selected.Count; i++) selected[i] = true;
        if (GUILayout.Button("전체 해제", GUILayout.Width(80))) for (int i = 0; i < selected.Count; i++) selected[i] = false;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        // 후보 목록
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.MaxHeight(260));
        for (int i = 0; i < candidates.Count; i++)
        {
            EditorGUILayout.BeginHorizontal("box");
            selected[i] = EditorGUILayout.Toggle(selected[i], GUILayout.Width(20));
            EditorGUILayout.LabelField($"[{candidates[i].idolName}]  {candidates[i].spineName}");
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4);

        bool anySelected = selected.Any(s => s);
        GUI.enabled = anySelected;
        if (GUILayout.Button("▶ 선택 항목 자동 세팅 실행", GUILayout.Height(36)))
        {
            RunSetup();
        }
        GUI.enabled = true;
    }

    // ── Atlas Converter 탭 그리기 ────────────────────────────────────────
    private void DrawAtlasConverterTab()
    {
        EditorGUILayout.HelpBox(
            "지정한 폴더 안의 .atlas 파일을 .atlas.txt로 변환합니다.\n" +
            "변환 후 Resources/Spine/{아이돌} 폴더에 직접 넣으세요.",
            MessageType.Info);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("변환할 폴더 경로 (절대 경로)");

        EditorGUILayout.BeginHorizontal();
        atlasSourceFolder = EditorGUILayout.TextField(atlasSourceFolder);
        if (GUILayout.Button("찾기", GUILayout.Width(50)))
        {
            string path = EditorUtility.OpenFolderPanel("Atlas 파일이 있는 폴더 선택", "", "");
            if (!string.IsNullOrEmpty(path)) atlasSourceFolder = path;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        GUI.enabled = !string.IsNullOrEmpty(atlasSourceFolder);
        if (GUILayout.Button("▶ .atlas → .atlas.txt 변환", GUILayout.Height(32)))
        {
            RunAtlasConvert();
        }
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(convertLog))
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(convertLog, MessageType.None);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 스캔 로직
    // ─────────────────────────────────────────────────────────────────────
    private void RunScan()
    {
        candidates.Clear();
        selected.Clear();
        scanLog = "";
        scanned = true;

        // CGManager에 이미 등록된 key 수집
        HashSet<string> registeredKeys = GetRegisteredKeys();

        // Spine 폴더 하위 아이돌 폴더 순회
        string[] idolDirs = AssetDatabase.GetSubFolders(SPINE_RESOURCE_PATH);
        int foundCount = 0;

        foreach (string idolDir in idolDirs)
        {
            string idolName = Path.GetFileName(idolDir).ToLower();
            string[] allAssets = AssetDatabase.FindAssets("t:SkeletonDataAsset", new[] { idolDir });

            foreach (string guid in allAssets)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string assetName = Path.GetFileNameWithoutExtension(assetPath); // ex) mei_sports_1_SkeletonData
                string spineName = assetName.Replace("_SkeletonData", "");      // ex) mei_sports_1
                string matPath = $"{idolDir}/{spineName}_Material.mat";

                var c = new SpineCandidate
                {
                    spineName = spineName,
                    idolName = idolName,
                    skeletonDataPath = assetPath,
                    materialPath = matPath,
                    alreadyRegistered = registeredKeys.Contains(spineName)
                };

                if (!c.alreadyRegistered)
                {
                    candidates.Add(c);
                    selected.Add(true);
                    foundCount++;
                }
            }
        }

        scanLog = foundCount == 0
            ? "미등록 항목 없음."
            : $"미등록 Spine {foundCount}개 발견. 처리할 항목을 선택 후 실행하세요.";

        Repaint();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 세팅 실행 로직
    // ─────────────────────────────────────────────────────────────────────
    private void RunSetup()
    {
        // Canvas > CG 찾기
        GameObject canvasObj = GameObject.Find(CANVAS_NAME);
        if (canvasObj == null) { Debug.LogError("[SpineAutoSetup] Canvas 오브젝트를 찾을 수 없습니다."); return; }

        Transform cgTransform = canvasObj.transform.Find(CG_NAME);
        if (cgTransform == null) { Debug.LogError("[SpineAutoSetup] Canvas > CG 오브젝트를 찾을 수 없습니다."); return; }

        // CGManager 찾기
        CGManager cgManager = FindCGManager();
        if (cgManager == null) { Debug.LogError("[SpineAutoSetup] CGManager 컴포넌트를 찾을 수 없습니다."); return; }

        Shader targetShader = Shader.Find(SHADER_NAME);
        if (targetShader == null) Debug.LogWarning($"[SpineAutoSetup] 쉐이더를 찾을 수 없습니다: {SHADER_NAME}");

        int successCount = 0;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (!selected[i]) continue;

            SpineCandidate c = candidates[i];
            bool ok = ProcessOne(c, cgTransform, cgManager, targetShader);
            if (ok) successCount++;
        }

        // 씬 파일 임시 이동 타이밍과의 충돌 방지를 위해 한 프레임 뒤에 저장
        int capturedCount = successCount;
        EditorApplication.delayCall += () =>
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            scanLog = $"✅ 완료: {capturedCount}개 처리됨. 씬을 저장해 주세요.";
            RunScan();
            Repaint();
        };
    }

    private bool ProcessOne(SpineCandidate c, Transform cgTransform, CGManager cgManager, Shader targetShader)
    {
        // ① 쉐이더 변경 - 씬 dirty 전에 머티리얼 에셋을 먼저 수정 후 즉시 저장
        //    (오브젝트 생성 이후에 SetDirty하면 씬 자동저장과 타이밍 충돌 발생)
        if (targetShader != null)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(c.materialPath);
            if (mat != null)
            {
                mat.shader = targetShader;
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssetIfDirty(mat);
            }
            else Debug.LogWarning($"[SpineAutoSetup] 머티리얼 없음: {c.materialPath}");
        }

        // ② 아이돌 폴더 찾기 (대소문자 무시)
        Transform idolFolder = FindChildIgnoreCase(cgTransform, c.idolName);
        if (idolFolder == null)
        {
            // 없으면 새로 생성
            GameObject newFolder = new GameObject(c.idolName);
            newFolder.transform.SetParent(cgTransform, false);
            idolFolder = newFolder.transform;
            Undo.RegisterCreatedObjectUndo(newFolder, "Create Idol Folder");
            Debug.Log($"[SpineAutoSetup] 아이돌 폴더 생성: {c.idolName}");
        }

        // ③ SkeletonDataAsset 로드
        SkeletonDataAsset skeletonData = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(c.skeletonDataPath);
        if (skeletonData == null)
        {
            Debug.LogError($"[SpineAutoSetup] SkeletonDataAsset 로드 실패: {c.skeletonDataPath}");
            return false;
        }

        // ④ SkeletonAnimation 오브젝트 생성 및 데이터 연결
        SkeletonAnimation skelAnim = Spine.Unity.Editor.SpineEditorUtilities.InstantiateSkeletonAnimation(skeletonData);
        if (skelAnim == null)
        {
            Debug.LogError($"[SpineAutoSetup] SkeletonAnimation 생성 실패: {c.spineName}");
            return false;
        }

        GameObject spineObj = skelAnim.gameObject;
        spineObj.name = c.spineName;
        spineObj.transform.SetParent(idolFolder, false);
        Undo.RegisterCreatedObjectUndo(spineObj, "Create Spine Object");

        // ⑤ SortingLayer, Order 설정
        MeshRenderer meshRenderer = spineObj.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.sortingLayerName = SORTING_LAYER;
            meshRenderer.sortingOrder = ORDER_IN_LAYER;
            EditorUtility.SetDirty(meshRenderer);
        }

        // ⑥ CGManager SpineEntries 등록
        RegisterToCGManager(cgManager, c.spineName, spineObj);

        Debug.Log($"[SpineAutoSetup] 처리 완료: {c.spineName}");
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 헬퍼 메서드
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>씬에서 CGManager 컴포넌트를 찾아 반환</summary>
    private CGManager FindCGManager()
    {
        GameObject gmObj = GameObject.Find(CGMANAGER_NAME);
        if (gmObj != null) return gmObj.GetComponent<CGManager>();
        return null;
    }

    /// <summary>대소문자 무시하고 자식 Transform 탐색</summary>
    private Transform FindChildIgnoreCase(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (string.Equals(child.name, name, System.StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    /// <summary>CGManager의 spineEntries에 이미 등록된 key 목록 반환</summary>
    private HashSet<string> GetRegisteredKeys()
    {
        var keys = new HashSet<string>();
        CGManager cgManager = FindCGManager();
        if (cgManager == null) return keys;

        SerializedObject so = new SerializedObject(cgManager);
        SerializedProperty entries = so.FindProperty("spineEntries");
        if (entries == null) return keys;

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            SerializedProperty keyProp = entry.FindPropertyRelative("key");
            if (keyProp != null) keys.Add(keyProp.stringValue);
        }
        return keys;
    }

    /// <summary>CGManager.spineEntries에 새 항목 추가</summary>
    private void RegisterToCGManager(CGManager cgManager, string key, GameObject spineObj)
    {
        SerializedObject so = new SerializedObject(cgManager);
        so.Update();

        SerializedProperty entries = so.FindProperty("spineEntries");
        if (entries == null)
        {
            Debug.LogError("[SpineAutoSetup] spineEntries 프로퍼티를 찾을 수 없습니다.");
            return;
        }

        // 중복 체크
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("key").stringValue == key)
            {
                Debug.LogWarning($"[SpineAutoSetup] 이미 등록된 key: {key}");
                return;
            }
        }

        // 새 항목 추가
        entries.arraySize++;
        SerializedProperty newEntry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
        newEntry.FindPropertyRelative("key").stringValue = key;
        newEntry.FindPropertyRelative("spineObject").objectReferenceValue = spineObj;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(cgManager);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Atlas 변환 로직
    // ─────────────────────────────────────────────────────────────────────
    private void RunAtlasConvert()
    {
        convertLog = "";

        if (!Directory.Exists(atlasSourceFolder))
        {
            convertLog = "❌ 폴더가 존재하지 않습니다.";
            return;
        }

        string[] atlasFiles = Directory.GetFiles(atlasSourceFolder, "*.atlas", SearchOption.TopDirectoryOnly);

        if (atlasFiles.Length == 0)
        {
            convertLog = "변환할 .atlas 파일이 없습니다.";
            return;
        }

        int count = 0;
        var log = new System.Text.StringBuilder();

        foreach (string src in atlasFiles)
        {
            string dest = src + ".txt";
            if (File.Exists(dest))
            {
                log.AppendLine($"  건너뜀 (이미 존재): {Path.GetFileName(dest)}");
                continue;
            }
            File.Move(src, dest);
            log.AppendLine($"  ✅ {Path.GetFileName(src)} → {Path.GetFileName(dest)}");
            count++;
        }

        convertLog = $"변환 완료: {count}개\n{log}";
        AssetDatabase.Refresh();
        Repaint();
    }
}
