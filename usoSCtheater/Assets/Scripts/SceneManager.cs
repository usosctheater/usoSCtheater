using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using UsoSCTheater.Scenario;   //[시나리오] 선택값(ScenarioSelection)

public class SceneManager : MonoBehaviour
{

    [Header("매니저 연결")]   
    [SerializeField] private DialogManager dialogManager;
    [SerializeField] UIManager uiManager;
    [SerializeField] BGManager bgManager;
    [SerializeField] CGManager cgManager;
    [SerializeField] AudioManager audioManager;   //[시나리오] 시나리오별 보이스 폴더 지정용

    [Header("씬 파일 경로")]
    //[시나리오] 목록 씬을 거치지 않고 main.unity를 직접 Play할 때만 사용 (선택값이 있으면 무시)
    [SerializeField] private string scenePath = "Scene/NKS";

    private List<TextAsset> sceneFiles = new List<TextAsset>();
    private int currentSceneIndex = 0;
    private string activeScenePath;   //[시나리오] 실제 로드 경로 (선택값 우선 → 인스펙터 scenePath). 인스펙터 값은 덮어쓰지 않음

    void Start()
    {
        ResolveScenePath();   //[시나리오]
        LoadSceneFiles();

        if (sceneFiles.Count == 0) return;   //[시나리오] 씬 없으면 PlayNextScene 인덱스 오류 방지

        ApplyStartScene();   //[시나리오]

        //[시나리오] 시나리오 폴더명(activeScenePath 마지막 조각)을 보이스 폴더로 지정
        if (audioManager != null) audioManager.SetScenarioVoiceFolder(GetScenarioFolderName());
        else Debug.LogWarning("[SceneManager] AudioManager 미연결 — 보이스는 Voice 루트에서만 검색합니다.");

        PlayNextScene();
    }

    //[시나리오] 로드 경로 결정: 목록 씬 선택값 우선, 없으면 인스펙터 scenePath
    private void ResolveScenePath()
    {
        if (ScenarioSelection.HasSelection)
        {
            activeScenePath = ScenarioSelection.GetScenePath(ScenarioSelection.ScenarioFolder);
            Debug.Log($"[SceneManager] 선택된 시나리오: {ScenarioSelection.ScenarioFolder} (시작 씬: {ScenarioSelection.StartSceneName ?? "처음부터"})");
        }
        else
        {
            activeScenePath = scenePath;
            Debug.Log($"[SceneManager] 선택값 없음 → 인스펙터 scenePath 사용: {scenePath}");
        }
    }

    //[시나리오] 시작 씬 지정 (파일명 기준, 못 찾으면 첫 씬)
    private void ApplyStartScene()
    {
        string start = ScenarioSelection.StartSceneName;
        if (string.IsNullOrEmpty(start)) return;

        int index = sceneFiles.FindIndex(f => f.name == start);
        if (index < 0)
        {
            Debug.LogWarning($"[SceneManager] 시작 씬 '{start}'을(를) {activeScenePath}에서 찾지 못해 첫 씬부터 재생합니다.");
            return;
        }
        currentSceneIndex = index;
    }

    //[시나리오] "Scene/IL" → "IL"
    private string GetScenarioFolderName()
    {
        if (string.IsNullOrEmpty(activeScenePath)) return "";   //[시나리오] scenePath → activeScenePath
        return activeScenePath.TrimEnd('/').Split('/')[^1];
    }

    void Update()
    {
        //디버그용 씬 직행 기능 (PageUp: 다음 씬, PageDown: 이전 씬)
        if (Input.GetKeyDown(KeyCode.PageUp)) DebugNextScene();
        else if (Input.GetKeyDown(KeyCode.PageDown)) DebugPreviousScene();
        else if (Input.GetKeyDown(KeyCode.Backspace)) dialogManager.DebugPrevLine();
    }

    private void LoadSceneFiles()
    {
        sceneFiles.Clear();

        //[시나리오] scenePath → activeScenePath (아래 로그 포함)
        TextAsset[] files = Resources.LoadAll<TextAsset>(activeScenePath);

        if (files.Length == 0)
        {
            Debug.LogError($"[SceneManager] {activeScenePath} 경로에 씬 파일이 없습니다.");
            return;
        }
        
        //파일명 기준으로 오름차순 정렬
        System.Array.Sort(files, (a,b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        sceneFiles.AddRange(files);
        Debug.Log($"[SceneManager] 총 {sceneFiles.Count}개 씬 파일 로드 완료");

        foreach (TextAsset file in sceneFiles) Debug.Log($"[SceneManager] 씬 등록: {file.name}");
    }

    //DialogManager에서 씬 종료 시 호출
    public void OnSceneEnd()
    {
        currentSceneIndex++;

        if (currentSceneIndex < sceneFiles.Count) PlayNextScene();
        else UnityEngine.SceneManagement.SceneManager.LoadScene("EndingScene");
    }

    private void PlayNextScene()
    {
        TextAsset nextFile = sceneFiles[currentSceneIndex];
        Debug.Log($"[SceneManager] 씬 시작: {nextFile.name}");
        
        //Title 파싱 - mainTitle / subTitle로 분리
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(nextFile.text);
        XmlNode sceneNode = doc.SelectSingleNode("Scene");
        string mainTitle = sceneNode?.Attributes["mainTitle"]?.Value ?? "";
        string subTitle  = sceneNode?.Attributes["subTitle"]?.Value  ?? "";

        //Title 표시
        if (!string.IsNullOrEmpty(mainTitle) || !string.IsNullOrEmpty(subTitle))
            uiManager.ShowSceneTitle(mainTitle, subTitle);
        
        dialogManager.LoadScene(nextFile);
    }

    //디버그용: 다음 씬으로 즉시 이동 (마지막 씬이면 무시)
    private void DebugNextScene()
    {
        if (currentSceneIndex + 1 >= sceneFiles.Count) return;

        currentSceneIndex++;
        dialogManager.DebugResetState();
        PlayNextScene();
    }

    //디버그용: 이전 씬으로 즉시 이동 (첫 씬이면 무시)
    private void DebugPreviousScene()
    {
        if (currentSceneIndex - 1 < 0) return;

        currentSceneIndex--;
        dialogManager.DebugResetState();
        PlayNextScene();
    }

}
