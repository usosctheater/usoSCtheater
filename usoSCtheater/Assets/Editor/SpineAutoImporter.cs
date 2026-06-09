// using UnityEngine;
// using UnityEditor;
// using System.IO;
// using System.Collections.Generic;
// using Spine;
// using Spine.Unity;
// using Spine.Unity.Editor;

// /// <summary>
// /// Spine 자동 임포터 에디터 창
// /// json / .atlas.txt / png 3개 파일을 드래그&드롭하거나 지정하면
// /// AtlasAsset → SkeletonDataAsset 을 자동 생성하고
// /// SkeletonAnimation 이 달린 게임오브젝트를 씬에 배치합니다.
// /// 메뉴: Window > Spine > Auto Importer
// /// </summary>
// namespace Spine.Unity.Editor {
//     public class SpineAutoImporter : EditorWindow {

//         // ── 드래그&드롭 입력 필드 ──────────────────────────────────────────
//         private TextAsset jsonAsset;      // .json (Spine 스켈레톤 데이터)
//         private TextAsset atlasTextAsset; // .atlas.txt (Atlas 텍스트)
//         private Texture2D pngTexture;     // .png (Atlas 텍스처)

//         // ── 출력 옵션 ─────────────────────────────────────────────────────
//         private bool placeInScene = true;               // 씬에 게임오브젝트 생성 여부
//         private string outputFolder = "Assets/Spine Characters"; // 에셋 저장 경로

//         // ── 결과 참조 ─────────────────────────────────────────────────────
//         private AtlasAsset createdAtlas;
//         private SkeletonDataAsset createdSkeletonData;
//         private string lastLog = "";

//         // ─────────────────────────────────────────────────────────────────
//         [MenuItem("Window/Spine/Auto Importer")]
//         public static void ShowWindow() {
//             var window = GetWindow<SpineAutoImporter>("Spine Auto Importer");
//             window.minSize = new Vector2(380f, 420f);
//         }

//         // ─────────────────────────────────────────────────────────────────
//         private void OnGUI() {
//             GUILayout.Label("Spine Auto Importer", EditorStyles.boldLabel);
//             EditorGUILayout.HelpBox(
//                 "json, Atlas(.atlas.txt), PNG 세 파일을 아래 필드에 지정하면\n" +
//                 "AtlasAsset → SkeletonDataAsset 을 자동 생성합니다.",
//                 MessageType.Info);

//             EditorGUILayout.Space();

//             // ── 파일 입력 ─────────────────────────────────────────────────
//             EditorGUILayout.LabelField("① 파일 지정", EditorStyles.boldLabel);
//             jsonAsset      = (TextAsset) EditorGUILayout.ObjectField(
//                                 "Skeleton JSON (.json)", jsonAsset, typeof(TextAsset), false);
//             atlasTextAsset = (TextAsset) EditorGUILayout.ObjectField(
//                                 "Atlas Text (.atlas.txt)", atlasTextAsset, typeof(TextAsset), false);
//             pngTexture     = (Texture2D) EditorGUILayout.ObjectField(
//                                 "Atlas PNG (.png)", pngTexture, typeof(Texture2D), false);

//             EditorGUILayout.Space();

//             // ── 옵션 ──────────────────────────────────────────────────────
//             EditorGUILayout.LabelField("② 옵션", EditorStyles.boldLabel);
//             outputFolder = EditorGUILayout.TextField("출력 폴더", outputFolder);
//             placeInScene = EditorGUILayout.Toggle("씬에 게임오브젝트 생성", placeInScene);

//             EditorGUILayout.Space();

//             // ── 버튼 ──────────────────────────────────────────────────────
//             bool allAssigned = (jsonAsset != null && atlasTextAsset != null && pngTexture != null);
//             GUI.enabled = allAssigned;

//             if (GUILayout.Button("▶ Import & Create Assets", GUILayout.Height(36f))) {
//                 RunImport();
//             }

//             GUI.enabled = true;

//             // ── 결과 표시 ─────────────────────────────────────────────────
//             if (!string.IsNullOrEmpty(lastLog)) {
//                 EditorGUILayout.Space();
//                 EditorGUILayout.HelpBox(lastLog, MessageType.None);
//             }

//             // ── 결과 에셋 핑 버튼 ─────────────────────────────────────────
//             if (createdSkeletonData != null) {
//                 EditorGUILayout.Space();
//                 EditorGUILayout.LabelField("생성된 에셋", EditorStyles.boldLabel);
//                 EditorGUILayout.ObjectField("AtlasAsset", createdAtlas, typeof(AtlasAsset), false);
//                 EditorGUILayout.ObjectField("SkeletonDataAsset", createdSkeletonData, typeof(SkeletonDataAsset), false);

