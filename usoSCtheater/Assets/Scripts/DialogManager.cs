using System.Collections;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NUnit.Framework;
// using Unity.GraphToolkit.Editor;
using UnityEngine.EventSystems;
using UsoSCTheater.Recording; // [녹화] 에디터/빌드 페이싱 분기용
using UsoSCTheater.Capture;   // [캡처] TEXT 라인 자동 스크린샷
using UsoSCTheater.Scenario;  //[용어 정리] 막 XML 루트 파싱(ActXml)
// using UnityEditor.Audio;

public class DialogManager : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI dialogText;

    [Header("매니저 연결")]
    [SerializeField] private BGManager bgManager;
    [SerializeField] private CGManager cgManager;
    [SerializeField] private AudioManager audioManager;
    [UnityEngine.Serialization.FormerlySerializedAs("sceneManager")]   //[용어 정리] 인스펙터 연결 보존
    [SerializeField] private ScenarioPlayer scenarioPlayer;           //[용어 정리] SceneManager sceneManager → ScenarioPlayer scenarioPlayer
    [SerializeField] private EffectManager effectManager;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private ImageManager imageManager;

    [Header("캡처 설정")]
    [SerializeField] private Camera captureCamera;   // [캡처] 스크린샷 대상 카메라

    [Header("타이핑 설정")]
    [SerializeField] private float typingSpeed = 0.1f;          //글자 당 딜레이 (s)

    [Header("자동재생 설정")]
    [SerializeField] private float autoPlayTextCoeff = 0.05f;   //텍스트 길이 계수
    [SerializeField] private float autoPlayDelay = 0.3f;

    [Header("BREAK 설정")]
    [SerializeField] private float breakDuration = 1.0f;

    private List<DialogLine> lines = new List<DialogLine>();
    //대사/BGM/SE를 순서대로 담을 노드 리스트
    private List<ScriptNode> scriptNodes = new List<ScriptNode>();
    private int currentIndex = 0;
    private Coroutine typingCoroutine;                          //타이핑 효과용 코루틴
    private Coroutine autoPlayCoroutine;
    private bool isTyping = false;
    private bool isTransition = false;
    private bool isAutoPlay = false;
    
    //Lip 재사용 기능 (CGGroup 대응으로 단일 값 → List)
    private List<CGGroupEntry> lastLipTargets = new List<CGGroupEntry>();   //립싱크 재사용 대상 (cgKey / animation / speakerName)
    private string lastLineSpeakerName = null;                              //직전 CG 지정 라인의 Name (그룹이면 그룹 대사 Name → 전원 립싱크)

    //CGgroup 구현용 Dict
    private Dictionary<string, List<CGGroupEntry>> cgGroupDict = new Dictionary<string, List<CGGroupEntry>>();

    private string currentActName = "";   // [캡처] 캡처 폴더명으로 사용되는 현재 막 이름 //[용어 정리] currentSceneName → currentActName
    
    void Update()
    {
        //핫키 할당
        if (Input.GetKeyDown(KeyCode.F3)) uiManager.ToggleAutoPlay();

        if (Input.GetMouseButtonDown(0))
        {   
            if (EventSystem.current.IsPointerOverGameObject()) return;
            
            // UI 클릭 감지
            // PointerEventData pointerData = new PointerEventData(EventSystem.current);
            // pointerData.position = Input.mousePosition;

            // List<RaycastResult> results = new List<RaycastResult>();
            // EventSystem.current.RaycastAll(pointerData, results);

            // if (results.Count > 0) foreach (var result in results) Debug.Log($"[클릭 감지] 오브젝트 : {result.gameObject.name} / 레이어 : {result.gameObject.layer}");
            // else Debug.Log("감지된 UI 오브젝트 없음");

            //클릭 예외처리
            if (isTransition) return;
            if (uiManager.IsHidden) return;
            if (uiManager.IsBacklogOpen) return;

            if (isTyping) SkipTyping();
            else 
            {
                //자동재생 대기 중이면 취소 후 즉시 NextLine으로
                if (autoPlayCoroutine != null)
                {
                    StopCoroutine(autoPlayCoroutine);
                    autoPlayCoroutine = null;
                }

                ProcessNext();
            }
        }
    }

    public void LoadAct(TextAsset xmlAsset)   //[용어 정리] LoadScene → LoadAct
    {
        scriptNodes.Clear();
        currentIndex = 0;
        currentActName = xmlAsset.name;   // [캡처] 캡처 폴더명으로 사용

        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xmlAsset.text);

        //막 루트(<Act>, 기존 <Scene> 호환) 바로 아래 모든 Line 노드를 순서대로 처리
        //[용어 정리] "Scene/Line" → ActXml.GetRoot. 루트가 없으면 빈 목록(기존 동작과 동일)
        XmlNode actRoot = ActXml.GetRoot(doc);
        if (actRoot == null) Debug.LogError($"[DialogManager] {xmlAsset.name}: 루트 태그 <{ActXml.RootTag}>/<{ActXml.LegacyRootTag}>를 찾을 수 없습니다.");
        XmlNodeList lineNodes = actRoot != null ? actRoot.SelectNodes("Line") : doc.SelectNodes($"{ActXml.RootTag}/Line");

        foreach (XmlNode node in lineNodes)
        {
            string type = GetAttr(node, "Type").ToUpper();

            switch (type)
            {
                case "TEXT":
                    DialogLine line = new DialogLine();
                    line.name = GetAttr(node, "Name");
                    //Text 받을 때, \n 문자열을 실제 줄바꿈으로 변환
                    line.text = GetAttr(node, "Text").Replace("\\n", "\n");
                    line.cgKey = GetAttr(node, "CG");
                    line.cgPos = GetAttr(node, "Position");
                    line.animation = GetAttr(node, "Animation");
                    line.voiceKey = GetAttr(node, "Voice");
                    line.effect = GetAttr(node, "Effect").Trim().ToLower();
                    line.value = float.TryParse(GetAttr(node, "Value"), out float val) ? val : 1.0f;
                    line.duration = float.TryParse(GetAttr(node, "Duration"), out float dur) ? dur : 0f;
                    line.speakerType = int.TryParse(GetAttr(node, "SpeakerType"), out int st) ? st : 1;

                    scriptNodes.Add(ScriptNode.CreateLine(line));
                    break;

                case "AUDIO":
                    scriptNodes.Add(ScriptNode.CreateAudio(GetAttr(node, "Value").ToLower(), GetAttr(node, "Track"), float.TryParse(GetAttr(node, "Volume"), out float aVol) ? aVol : 1.0f, GetAttr(node, "Effect").ToLower()));
                    break;

                case "SE":
                    scriptNodes.Add(ScriptNode.CreateSE(GetAttr(node, "Track"), float.TryParse(GetAttr(node, "Volume"), out float seVol) ? seVol : 1.0f, float.TryParse(GetAttr(node, "Duration"), out float seDur) ? seDur : 0f));
                    break;

                case "TRANSITION":
                    scriptNodes.Add(ScriptNode.CreateTransition(GetAttr(node, "Effect"), GetAttr(node, "SE")));
                    break;

                case "BG":
                    scriptNodes.Add(ScriptNode.CreateBG(GetResourceKey(node), GetAttr(node, "Effect"), GetAttr(node, "Position"), float.TryParse(GetAttr(node, "Value"), out float bgVal) ? bgVal : 1.0f));
                    break;

                case "BREAK":
                    scriptNodes.Add(ScriptNode.CreateBreak(float.TryParse(GetAttr(node, "Duration"), out float breakDur) ? breakDur : 0f, GetAttr(node, "Effect").Trim().ToLower()));
                    break;

                case "CGGROUP":
                    // Name = "그룹명, 화자명" (첫 번째 쉼표만 구분자, 화자명 생략 가능 → Lip 재사용은 그룹 대사 Name으로만)
                    // 화자명은 TEXT의 Name과 글자 그대로 일치해야 Lip 재사용 대상이 됨
                    string[] groupNameParts = GetAttr(node, "Name").Split(new[] { ',' }, 2);
                    string groupName = groupNameParts[0].Trim();
                    string groupSpeaker = groupNameParts.Length > 1 ? groupNameParts[1].Trim() : "";
                    if (!cgGroupDict.ContainsKey(groupName)) cgGroupDict[groupName] = new List<CGGroupEntry>();

                    cgGroupDict[groupName].Add(new CGGroupEntry(GetAttr(node, "CG"), GetAttr(node, "Position"), GetAttr(node, "Animation"), groupSpeaker));
                    break;

                case "SETCG":
                    scriptNodes.Add(ScriptNode.CreateSetCG(GetAttr(node, "CG"), GetAttr(node, "Position"), GetAttr(node, "Animation"), GetAttr(node, "Effect").Trim().ToLower(), float.TryParse(GetAttr(node, "Value"), out float setCgVal) ? setCgVal : 1.0f, float.TryParse(GetAttr(node, "Duration"), out float setCgDur) ? setCgDur : 0f));
                    break;

                case "IMAGE":
                    scriptNodes.Add(ScriptNode.CreateImage(GetResourceKey(node), float.TryParse(GetAttr(node, "Duration"), out float imgDur) ? imgDur : -1f));
                    break;

                default:
                    Debug.LogWarning($"[DialogManager] 알 수 없는 타입: {type}");
                    break;
            }

        }

        Debug.Log($"총 {scriptNodes.Count}개 노드 로드 완료");

        //로드 완료 후에 자동으로 첫번째 Line을 출력
        ProcessNext();
    }

    private void ShowLine(DialogLine line)
    {
        
        //인자로 넘겨받는 구조로 변경
        // DialogLine line = lines[currentIndex];

        //텍스트 처리
        nameText.text = line.name;
        // 코루틴 처리로 변경해서 기존 처리 라인 주석
        // dialogText.text = line.text;

        //화자에 따라 대화창 색 동기화
        uiManager.SetDialogBoxType(line.speakerType);

        //이전 타이핑 코루틴 진행중이라면 중단시킴
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypeText(line));

        //리소스 처리
        // CG - None일 경우
        if (line.cgKey.ToLower() == "none")
        {
            Debug.LogWarning($"[DialogManager] TEXT 타입에서 CG=none 사용 감지 (Name: {line.name}) - SETCG 타입 사용을 권장합니다.");
            cgManager.ClearAllCGState();
            ResetLipTracking();
        }
        // CG - 키가 있을 때만
        else if (!string.IsNullOrEmpty(line.cgKey))
        {
            //CG 키가 CGGroup인 경우
            if (cgGroupDict.ContainsKey(line.cgKey))
            {
                foreach (var entry in cgGroupDict[line.cgKey]) cgManager.SetCG(entry.cgKey, entry.cgPos, entry.animation, GetVoiceDuration(line.voiceKey));

                //Lip 재사용 정보 저장: 그룹 멤버 전원 (원본 참조가 아닌 복사본으로 저장)
                lastLipTargets = new List<CGGroupEntry>(cgGroupDict[line.cgKey]);
                lastLineSpeakerName = line.name;
            }
            //그 외에는 기존 단일 CG 처리
            else
            {
                cgManager.SetCG(line.cgKey, line.cgPos, line.animation, GetVoiceDuration(line.voiceKey));
            
                //Lip 재사용 기능을 위한 정보 저장
                lastLipTargets = new List<CGGroupEntry> { new CGGroupEntry(line.cgKey, line.cgPos, line.animation, line.name) };
                lastLineSpeakerName = line.name;
            }
        }
        //CG 키가 없을 때: 직전 립싱크 대상 중 화자가 일치하는 CG가 있으면 Lip 재사용, 없으면 초기화
        else if (!RestartLipByName(line.name, GetVoiceDuration(line.voiceKey)))
        {
            ResetLipTracking();
        }
        
        //이펙트 처리
        // Effect = zoom일 경우
        // [TODO] CGGroup Zoom은 그룹 전체 Zoom으로 별도 기능 구현 필요
        if (line.effect == "zoom" && !string.IsNullOrEmpty(line.cgKey))
        {
            if (cgGroupDict.ContainsKey(line.cgKey)) Debug.LogWarning($"[DialogManager] CGGroup({line.cgKey})에 Zoom하는 기능은 별도 구현이 필요합니다.");
            else cgManager.SetZoom(line.cgKey, line.cgPos, line.value, line.duration);
        }

        //보이스 재생
        audioManager.PlayVoice(line.voiceKey);

        //자동재생 코루틴 시작
        if (isAutoPlay)
        {
            if (autoPlayCoroutine != null) StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = StartCoroutine(AutoPlayCoroutine(line));
        }
    }

    //노드 순차 처리하는 함수 (노드 다양화로 Line 이외에도 처리하게 변경)
    private void ProcessNext()
    {
        while(currentIndex < scriptNodes.Count)
        {
            ScriptNode node = scriptNodes[currentIndex];
            currentIndex++; 

            switch (node.type)
            {
                case ScriptNode.NodeType.Audio:
                    if (node.audioEffect == "stop") audioManager.StopAudio(node.audioSlot);
                    else audioManager.PlayAudio(node.audioSlot, node.track, node.volume, node.audioEffect == "loop");
                    continue;

                case ScriptNode.NodeType.SE:
                    audioManager.PlaySE(node.track, node.volume, node.seDuration);
                    continue;

                case ScriptNode.NodeType.BG:
                    bgManager.SetBG(node.bg);

                    //만약 이펙트가 부여되어 있으면 적용
                    if (node.bgEffect.ToLower() == "flashback") bgManager.SetFlashback();
                    else if (node.bgEffect.ToLower() == "zoom") bgManager.setZoom(node.zoomPos, node.zoomValue);
                    continue;

                case ScriptNode.NodeType.SetCG:
                    ProcessSetCG(node);
                    continue;

                case ScriptNode.NodeType.Image:
                    imageManager.ShowImage(node.imageKey, node.imageDuration);
                    continue;

                case ScriptNode.NodeType.Transition:
                    isTransition = true;
                    // normal 트랜지션이면 BGM 유지
                    bool isNormalTransition = node.transition_effect.ToLower() == "normal";
                    effectManager.PlayTransition(node.transition_effect, node.transition_se, ()=> {
                        ClearScreen(stopBGM: !isNormalTransition);   //[용어 정리] ClearScene → ClearScreen
                        isTransition = false;
                        ProcessNext();
                    });
                    return;

                case ScriptNode.NodeType.Break:
                    // clean flag일 때만 DialogBox 숨김
                    uiManager.SetDialogBoxVisible(node.breakEffect != "clean");

                    float waitDur = node.breakDuration > 0f ? node.breakDuration : breakDuration;
                    if (autoPlayCoroutine != null) StopCoroutine(autoPlayCoroutine);
                    autoPlayCoroutine = StartCoroutine(BreakWaitCoroutine(waitDur));
                    return;

                case ScriptNode.NodeType.Line:
                    uiManager.SetDialogBoxVisible(node.line.speakerType != 0);                // TEXT 진입 시 sType = 0이 아니라면 표시
                    ShowLine(node.line);
                    return;
            }
        }

        ClearScreen();   //[용어 정리] ClearScene → ClearScreen
        //Debug.Log("막 종료");
        Debug.Log($"[DialogManager] 막 종료 — ScenarioPlayer에 전달");
        scenarioPlayer.OnActEnd();   //[용어 정리] sceneManager.OnSceneEnd → scenarioPlayer.OnActEnd
    }

    // 화자 Name으로 Lip 재사용 대상 탐색 후 립싱크 재시작. 하나라도 재생했으면 true
    // - 직전 CG 지정 라인과 Name이 같으면 대상 전원 (단일 CG / 그룹 전원 대사)
    // - 아니면 CGGROUP 화자명이 일치하는 멤버만 (화자명 미지정 멤버는 제외 → 빈 Name 나레이션과 오매칭 방지)
    private bool RestartLipByName(string speakerName, float voiceDuration)
    {
        if (lastLipTargets.Count == 0) return false;

        bool matchAll = speakerName == lastLineSpeakerName;
        bool restarted = false;

        foreach (var target in lastLipTargets)
        {
            if (!matchAll && (string.IsNullOrEmpty(target.speakerName) || target.speakerName != speakerName)) continue;

            cgManager.RestartLipSync(target.cgKey, target.animation, voiceDuration);
            restarted = true;
        }
        return restarted;
    }

    // Lip 재사용 정보 초기화
    private void ResetLipTracking()
    {
        lastLipTargets.Clear();
        lastLineSpeakerName = null;
    }

    // SETCG 노드 처리: TEXT의 CG 세팅 로직을 재사용하되, 클릭 없이 즉시 다음 라인으로 진행
    private void ProcessSetCG(ScriptNode node)
    {
        // Effect = clean: CG 속성에 명시된 스파인만 제거 (트래킹 변수 미변경)
        if (node.setCgEffect == "clean")
        {
            if (cgGroupDict.ContainsKey(node.setCgKey))
            {
                foreach (var entry in cgGroupDict[node.setCgKey])
                {
                    cgManager.HideCG(entry.cgKey);
                    cgManager.ClearZoom(entry.cgKey);
                }
            }
            else
            {
                cgManager.HideCG(node.setCgKey);
                cgManager.ClearZoom(node.setCgKey);
            }
            return;
        }

        // CG = none: 전체 CG 제거 (기존 TEXT의 none 처리와 동일 - 트래킹 변수도 여기서만 초기화)
        if (node.setCgKey.ToLower() == "none")
        {
            cgManager.ClearAllCGState();
            ResetLipTracking();
            return;
        }

        // CG 키가 CGGroup인 경우
        // 주의: Lip 재사용 정보(lastLipTargets/lastLineSpeakerName)는 여기서 절대 건드리지 않음.
        if (cgGroupDict.ContainsKey(node.setCgKey))
        {
            foreach (var entry in cgGroupDict[node.setCgKey]) cgManager.SetCG(entry.cgKey, entry.cgPos, entry.animation, 0f);
        }
        // 단일 CG 처리
        else
        {
            cgManager.SetCG(node.setCgKey, node.setCgPos, node.setCgAnimation, 0f);
        }

        // Effect = zoom
        // [TODO] CGGroup Zoom은 그룹 전체 Zoom으로 별도 기능 구현 필요
        if (node.setCgEffect == "zoom")
        {
            if (cgGroupDict.ContainsKey(node.setCgKey)) Debug.LogWarning($"[DialogManager] CGGroup({node.setCgKey})에 Zoom하는 기능은 별도 구현이 필요합니다.");
            else cgManager.SetZoom(node.setCgKey, node.setCgPos, node.setCgValue, node.setCgDuration);
        }
    }

    // stopBGM: false이면 BGM을 정지하지 않음 (normal 트랜지션 등에서 BGM 유지 시 사용)
    //화면(CG/BG/텍스트 등) 초기화. 막 종료 및 장 전환(TRANSITION) 시 호출 //[용어 정리] ClearScene → ClearScreen
    private void ClearScreen(bool stopBGM = true)
    {
        //텍스트 초기화
        nameText.text = "";
        dialogText.text = "";

        //타이핑 코루틴 중단
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        isTyping = false;

        //리소스 초기화
        cgManager.ClearAllCGState();
        ResetLipTracking();     //CG가 모두 사라지므로 Lip 재사용 정보도 초기화 (숨겨진 CG에 립싱크 방지)
        bgManager.HideBG();
        bgManager.HideFlashback();
        bgManager.hideZoom();

        // normal 트랜지션 시 BGM 유지를 위해 조건부 정지
        if (stopBGM) audioManager.StopAllAudio();

        //안전을 위해서 CGGroupDict도 초기화
        cgGroupDict.Clear();
    }

    //속성이 없거나 비어있는 경우엔 빈 문자열 반환하는 함수
    private string GetAttr(XmlNode node, string key)
    {
        XmlAttribute attr = node.Attributes[key];
        return (attr != null) ? attr.Value : "";
    }

    // =====================================================================
    // [주의] 리소스 키 속성 하위 호환 처리 (2026-09-29)
    // BG / IMAGE의 리소스 키 속성명은 "ResourceKey"가 기본(신규 작성 시 사용).
    // 기존에 작성된 XML은 "Key"를 사용하므로, ResourceKey가 없으면 Key를 읽는다.
    // → 기존 XML(Key)과 신규 XML(ResourceKey) 양쪽 모두 동작함.
    // → 모든 XML이 ResourceKey로 전환되기 전까지 Key 폴백을 절대 제거하지 말 것.
    // =====================================================================
    private string GetResourceKey(XmlNode node)
    {
        string resourceKey = GetAttr(node, "ResourceKey");
        return !string.IsNullOrEmpty(resourceKey) ? resourceKey : GetAttr(node, "Key");
    }

    private IEnumerator TypeText(DialogLine line)
    {
        isTyping = true;
        dialogText.text = "";

        for (int i = 0; i < line.text.Length; i++)
        {
            dialogText.text = line.text.Substring(0, i + 1);

            //나중에 오디오 매니저 연결 시 여기서 타이핑 사운드 함수 호출

            //타이핑 효과 텀 설정 (어색하면 없애도 됨)
            yield return RecordingTimeUtil.PacingWait(typingSpeed);
        }

        isTyping = false;
        typingCoroutine = null;
    }

    private void SkipTyping()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        if (autoPlayCoroutine != null)
        {
            StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = null;
        }    

        // Break 노드에서는 텍스트 건드리지 않음
        if (scriptNodes[currentIndex - 1].type == ScriptNode.NodeType.Break) return;
        
        dialogText.text = scriptNodes[currentIndex - 1].line.text;

        isTyping = false;
    }

    public void SetAutoPlay(bool value)
    {
        isAutoPlay = value;

        //자동재생 꺼지면 대기 코루틴 중단
        if (!isAutoPlay && autoPlayCoroutine != null)
        {
            StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = null;
        }
        else
        {
            //자동재생 켜지면 현재 라인 즉시 자동재생 시작
            if (currentIndex > 0 && scriptNodes[currentIndex - 1].type == ScriptNode.NodeType.Line)
            {
                if (autoPlayCoroutine != null) StopCoroutine(autoPlayCoroutine);
                autoPlayCoroutine = StartCoroutine(AutoPlayCoroutine(scriptNodes[currentIndex - 1].line));
            }
            //Break 도중 AutoPlay 활성화 시, 기본 Duration 처리
            else if (currentIndex > 0 && scriptNodes[currentIndex - 1].type == ScriptNode.NodeType.Break)
            {
                float dur = scriptNodes[currentIndex - 1].breakDuration > 0f ? scriptNodes[currentIndex - 1].breakDuration : breakDuration;

                if (autoPlayCoroutine != null) StopCoroutine(autoPlayCoroutine);
                autoPlayCoroutine = StartCoroutine(BreakWaitCoroutine(dur));
            }
        }
    }

    private IEnumerator AutoPlayCoroutine(DialogLine line)
    {
        //텍스트 타이핑 시간 계산
        float typingDuration = line.text.Length * typingSpeed;
        float voiceDuration = GetVoiceDuration(line.voiceKey);

        //둘 중 더 긴 시간동안 대기
        float waitDuration = Mathf.Max(voiceDuration, typingDuration);
        yield return RecordingTimeUtil.PacingWait(waitDuration);

        //만약 타이핑이 아직 진행 중이면 완료될 때까지 대기
        while (isTyping) yield return null;

        // [캡처] 타이핑/보이스 중 더 늦게 끝난 시점 = 여기. 렌더링 완료 보장 후 캡처.
        yield return new WaitForEndOfFrame();
        ScreenCaptureUtil.CaptureLine(currentActName, currentIndex - 1, captureCamera);   //[용어 정리] SceneCaptureUtil → ScreenCaptureUtil

        //고정 딜레이 적용
        yield return RecordingTimeUtil.PacingWait(autoPlayDelay);

        autoPlayCoroutine = null;
        ProcessNext();
    }

    // Break 전용 대기 코루틴: Duration만큼 대기 후 자동 진행 (클릭 시 즉시 스킵은 Update()에서 처리)
    private IEnumerator BreakWaitCoroutine(float duration)
    {
        yield return RecordingTimeUtil.PacingWait(duration);

        //자동재생이 켜져 있으면 기존 TEXT와 동일하게 autoPlayDelay까지 추가 대기
        if (isAutoPlay) yield return RecordingTimeUtil.PacingWait(autoPlayDelay);

        autoPlayCoroutine = null;
        ProcessNext();
    }

    public List<ScriptNode> GetReadNodes()
    {
        return scriptNodes.GetRange(0, currentIndex);
    }

    private float GetVoiceDuration(string voiceKey)
    {
        //보이스 경로 규칙(시나리오 폴더 우선 → 루트 폴백)은 AudioManager에서 일괄 관리
        return audioManager.GetVoiceDuration(voiceKey);
    }

    //디버그용 기능
    //막 강제 전환 전 진행 중이던 타이핑/자동재생/트랜지션 상태 초기화
    public void DebugResetState()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        if (autoPlayCoroutine != null)
        {
            StopCoroutine(autoPlayCoroutine);
            autoPlayCoroutine = null;
        }

        isTyping = false;
        isTransition = false;

        ClearScreen();   //[용어 정리] ClearScene → ClearScreen
    }

    public void DebugPrevLine()
    {
        int searchFrom = currentIndex -2;

        for (int i = searchFrom; i >= 0; i--)
        {
            //Line 노드만 탐색해서
            if (scriptNodes[i].type != ScriptNode.NodeType.Line) continue;

            //CurrentIndex 반영
            currentIndex = i + 1;

            //진행중이던 코루틴 모두 중지
            if (typingCoroutine != null) { StopCoroutine(typingCoroutine); typingCoroutine = null; }
            if (autoPlayCoroutine != null) { StopCoroutine(autoPlayCoroutine); autoPlayCoroutine = null; }
            isTyping = false;

            ShowLine(scriptNodes[i].line);
            return;
        }
    }
}

    
[System.Serializable]
public class DialogLine
{
    //텍스트
    public string name     = "";
    public string text     = "";

