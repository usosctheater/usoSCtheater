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
    [SerializeField] private SceneManager sceneManager;
    [SerializeField] private EffectManager effectManager;
    [SerializeField] private UIManager uiManager;

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
    
    //Lip 재사용 기능
    private string lastCgKey = null;
    private string lastAnimation = null;
    private string lastSpeakerName = null;

    //CGgroup 구현용 Dict
    private Dictionary<string, List<CGGroupEntry>> cgGroupDict = new Dictionary<string, List<CGGroupEntry>>();
    
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

    public void LoadScene(TextAsset xmlAsset)
    {
        scriptNodes.Clear();
        currentIndex = 0;

        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xmlAsset.text);

        //Scene 바로 아래 모든 자식 노드를 순서대로 처리
        XmlNodeList lineNodes = doc.SelectNodes("Scene/Line");

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

                    scriptNodes.Add(new ScriptNode(line));
                    break;

                case "AUDIO":
                    scriptNodes.Add(new ScriptNode(ScriptNode.NodeType.Audio, GetAttr(node, "Value").ToLower(), GetAttr(node, "Track"), float.TryParse(GetAttr(node, "Volume"), out float aVol) ? aVol : 1.0f, GetAttr(node, "Effect").ToLower()));
                    break;

                case "SE":
                    scriptNodes.Add(new ScriptNode(ScriptNode.NodeType.SE, GetAttr(node, "Track"), float.TryParse(GetAttr(node, "Volume"), out float seVol) ? seVol : 1.0f, float.TryParse(GetAttr(node, "Duration"), out float seDur) ? seDur : 0f));
                    break;

                case "TRANSITION":
                    scriptNodes.Add(new ScriptNode(GetAttr(node, "Effect"), GetAttr(node, "SE")));
                    break;

                case "BG":
                    scriptNodes.Add(new ScriptNode(ScriptNode.NodeType.BG, GetAttr(node, "Key"), GetAttr(node, "Effect"), GetAttr(node, "Position"), float.TryParse(GetAttr(node, "Value"), out float bgVal) ? bgVal : 1.0f));
                    break;

                case "BREAK":
                    scriptNodes.Add(new ScriptNode(ScriptNode.NodeType.Break, float.TryParse(GetAttr(node, "Duration"), out float breakDur) ? breakDur : 0f, GetAttr(node, "Effect").Trim().ToLower()));
                    break;

                case "CGGROUP":
                    string groupName = GetAttr(node, "Name");
                    if (!cgGroupDict.ContainsKey(groupName)) cgGroupDict[groupName] = new List<CGGroupEntry>();

                    cgGroupDict[groupName].Add(new CGGroupEntry(GetAttr(node, "CG"), GetAttr(node, "Position"), GetAttr(node, "Animation")));
                    break;

                case "SETCG":
                    scriptNodes.Add(new ScriptNode(
                        ScriptNode.NodeType.SetCG,
                        GetAttr(node, "CG"),
                        GetAttr(node, "Position"),
                        GetAttr(node, "Animation"),
                        GetAttr(node, "Effect").Trim().ToLower(),
                        float.TryParse(GetAttr(node, "Value"), out float setCgVal) ? setCgVal : 1.0f,
                        float.TryParse(GetAttr(node, "Duration"), out float setCgDur) ? setCgDur : 0f
                    ));
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
            lastCgKey = null;
            lastAnimation = null;
            lastSpeakerName = null;
        }
        // CG - 키가 있을 때만
        else if (!string.IsNullOrEmpty(line.cgKey))
        {
            //CG 키가 CGGroup인 경우
            if (cgGroupDict.ContainsKey(line.cgKey))
            {
                foreach (var entry in cgGroupDict[line.cgKey]) cgManager.SetCG(entry.cgKey, entry.cgPos, entry.animation, GetVoiceDuration(line.voiceKey));

                lastCgKey = line.cgKey;
                lastAnimation = null;               //그룹은 단일 Animation 지정 없음
                lastSpeakerName = line.name;
            }
            //그 외에는 기존 단일 CG 처리
            else
            {
                cgManager.SetCG(line.cgKey, line.cgPos, line.animation, GetVoiceDuration(line.voiceKey));
            
                //Lip 재사용 기능을 위한 정보 저장
                lastCgKey = line.cgKey;
                lastAnimation = line.animation;
                lastSpeakerName = line.name;
            }
        }
        //CG 키가 없지만 이전 CG가 있고, 화자가 같다면 Lip 재사용
        else if (!string.IsNullOrEmpty(lastCgKey) && line.name == lastSpeakerName)
        {
            cgManager.RestartLipSync(lastCgKey, lastAnimation, GetVoiceDuration(line.voiceKey));
        }
        else
        {
            //둘 다 아니라면, 초기화
            lastCgKey = null;
            lastAnimation = null;
            lastSpeakerName = null;
        }
        
        //이펙트 처리
        // Effect = zoom일 경우
        if (line.effect == "zoom" && !string.IsNullOrEmpty(line.cgKey)) cgManager.SetZoom(line.cgKey, line.cgPos, line.value, line.duration);

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

                case ScriptNode.NodeType.Transition:
                    isTransition = true;
                    // normal 트랜지션이면 BGM 유지
                    bool isNormalTransition = node.transition_effect.ToLower() == "normal";
                    effectManager.PlayTransition(node.transition_effect, node.transition_se, ()=> {
                        ClearScene(stopBGM: !isNormalTransition);
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

        ClearScene();
        //Debug.Log("씬 종료");
        Debug.Log($"[DialogManager] 씬 종료 — SceneManager에 전달");
        sceneManager.OnSceneEnd();
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
            lastCgKey = null;
            lastAnimation = null;
            lastSpeakerName = null;
            return;
        }

        // CG 키가 CGGroup인 경우
        // 주의: lastCgKey/lastAnimation은 여기서 절대 건드리지 않음.
        // SETCG는 대사가 없는 타입이라, 이 값을 갱신하면 SETCG 이후에 나오는
        // "같은 화자의 CG 생략 TEXT 라인"이 엉뚱한 캐릭터의 EndLoop/립을 재시작하게 됨.
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
        if (node.setCgEffect == "zoom") cgManager.SetZoom(node.setCgKey, node.setCgPos, node.setCgValue, node.setCgDuration);
    }

    // stopBGM: false이면 BGM을 정지하지 않음 (normal 트랜지션 등에서 BGM 유지 시 사용)
    private void ClearScene(bool stopBGM = true)
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
        if (string.IsNullOrEmpty(voiceKey)) return 0f;

        //구분자로 다중 파싱 시에는 가장 긴 길이를 반환
        float maxDuration = 0f;

        foreach(string key in voiceKey.Split(new char[] { ' ', ','}, System.StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = key.Trim();
            AudioClip clip = Resources.Load<AudioClip>($"Audio/Voice/{trimmed}");
            
            if (clip != null) maxDuration = Mathf.Max(maxDuration, clip.length);
        }

        return maxDuration;
    }

    //디버그용 기능
    //씬 강제 전환 전 진행 중이던 타이핑/자동재생/트랜지션 상태 초기화
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

        ClearScene();
    }

    public void DebugPrevLine()
    {
        int searchFrom = currentIndex -2;

        for (int i = searchFrom; i >= 0; i++)
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
    public enum NodeType {Line, Audio, SE, Transition, BG, Break, SetCG}  // SetCG 추가

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

    //대사 노드 생성자
    public ScriptNode(DialogLine line)
    {
        this.type = NodeType.Line;
        this.line = line;
    }

    //Audio 노드 생성자
    public ScriptNode(NodeType type, string slot, string track, float volume, string effect = "")
    {
        this.type = type;
        this.audioSlot = slot;
        this.track = track;
        this.volume = volume;
        this.audioEffect = effect;
    }

    //SE 노드 생성자
    public ScriptNode(NodeType type, string track, float volume, float seDuration = 0f)
    {
        this.type = type;
        this.track = track;
        this.volume = volume;
        this.seDuration = seDuration;
    }

    //Transition 노드 생성자
    public ScriptNode(string effect, string se)
    {
        this.type = NodeType.Transition;
        this.transition_effect = effect;
        this.transition_se = se;
    }

    //Break 노드 생성자
    public ScriptNode(NodeType type, float breakDuration = 0f, string breakEffect = "")
    {
        this.type = type;
        this.breakDuration = breakDuration;
        this.breakEffect = breakEffect;
    }

    //BG 노드 생성자
    public ScriptNode(NodeType type, string bgKey, string bgEffect = "", string zoomPos = "5", float zoomValue = 1.0f)
    {
        this.type = type;
        this.bg = bgKey;
        this.bgEffect = bgEffect;
        this.zoomPos = zoomPos;
        this.zoomValue = zoomValue;
    }

    //SetCG 노드 생성자 (BG 생성자와 파라미터 개수를 다르게 해서 오버로드 모호성 방지 위해 전부 필수 인자로 처리)
    public ScriptNode(NodeType type, string cgKey, string cgPos, string cgAnimation, string cgEffect, float cgValue, float cgDuration)
    {
        this.type = type;
        this.setCgKey = cgKey;
        this.setCgPos = cgPos;
        this.setCgAnimation = cgAnimation;
        this.setCgEffect = cgEffect;
        this.setCgValue = cgValue;
        this.setCgDuration = cgDuration;
    }
}

//CGGroup의 단일 Spine 데이터 저장용 클래스
public class CGGroupEntry
{
    public string cgKey;
    public string cgPos;
    public string animation;

    public CGGroupEntry(string cgKey, string cgPos, string animation)
    {
        this.cgKey = cgKey;
        this.cgPos = cgPos;
        this.animation = animation;
    }
}