//                 if (GUILayout.Button("프로젝트 창에서 선택")) {
//                     Selection.activeObject = createdSkeletonData;
//                     EditorGUIUtility.PingObject(createdSkeletonData);
//                 }
//             }

//             // ── 드래그&드롭 지원 (창 전체) ────────────────────────────────
//             HandleDragAndDrop();
//         }

//         // ─────────────────────────────────────────────────────────────────
//         /// <summary>창 위로 파일을 드래그&드롭하면 자동 분류합니다.</summary>
//         private void HandleDragAndDrop() {
//             Event evt = Event.current;
//             if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
//                 return;

//             DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

//             if (evt.type == EventType.DragPerform) {
//                 DragAndDrop.AcceptDrag();
//                 foreach (Object dragObj in DragAndDrop.objectReferences) {
//                     string path = AssetDatabase.GetAssetPath(dragObj);
//                     if (dragObj is TextAsset ta) {
//                         if (path.EndsWith(".json"))
//                             jsonAsset = ta;
//                         else if (path.EndsWith(".atlas.txt"))
//                             atlasTextAsset = ta;
//                     } else if (dragObj is Texture2D tex) {
//                         pngTexture = tex;
//                     }
//                 }
//                 evt.Use();
//                 Repaint();
//             }
//         }

//         // ─────────────────────────────────────────────────────────────────
//         /// <summary>핵심 임포트 로직: Atlas → SkeletonData → (옵션) 게임오브젝트 생성</summary>
//         private void RunImport() {
//             // ── 출력 폴더 보장 ────────────────────────────────────────────
//             if (!AssetDatabase.IsValidFolder(outputFolder)) {
//                 // 중간 경로까지 순차 생성
//                 EnsureFolderExists(outputFolder);
//             }

//             // ── 1단계: AtlasAsset 생성 ────────────────────────────────────
//             createdAtlas = CreateAtlasAsset();
//             if (createdAtlas == null) {
//                 lastLog = "❌ AtlasAsset 생성 실패. 콘솔 로그를 확인하세요.";
//                 return;
//             }

//             // ── 2단계: SkeletonDataAsset 생성 ─────────────────────────────
//             createdSkeletonData = CreateSkeletonDataAsset(createdAtlas);
//             if (createdSkeletonData == null) {
//                 lastLog = "❌ SkeletonDataAsset 생성 실패. 콘솔 로그를 확인하세요.";
//                 return;
//             }

//             // ── 3단계: (옵션) 씬에 SkeletonAnimation 게임오브젝트 배치 ────
//             if (placeInScene) {
//                 PlaceSkeletonInScene(createdSkeletonData);
//             }

//             lastLog = $"✅ 임포트 완료!\n" +
//                       $"  AtlasAsset      : {AssetDatabase.GetAssetPath(createdAtlas)}\n" +
//                       $"  SkeletonDataAsset: {AssetDatabase.GetAssetPath(createdSkeletonData)}";

//             AssetDatabase.SaveAssets();
//             AssetDatabase.Refresh();
//             Repaint();
//         }

//         // ─────────────────────────────────────────────────────────────────
//         /// <summary>AtlasAsset 을 생성하고 반환합니다.</summary>
//         private AtlasAsset CreateAtlasAsset() {
//             string primaryName = Path.GetFileNameWithoutExtension(atlasTextAsset.name)
//                                      .Replace(".atlas", "");
//             string atlasAssetPath = $"{outputFolder}/{primaryName}_Atlas.asset";

//             // 기존 에셋이 있으면 재사용
//             AtlasAsset atlas = AssetDatabase.LoadAssetAtPath<AtlasAsset>(atlasAssetPath);
//             if (atlas == null) {
//                 atlas = ScriptableObject.CreateInstance<AtlasAsset>();
//                 AssetDatabase.CreateAsset(atlas, atlasAssetPath);
//             }

//             // ── 머티리얼 생성 (PNG → Material) ────────────────────────────
//             string texPath    = AssetDatabase.GetAssetPath(pngTexture);
//             string matPath    = $"{outputFolder}/{primaryName}_Material.mat";
//             Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
//             if (mat == null) {
//                 mat = new Material(Shader.Find(SpineEditorUtilities.defaultShader));
//                 AssetDatabase.CreateAsset(mat, matPath);
//             }
//             mat.mainTexture = pngTexture;
//             EditorUtility.SetDirty(mat);

