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

    [Header("씬 파일 경로")]
    [SerializeField] private string scenePath = "Scene";

    private List<TextAsset> sceneFiles = new List<TextAsset>();
    private int currentSceneIndex = 0;

    void Start()
    {
        LoadSceneFiles();
        PlayNextScene();
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
        else Debug.Log("[SceneManager] 모든 씬 종료");                  //추후 엔딩 처리 등 기능 추가 예정
    }

    private void PlayNextScene()
    {
        TextAsset nextFile = sceneFiles[currentSceneIndex];
        Debug.Log($"[SceneManager] 씬 시작: {nextFile.name}");
        
        //Title 파싱
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(nextFile.text);
        XmlNode sceneNode = doc.SelectSingleNode("Scene");
        string title = sceneNode?.Attributes["title"]?.Value ?? "";

        //Title 표시
        if (!string.IsNullOrEmpty(title)) uiManager.ShowSceneTitle(title);
        
        dialogManager.LoadScene(nextFile);
    }

}
