using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using UsoSCTheater.Character;

/// <summary>
/// Spine 등록 에디터 창 (구 SpineAutoImporter — 2026-10-27 이름 변경·개편, Atlas Converter 탭 삭제: TE가 atlas 이미지 이름을 맞춰 내보냄)
/// 메뉴: Tools > Spine > Register Spine
/// TE [게임으로 내보내기]로 들어온 Resources/Spine/{id}/의 Spine을 키 타입으로 LD/SD 분류 → 선택 항목 등록 → 인덱스(CharacterSpineData.xml) 기록
///   등록 = 머티리얼 셰이더 변경 + 인덱스 기록 (LD·SD 공통, 씬 변경 없음)
///   [LD 불러오기] 2026-10-28: LD도 씬 오브젝트·CGManager.spineEntries를 만들지 않음 → 게임(CGManager.PrepareAct)이 막 시작 때 인덱스 + 경로 규칙으로 생성
/// 스캔은 창을 열 때·버튼을 누를 때·적용 뒤에만 함 (OnGUI는 캐시된 목록만 그림)
/// </summary>
public class SpineRegisterTool : EditorWindow
{
    private enum Status { WaitingImport, Unregistered, Registered, MissingFile, Unknown }

    private class Row
    {
        public string key;
        public string characterId;            // 키 앞부분 (키 형식이 다르면 폴더 이름)
        public SpineKeyInfo info;             // 키 형식이 다르면 null
        public SpineKind kind;
        public Status status;
        public string folder;                 // Assets/Resources/Spine/{폴더}
        public string skeletonDataPath;       // 없으면 null
        public bool selectable;
        public bool selected;
        public string note;                   // 상태 보충 설명 (경고 등)
    }

    // ── 상수 ─────────────────────────────────────────────────────────────
    private const string SPINE_RESOURCE_PATH = "Assets/Resources/Spine";
    private const string SHADER_NAME = "Spine/Straight Alpha/Skeleton Fill";
    //[LD 불러오기] 삭제: CANVAS_NAME·CG_NAME·CGMANAGER_NAME·SORTING_LAYER·ORDER_IN_LAYER·PMA_VERTEX_COLORS(E-004)
    //  → LD 오브젝트는 게임이 실행 중에 만듦 (생성 설정은 CGManager의 SPINE_* 상수)

    private static readonly string[] KIND_FILTER_LABELS = { "전체", "LD", "SD", "알 수 없음" };

    // ── 상태 ─────────────────────────────────────────────────────────────
    private readonly List<Row> rows = new List<Row>();
    private readonly Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
    private string[] characterOptions = { "전체" };
    private string scanLog = "";
    private Vector2 scrollPos;

    // 필터
    private int kindFilter;
    private int characterFilter;
    private string search = "";
    private bool unregisteredOnly = true;

    // ─────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Spine/Register Spine")]
    public static void ShowWindow()
    {
        var window = GetWindow<SpineRegisterTool>("Spine Register");
        window.minSize = new Vector2(560f, 560f);
    }

    private void OnEnable() => RunScan();