//             // ── Atlas 필드 설정 ───────────────────────────────────────────
//             atlas.atlasFile = atlasTextAsset;
//             atlas.materials = new Material[] { mat };
//             EditorUtility.SetDirty(atlas);
//             AssetDatabase.SaveAssets();

//             Debug.Log($"[SpineAutoImporter] AtlasAsset 생성: {atlasAssetPath}");
//             return AssetDatabase.LoadAssetAtPath<AtlasAsset>(atlasAssetPath);
//         }

//         // ─────────────────────────────────────────────────────────────────
//         /// <summary>SkeletonDataAsset 을 생성하고 반환합니다.</summary>
//         private SkeletonDataAsset CreateSkeletonDataAsset(AtlasAsset atlasAsset) {
//             string primaryName   = Path.GetFileNameWithoutExtension(jsonAsset.name);
//             string skeletonPath  = $"{outputFolder}/{primaryName}_SkeletonData.asset";

//             SkeletonDataAsset skeletonData = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(skeletonPath);
//             if (skeletonData == null) {
//                 skeletonData = ScriptableObject.CreateInstance<SkeletonDataAsset>();
//                 AssetDatabase.CreateAsset(skeletonData, skeletonPath);
//             }

//             // ── SkeletonDataAsset 필드 설정 ───────────────────────────────
//             skeletonData.skeletonJSON  = jsonAsset;
//             skeletonData.atlasAssets   = new AtlasAsset[] { atlasAsset };
//             skeletonData.defaultMix    = SpineEditorUtilities.defaultMix;
//             skeletonData.scale         = SpineEditorUtilities.defaultScale;

//             // 캐시 초기화 후 유효성 검증
//             skeletonData.Clear();
//             SkeletonData skelData = skeletonData.GetSkeletonData(true);
//             if (skelData == null) {
//                 Debug.LogError($"[SpineAutoImporter] SkeletonData 로드 실패: {skeletonPath}");
//                 AssetDatabase.DeleteAsset(skeletonPath);
//                 return null;
//             }

//             EditorUtility.SetDirty(skeletonData);
//             AssetDatabase.SaveAssets();

//             Debug.Log($"[SpineAutoImporter] SkeletonDataAsset 생성: {skeletonPath}");
//             return AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(skeletonPath);
//         }

//         // ─────────────────────────────────────────────────────────────────
//         /// <summary>씬에 SkeletonAnimation 컴포넌트가 달린 게임오브젝트를 생성합니다.</summary>
//         private void PlaceSkeletonInScene(SkeletonDataAsset skeletonDataAsset) {
//             // InstantiateSkeletonAnimation 은 SkeletonAnimation 컴포넌트를 반환합니다.
//             SkeletonAnimation skelAnim = SpineEditorUtilities.InstantiateSkeletonAnimation(skeletonDataAsset);
//             if (skelAnim == null) {
//                 Debug.LogWarning("[SpineAutoImporter] 씬 배치 실패: InstantiateSkeletonAnimation 이 null 을 반환했습니다.");
//                 return;
//             }

//             skelAnim.gameObject.name = skeletonDataAsset.name.Replace("_SkeletonData", "");

//             // 씬 뷰 카메라 앞 중앙에 배치
//             if (SceneView.lastActiveSceneView != null) {
//                 Camera sv = SceneView.lastActiveSceneView.camera;
//                 skelAnim.transform.position = sv.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 10f));
//             }

//             Undo.RegisterCreatedObjectUndo(skelAnim.gameObject, "Create Spine GameObject");
//             Selection.activeGameObject = skelAnim.gameObject;

//             Debug.Log($"[SpineAutoImporter] 씬에 게임오브젝트 생성: {skelAnim.gameObject.name}");
//         }

//         // ─────────────────────────────────────────────────────────────────
//         /// <summary>"Assets/A/B/C" 형식의 경로를 단계별로 생성합니다.</summary>
//         private static void EnsureFolderExists(string path) {
//             string[] parts = path.Split('/');
//             string current = parts[0]; // "Assets"
//             for (int i = 1; i < parts.Length; i++) {
//                 string next = current + "/" + parts[i];
//                 if (!AssetDatabase.IsValidFolder(next))
//                     AssetDatabase.CreateFolder(current, parts[i]);
//                 current = next;
//             }
//         }
//     }
// }
