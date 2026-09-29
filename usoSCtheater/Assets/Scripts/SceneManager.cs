using System.Collections.Generic;
using System.Xml;
using UnityEngine;

public class SceneManager : MonoBehaviour
{

    [Header("매니저 연결")]   
    [SerializeField] private DialogManager dialogManager;
    [SerializeField] UIManager uiManager;
    [SerializeField] BGManager bgManager;
    [SerializeField] CGManager cgManager;
    [SerializeField] AudioManager audioManager;   //[시나리오] 시나리오별 보이스 폴더 지정용

    [Header("씬 파일 경로")]
    [SerializeField] private string scenePath = "Scene/NKS";   //[시나리오] 기본값 main → NKS (실제 값은 인스펙터)

    private List<TextAsset> sceneFiles = new List<TextAsset>();
    private int currentSceneIndex = 0;

    void Start()
    {
        LoadSceneFiles();

        //[시나리오] 시나리오 폴더명(scenePath 마지막 조각)을 보이스 폴더로 지정
        if (audioManager != null) audioManager.SetScenarioVoiceFolder(GetScenarioFolderName());
        else Debug.LogWarning("[SceneManager] AudioManager 미연결 — 보이스는 Voice 루트에서만 검색합니다.");

        PlayNextScene();
    }

    //[시나리오] "Scene/IL" → "IL"
    private string GetScenarioFolderName()
    {
        if (string.IsNullOrEmpty(scenePath)) return "";
        return scenePath.TrimEnd('/').Split('/')[^1];
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

        TextAsset[] files = Resources.LoadAll<TextAsset>(scenePath);

        if (files.Length == 0)
        {
            Debug.LogError($"[SceneManager] {scenePath} 경로에 씬 파일이 없습니다.");
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
