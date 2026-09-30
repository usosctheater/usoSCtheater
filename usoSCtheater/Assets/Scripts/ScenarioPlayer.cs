using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using UsoSCTheater.Scenario;   //[시나리오] 선택값(ScenarioSelection), 막 XML(ActXml)
using UsoSCTheater.SceneFlow;  //[씬 전환] SceneTransitionManager
using UsoSCTheater.Recording;  //[녹화] 재생 시작/종료 신호

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
    private string activeScenarioPath;     //[시나리오] Resources 로드 경로 (예: "Scenario/IL")
    private bool isExiting = false;        //[강제 종료] ESC 중복 입력 방지

    void Start()
    {
        ResolveScenario();   //[시나리오]
        LoadActFiles();

        if (actFiles.Count == 0) return;   //[시나리오] 막 없으면 PlayCurrentAct 인덱스 오류 방지

        ApplyStartAct();   //[시나리오]

        //[시나리오] 시나리오 폴더명을 보이스 폴더로 지정
        if (audioManager != null) audioManager.SetScenarioVoiceFolder(activeScenarioFolder);
        else Debug.LogWarning("[ScenarioPlayer] AudioManager 미연결 — 보이스는 Voice 루트에서만 검색합니다.");

        //[녹화] 재생 시작 신호 (녹화 모드일 때만 녹화 도구가 녹화 시작 + 자동재생 On). 모두 재생이면 막 이름 null
        RecordingSignal.RequestStart(activeScenarioFolder, ScenarioSelection.IsPlayAll ? null : actFiles[currentActIndex].name);

        PlayCurrentAct();
    }

    //[시나리오] 재생할 시나리오 결정: 목록 씬 선택값 우선, 없으면 인스펙터 기본 시나리오
    private void ResolveScenario()
    {
        if (ScenarioSelection.HasSelection)
        {
            activeScenarioFolder = ScenarioSelection.ScenarioFolder;
            Debug.Log($"[ScenarioPlayer] 선택된 시나리오: {activeScenarioFolder} (재생 범위: {ScenarioSelection.StartActName ?? "모두 재생"})");
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
        //[강제 종료] ESC: 재생 중단 → 녹화 종료 신호 → 목록 씬
        if (Input.GetKeyDown(KeyCode.Escape)) { ForceReturnToScenarioSelect(); return; }

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
        //[재생 범위] 단일 막 재생: 해당 막 종료 → 녹화 종료 신호 → 목록 씬 (PageUp/Down으로 막을 옮긴 경우도 동일)
        if (!ScenarioSelection.IsPlayAll)
        {
            Debug.Log($"[ScenarioPlayer] 단일 막 재생 종료 ({actFiles[currentActIndex].name}) → 목록 씬");
            isExiting = true;
            RecordingSignal.RequestStop();
            SceneTransitionManager.GoToScenarioSelectScene();
            return;
        }

        //[재생 범위] 모두 재생: 다음 막, 마지막 막이면 엔딩 씬 (녹화 종료는 엔딩 종료 시점)
        currentActIndex++;

        if (currentActIndex < actFiles.Count) PlayCurrentAct();
        else SceneTransitionManager.GoToEndingScene();   //[씬 전환] UnityEngine LoadScene("EndingScene") → 공통 매니저 경유
    }

    //[강제 종료] ESC: 진행 중인 타이핑/자동재생/트랜지션/오디오 정리 → 녹화 종료 신호 → 목록 씬
    private void ForceReturnToScenarioSelect()
    {
        if (isExiting) return;
        isExiting = true;

        Debug.Log("[ScenarioPlayer] ESC 강제 종료 → 목록 씬");
        dialogManager.DebugResetState();
        if (audioManager != null)
        {
            audioManager.StopVoice();
            audioManager.StopAllAudio();
        }

        RecordingSignal.RequestStop();
        SceneTransitionManager.GoToScenarioSelectScene();
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