    //리소스
    public string cgKey    = "";
    public string cgPos    = "";
    public string animation= "";
    public string voiceKey = "";
    public int speakerType = 1;             //1: 아이돌 2: 프로듀서 3: 기타

    //이펙트
    public string effect   = "";
    public float value     = 1.0f;
    public float duration  = 0f;
}

public class ScriptNode
{
    //노드를 타입별로 구분
    public enum NodeType {Line, Audio, SE, Transition, BG, Break, SetCG, Image}  // Image 추가

    //type == Line일 때 사용
    public NodeType type;
    public DialogLine line;

    //tpye = BGM/SE일 때 사용 
    public string track;                
    public float volume;
    public float seDuration;
    public string audioSlot;
    public string audioEffect;
    
    public string transition_effect;
    public string transition_se;
    public string bg;
    public string bgEffect;
    public string zoomPos;
    public float zoomValue;

    //type == Break일 때 사용
    public float breakDuration;
    public string breakEffect;

    //type == SetCG일 때 사용
    public string setCgKey;
    public string setCgPos;
    public string setCgAnimation;
    public string setCgEffect;
    public float setCgValue;
    public float setCgDuration;

    //type == Image일 때 사용
    public string imageKey;
    public float imageDuration;

    //내부 생성자 - 외부에서는 반드시 아래 Create* 팩토리 메서드를 통해서만 생성
    private ScriptNode() { }

