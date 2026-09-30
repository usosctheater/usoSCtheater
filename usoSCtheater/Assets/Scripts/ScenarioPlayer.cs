using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using UsoSCTheater.Scenario;   //[시나리오] 선택값(ScenarioSelection), 막 XML(ActXml)

//[용어 정리] SceneManager → ScenarioPlayer
//시나리오(폴더 1개) 안의 막(Act XML)을 순서대로 재생한다. Unity 씬 전환은 이 클래스의 역할이 아님
//용어: Scene = Unity 씬 / Scenario = 이야기 한 편(폴더) / Act = 막(XML 1개) / 장 = 막 안의 TRANSITION 구간(주석 전용)
public class ScenarioPlayer : MonoBehaviour
{

    [Header("매니저 연결")]   
    [SerializeField] private DialogManager dialogManager;
    [SerializeField] UIManager uiManager;
    [SerializeField] BGManager bgManager;
    [SerializeField] CGManager cgManager;
    [SerializeField] AudioManager audioManager;   //[시나리오] 시나리오별 보이스 폴더 지정용

    [Header("기본 시나리오")]
    //[시나리오] 목록 씬을 거치지 않고 CommunicationScene을 직접 Play할 때만 사용 (선택값이 있으면 무시)
    //[용어 정리] scenePath("Scene/NKS") → defaultScenarioFolder("NKS"). 기존 인스펙터 값 호환을 위해 마지막 경로 조각만 사용
    [UnityEngine.Serialization.FormerlySerializedAs("scenePath")]
    [SerializeField] private string defaultScenarioFolder = "NKS";

    private List<TextAsset> actFiles = new List<TextAsset>();
    private int currentActIndex = 0;
    private string activeScenarioFolder;   //[시나리오] 실제 재생 시나리오 폴더명 (선택값 우선 → 인스펙터 기본값). 인스펙터 값은 덮어쓰지 않음
    private string activeScenarioPath;     //[시나리오] Resources 로드 경로 (예: "Scene/IL")

    void Start()
    {
        ResolveScenario();   //[시나리오]
        LoadActFiles();

        if (actFiles.Count == 0) return;   //[시나리오] 막 없으면 PlayCurrentAct 인덱스 오류 방지

        ApplyStartAct();   //[시나리오]

        //[시나리오] 시나리오 폴더명을 보이스 폴더로 지정
        if (audioManager != null) audioManager.SetScenarioVoiceFolder(activeScenarioFolder);
        else Debug.LogWarning("[ScenarioPlayer] AudioManager 미연결 — 보이스는 Voice 루트에서만 검색합니다.");

        PlayCurrentAct();
    }

    //[시나리오] 재생할 시나리오 결정: 목록 씬 선택값 우선, 없으면 인스펙터 기본 시나리오
    private void ResolveScenario()
    {
        if (ScenarioSelection.HasSelection)
        {
            activeScenarioFolder = ScenarioSelection.ScenarioFolder;
            Debug.Log($"[ScenarioPlayer] 선택된 시나리오: {activeScenarioFolder} (시작 막: {ScenarioSelection.StartActName ?? "처음부터"})");
        }
        else
        {
            activeScenarioFolder = GetLastPathSegment(defaultScenarioFolder);
            Debug.Log($"[ScenarioPlayer] 선택값 없음 → 인스펙터 기본 시나리오 사용: {activeScenarioFolder}");
        }
        activeScenarioPath = ScenarioSelection.GetScenarioPath(activeScenarioFolder);
    }

    //[시나리오] 시작 막 지정 (파일명 기준, 못 찾으면 첫 막)
    private void ApplyStartAct()
    {
        string start = ScenarioSelection.StartActName;
        if (string.IsNullOrEmpty(start)) return;

        int index = actFiles.FindIndex(f => f.name == start);
        if (index < 0)
        {
            Debug.LogWarning($"[ScenarioPlayer] 시작 막 '{start}'을(를) {activeScenarioPath}에서 찾지 못해 첫 막부터 재생합니다.");
            return;
        }
        currentActIndex = index;
    }

    //[시나리오] "Scene/IL" → "IL", "IL" → "IL"
    private static string GetLastPathSegment(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return path.TrimEnd('/').Split('/')[^1];
    }

    void Update()
    {
        //디버그용 막 직행 기능 (PageUp: 다음 막, PageDown: 이전 막)
        if (Input.GetKeyDown(KeyCode.PageUp)) DebugNextAct();
        else if (Input.GetKeyDown(KeyCode.PageDown)) DebugPreviousAct();
        else if (Input.GetKeyDown(KeyCode.Backspace)) dialogManager.DebugPrevLine();
    }

    private void LoadActFiles()
    {
        actFiles.Clear();

        TextAsset[] files = Resources.LoadAll<TextAsset>(activeScenarioPath);

        if (files.Length == 0)
        {
            Debug.LogError($"[ScenarioPlayer] {activeScenarioPath} 경로에 막 파일이 없습니다.");
            return;
        }
        
        //파일명 기준으로 오름차순 정렬
        System.Array.Sort(files, (a,b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        actFiles.AddRange(files);
        Debug.Log($"[ScenarioPlayer] 총 {actFiles.Count}개 막 파일 로드 완료");

        foreach (TextAsset file in actFiles) Debug.Log($"[ScenarioPlayer] 막 등록: {file.name}");
    }

    //DialogManager에서 막 종료 시 호출
    public void OnActEnd()
    {
        currentActIndex++;

        if (currentActIndex < actFiles.Count) PlayCurrentAct();
        else UnityEngine.SceneManagement.SceneManager.LoadScene("EndingScene");
    }

    private void PlayCurrentAct()
    {
        TextAsset actFile = actFiles[currentActIndex];
        Debug.Log($"[ScenarioPlayer] 막 시작: {actFile.name}");
        
        //Title 파싱 - mainTitle / subTitle로 분리
        XmlDocument doc = new XmlDocument();
        doc.LoadXml(actFile.text);
        XmlNode actNode = ActXml.GetRoot(doc);   //[용어 정리] <Act> 우선, 기존 <Scene> 호환
        string mainTitle = actNode?.Attributes["mainTitle"]?.Value ?? "";
        string subTitle  = actNode?.Attributes["subTitle"]?.Value  ?? "";

        //Title 표시
        if (!string.IsNullOrEmpty(mainTitle) || !string.IsNullOrEmpty(subTitle))
            uiManager.ShowActTitle(mainTitle, subTitle);
        
        dialogManager.LoadAct(actFile);
    }

    //디버그용: 다음 막으로 즉시 이동 (마지막 막이면 무시)
    private void DebugNextAct()
    {
        if (currentActIndex + 1 >= actFiles.Count) return;

        currentActIndex++;
        dialogManager.DebugResetState();
        PlayCurrentAct();
    }

    //디버그용: 이전 막으로 즉시 이동 (첫 막이면 무시)
    private void DebugPreviousAct()
    {
        if (currentActIndex - 1 < 0) return;

        currentActIndex--;
        dialogManager.DebugResetState();
        PlayCurrentAct();
    }

}