    // ─────────────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────────────
    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Resources/Spine/{캐릭터}/의 Spine을 LD(커뮤니케이션)·SD(엔딩)로 분류해 등록합니다.\n" +
            "등록 = 셰이더 적용 + 인덱스 기록 (씬은 필요 없음 — LD는 게임이 막 시작 때 불러옴)",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("↻ 다시 스캔", GUILayout.Height(26))) RunScan();
        if (GUILayout.Button("인덱스 다시 만들기", GUILayout.Height(26), GUILayout.Width(150))) RebuildIndex();
        EditorGUILayout.EndHorizontal();

        //[LD 불러오기] 삭제: CommunicationScene 열림/닫힘 표시
        if (!string.IsNullOrEmpty(scanLog)) EditorGUILayout.HelpBox(scanLog, MessageType.None);

        DrawFilters();
        DrawList();
        DrawFooter();
    }

    private void DrawFilters()
    {
        EditorGUILayout.Space(4);
        kindFilter = GUILayout.Toolbar(kindFilter, KIND_FILTER_LABELS);
        EditorGUILayout.BeginHorizontal();
        characterFilter = EditorGUILayout.Popup(characterFilter, characterOptions, GUILayout.Width(130));
        search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
        unregisteredOnly = EditorGUILayout.ToggleLeft("미등록만 보기", unregisteredOnly, GUILayout.Width(100));
        EditorGUILayout.EndHorizontal();
    }

    //필터 통과 여부 (캐시된 rows만 검사 — 파일 접근 없음)
    private bool PassFilter(Row r)
    {
        if (kindFilter == 1 && r.kind != SpineKind.LD) return false;
        if (kindFilter == 2 && r.kind != SpineKind.SD) return false;
        if (kindFilter == 3 && r.kind != SpineKind.Unknown) return false;
        if (characterFilter > 0 && r.characterId != characterOptions[characterFilter]) return false;
        if (!string.IsNullOrEmpty(search) && r.key.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
        if (unregisteredOnly && r.status == Status.Registered) return false;
        return true;
    }

    private void DrawList()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        foreach (var group in rows.GroupBy(r => r.characterId))
        {
            var visible = group.Where(PassFilter).ToList();
            if (visible.Count == 0) continue;

            int unregistered = group.Count(r => r.status != Status.Registered);
            if (!foldouts.TryGetValue(group.Key, out bool open)) open = true;
            foldouts[group.Key] = EditorGUILayout.Foldout(open, $"{group.Key}  (미등록·확인 필요 {unregistered} / 전체 {group.Count()})", true);
            if (!foldouts[group.Key]) continue;

            foreach (Row r in visible)
            {
                EditorGUILayout.BeginHorizontal("box");
                GUI.enabled = r.selectable;
                r.selected = EditorGUILayout.Toggle(r.selected && r.selectable, GUILayout.Width(18));
                GUI.enabled = true;
                EditorGUILayout.LabelField(r.kind == SpineKind.Unknown ? "?" : r.kind.ToString(), GUILayout.Width(26));
                EditorGUILayout.LabelField(new GUIContent(r.key, r.note), GUILayout.MinWidth(240));
                EditorGUILayout.LabelField(new GUIContent(StatusLabel(r), r.note), GUILayout.Width(150));
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawFooter()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("보이는 항목 선택", GUILayout.Width(120)))
            foreach (Row r in rows.Where(PassFilter)) r.selected = r.selectable;
        if (GUILayout.Button("전체 해제", GUILayout.Width(80)))
            foreach (Row r in rows) r.selected = false;
        GUILayout.FlexibleSpace();
        int count = rows.Count(r => r.selected && r.selectable);
        GUI.enabled = count > 0;
        if (GUILayout.Button($"▶ 선택 항목 적용 ({count})", GUILayout.Height(30), GUILayout.Width(190))) RunApply();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
    }

    private static string StatusLabel(Row r)
    {
        switch (r.status)
        {
            case Status.WaitingImport: return "Unity 가져오기 대기";
            case Status.Unregistered: return "미등록";
            case Status.Registered: return "등록됨";
            case Status.MissingFile: return "파일 없음 (인덱스만)";
            default: return "알 수 없음";
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // 스캔
    // ─────────────────────────────────────────────────────────────────────
    private void RunScan()
    {
        rows.Clear();
        //[LD 불러오기] 삭제: CGManager 찾기·spineEntries 키 목록 (씬과 무관하게 판정)
        HashSet<string> indexKeys = new HashSet<string>(LoadIndex().Select(e => e.Key), System.StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        CharacterDatabase.Reload();   //CharacterData.xml을 다시 읽어 캐릭터 검사
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (string folder in AssetDatabase.GetSubFolders(SPINE_RESOURCE_PATH))
        {
            string folderName = Path.GetFileName(folder);

            //키 = json 파일 이름 ∪ SkeletonData 이름 (json만 있으면 Unity 가져오기 대기)
            var keys = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (string json in Directory.GetFiles(folder, "*.json")) keys.Add(Path.GetFileNameWithoutExtension(json));
            var dataPaths = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:SkeletonDataAsset", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.EndsWith(SpineKeys.SkeletonDataSuffix)) continue;
                string key = name.Substring(0, name.Length - SpineKeys.SkeletonDataSuffix.Length);
                dataPaths[key] = path;
                keys.Add(key);
            }

            foreach (string key in keys)
            {
                seen.Add(key);
                var r = new Row { key = key, folder = folder, characterId = folderName };
                dataPaths.TryGetValue(key, out r.skeletonDataPath);

                if (!SpineKeys.TryParse(key, out r.info) || r.info.Kind == SpineKind.Unknown)
                {
                    r.kind = SpineKind.Unknown;
                    r.status = Status.Unknown;
                    r.note = r.info == null ? "키 형식이 {캐릭터}_{타입}_{enzaId}가 아님" : $"모르는 타입 '{r.info.Type}' (SpineKeys에 추가 필요)";
                    rows.Add(r);
                    continue;
                }

                r.kind = r.info.Kind;
                r.characterId = r.info.CharacterId;
                if (!string.Equals(r.info.CharacterId, folderName, System.StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"{key}: 키의 캐릭터({r.info.CharacterId})와 폴더({folderName})가 다름 — 게임은 Spine/{r.info.CharacterId}/에서 찾음");
                if (CharacterDatabase.Find(r.info.CharacterId) == null)
                    r.note = $"CharacterData.xml에 '{r.info.CharacterId}' 없음 (등록은 가능, 이름·아이콘 없음)";

                //[LD 불러오기] 수정: LD도 SD와 같은 기준 (인덱스에 있음 + 셰이더 적용)
                bool inIndex = indexKeys.Contains(key);
                if (r.skeletonDataPath == null) r.status = Status.WaitingImport;
                else r.status = inIndex && IsShaderApplied(r) ? Status.Registered : Status.Unregistered;

                r.selectable = r.status == Status.Unregistered;
                rows.Add(r);
            }
        }

        //인덱스에는 있는데 파일이 없는 항목
        foreach (string key in indexKeys.Where(k => !seen.Contains(k)))
        {
            SpineKeys.TryParse(key, out SpineKeyInfo info);
            rows.Add(new Row
            {
                key = key, info = info, kind = info?.Kind ?? SpineKind.Unknown,
                characterId = info?.CharacterId ?? "?", status = Status.MissingFile,
                note = "SkeletonData 없음 → [인덱스 다시 만들기]로 정리",
            });
        }

        rows.Sort((a, b) => string.CompareOrdinal(a.characterId + "/" + a.key, b.characterId + "/" + b.key));
        characterOptions = new[] { "전체" }.Concat(rows.Select(r => r.characterId).Distinct()).ToArray();
        if (characterFilter >= characterOptions.Length) characterFilter = 0;

        int pending = rows.Count(r => r.status == Status.Unregistered);
        int waiting = rows.Count(r => r.status == Status.WaitingImport);
        int missing = rows.Count(r => r.status == Status.MissingFile);
        int unknown = rows.Count(r => r.status == Status.Unknown);
        var log = new StringBuilder($"전체 {rows.Count} · 미등록 {pending} · 가져오기 대기 {waiting} · 파일 없음 {missing} · 알 수 없음 {unknown}");
        foreach (string w in warnings) log.Append("\n⚠ ").Append(w);
        scanLog = log.ToString();
        foreach (string w in warnings) Debug.LogWarning($"[SpineRegister] {w}");
        Repaint();
    }

    private static string MaterialPath(Row r) => $"{r.folder}/{r.key}_Material.mat";

    private static bool IsShaderApplied(Row r)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(r));
        return mat != null && mat.shader != null && mat.shader.name == SHADER_NAME;
    }

    // ─────────────────────────────────────────────────────────────────────
    // 적용 (선택 항목 등록)
    // ─────────────────────────────────────────────────────────────────────
    private void RunApply()
    {
        List<Row> targets = rows.Where(r => r.selected && r.selectable).ToList();
        if (targets.Count == 0) return;

        Shader shader = Shader.Find(SHADER_NAME);
        if (shader == null) Debug.LogWarning($"[SpineRegister] 셰이더를 찾을 수 없습니다: {SHADER_NAME}");

        //[LD 불러오기] 삭제: LD용 CommunicationScene(Canvas > CG, CGManager) 확인 · 씬 오브젝트 생성 · 씬 dirty 처리
        var registered = new List<SpineKeyInfo>();
        foreach (Row r in targets)
        {
            ApplyShader(r, shader);   //① 셰이더 (LD·SD 공통)
            registered.Add(r.info);
            Debug.Log($"[SpineRegister] 등록: [{r.kind}] {r.key}");
        }

        //② 인덱스 기록 (기존 + 이번 등록분)
        var index = LoadIndex().ToDictionary(e => e.Key, System.StringComparer.OrdinalIgnoreCase);
        foreach (SpineKeyInfo info in registered) index[info.Key] = info;
        WriteIndex(index.Values);

        int capturedCount = registered.Count;
        EditorApplication.delayCall += () =>   //에셋 저장 직후 다시 스캔 (기존 방식 유지)
        {
            RunScan();
            scanLog = $"✅ 등록 {capturedCount}개\n" + scanLog;
            Repaint();
        };
    }

    private static void ApplyShader(Row r, Shader shader)
    {
        if (shader == null) return;
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(r));
        if (mat == null) { Debug.LogWarning($"[SpineRegister] 머티리얼 없음: {MaterialPath(r)}"); return; }
        if (mat.shader == shader) return;
        mat.shader = shader;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mat);
    }

    //[LD 불러오기] 삭제: CreateLDObject (Canvas > CG에 SkeletonAnimation 생성 + spineEntries 등록) → CGManager.TryCreateSpine이 실행 중에 생성

    // ─────────────────────────────────────────────────────────────────────
    // 인덱스 다시 만들기 — 실제 상태 기준으로 처음부터 다시 씀
    //   [LD 불러오기] 수정: LD·SD 공통 — 셰이더가 적용돼 있고 SkeletonData가 있음 (이전: LD는 spineEntries 기준, 씬이 닫혀 있으면 기존 인덱스 유지)
    // ─────────────────────────────────────────────────────────────────────
    private void RebuildIndex()
    {
        RunScan();
        var entries = new List<SpineKeyInfo>();

        foreach (Row r in rows)
        {
            if (r.info == null || r.skeletonDataPath == null || r.kind == SpineKind.Unknown) continue;
            if (IsShaderApplied(r)) entries.Add(r.info);
        }

        WriteIndex(entries);
        RunScan();
        int ld = entries.Count(e => e.Kind == SpineKind.LD), sd = entries.Count(e => e.Kind == SpineKind.SD);
        scanLog = $"✅ 인덱스 다시 만들기: LD {ld} · SD {sd}\n" + scanLog;
        Debug.Log($"[SpineRegister] 인덱스 다시 만들기: LD {ld} · SD {sd}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 인덱스 파일 읽기·쓰기
    // ─────────────────────────────────────────────────────────────────────
    private static List<SpineKeyInfo> LoadIndex()
    {
        if (!File.Exists(CharacterSpineData.AssetPath)) return new List<SpineKeyInfo>();
        return CharacterSpineData.Parse(File.ReadAllText(CharacterSpineData.AssetPath, Encoding.UTF8));
    }

    private static void WriteIndex(IEnumerable<SpineKeyInfo> entries)
    {
        string text = CharacterSpineData.Serialize(entries);
        if (File.Exists(CharacterSpineData.AssetPath) && File.ReadAllText(CharacterSpineData.AssetPath, Encoding.UTF8) == text) return;   //같으면 쓰지 않음 (불필요한 diff 방지)
        File.WriteAllText(CharacterSpineData.AssetPath, text, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(CharacterSpineData.AssetPath);
        CharacterDatabase.Reload();
    }

    //[LD 불러오기] 삭제: 씬 헬퍼 (FindCGManager·FindChildIgnoreCase·GetRegisteredKeys·RegisterToCGManager) — LD 등록이 씬을 쓰지 않음
}