    //대사 노드 생성
    public static ScriptNode CreateLine(DialogLine line)
    {
        return new ScriptNode
        {
            type = NodeType.Line,
            line = line
        };
    }

    //Audio 노드 생성
    public static ScriptNode CreateAudio(string slot, string track, float volume, string effect = "")
    {
        return new ScriptNode
        {
            type = NodeType.Audio,
            audioSlot = slot,
            track = track,
            volume = volume,
            audioEffect = effect
        };
    }

    //SE 노드 생성
    public static ScriptNode CreateSE(string track, float volume, float seDuration = 0f)
    {
        return new ScriptNode
        {
            type = NodeType.SE,
            track = track,
            volume = volume,
            seDuration = seDuration
        };
    }

    //Transition 노드 생성
    public static ScriptNode CreateTransition(string effect, string se)
    {
        return new ScriptNode
        {
            type = NodeType.Transition,
            transition_effect = effect,
            transition_se = se
        };
    }

    //Break 노드 생성
    public static ScriptNode CreateBreak(float breakDuration = 0f, string breakEffect = "")
    {
        return new ScriptNode
        {
            type = NodeType.Break,
            breakDuration = breakDuration,
            breakEffect = breakEffect
        };
    }

    //BG 노드 생성
    public static ScriptNode CreateBG(string bgKey, string bgEffect = "", string zoomPos = "5", float zoomValue = 1.0f)
    {
        return new ScriptNode
        {
            type = NodeType.BG,
            bg = bgKey,
            bgEffect = bgEffect,
            zoomPos = zoomPos,
            zoomValue = zoomValue
        };
    }

    //SetCG 노드 생성
    public static ScriptNode CreateSetCG(string cgKey, string cgPos, string cgAnimation, string cgEffect, float cgValue, float cgDuration)
    {
        return new ScriptNode
        {
            type = NodeType.SetCG,
            setCgKey = cgKey,
            setCgPos = cgPos,
            setCgAnimation = cgAnimation,
            setCgEffect = cgEffect,
            setCgValue = cgValue,
            setCgDuration = cgDuration
        };
    }

    //Image 노드 생성
    public static ScriptNode CreateImage(string imageKey, float imageDuration)
    {
        return new ScriptNode
        {
            type = NodeType.Image,
            imageKey = imageKey,
            imageDuration = imageDuration
        };
    }
}

//CGGroup의 단일 Spine 데이터 저장용 클래스
public class CGGroupEntry
{
    public string cgKey;
    public string cgPos;
    public string animation;
    public string speakerName;   //Lip 재사용 비교용 화자명 (CGGROUP: Name의 쉼표 뒤, 단일 CG: TEXT의 Name)

    public CGGroupEntry(string cgKey, string cgPos, string animation, string speakerName = "")
    {
        this.cgKey = cgKey;
        this.cgPos = cgPos;
        this.animation = animation;
        this.speakerName = speakerName;
    }
}