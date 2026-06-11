using System.Collections;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine.EventSystems;

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
                    line.text = GetAttr(node, "Text");
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

                case "BGM":
                    scriptNodes.Add(new ScriptNode(ScriptNode.NodeType.BGM, GetAttr(node, "Track"), float.TryParse(GetAttr(node, "Volume"), out float bgmVol) ? bgmVol : 1.0f));
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
            cgManager.ClearAllCGState();
            lastCgKey = null;
            lastAnimation = null;
            lastSpeakerName = null;
        }
        // CG - 키가 있을 때만
        else if (!string.IsNullOrEmpty(line.cgKey))
        {
            cgManager.SetCG(line.cgKey, line.cgPos, line.animation, GetVoiceDuration(line.voiceKey));
            //Debug.Log($"[CG] {line.cgKey} / 위치: {line.cgPos}");
            
            //Lip 재사용 기능을 위한 정보 저장
            lastCgKey = line.cgKey;
            lastAnimation = line.animation;
            lastSpeakerName = line.name;
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
                case ScriptNode.NodeType.BGM:
                    audioManager.PlayBGM(node.track, node.volume);
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

                case ScriptNode.NodeType.Line:
                    ShowLine(node.line);
                    return;
            }
        }

        ClearScene();
        //Debug.Log("씬 종료");
        Debug.Log($"[DialogManager] 씬 종료 — SceneManager에 전달");
        sceneManager.OnSceneEnd();
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
        if (stopBGM) audioManager.StopBGM();
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
            yield return new WaitForSecondsRealtime(typingSpeed);
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

        DialogLine line = scriptNodes[currentIndex - 1].line;
        dialogText.text = line.text;
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
        }
    }

    private IEnumerator AutoPlayCoroutine(DialogLine line)
    {
        //텍스트 타이핑 시간 계산
        float typingDuration = line.text.Length * typingSpeed;
        float voiceDuration = GetVoiceDuration(line.voiceKey);

        //둘 중 더 긴 시간동안 대기
        float waitDuration = Mathf.Max(voiceDuration, typingDuration);
        yield return new WaitForSecondsRealtime(waitDuration);

        //만약 타이핑이 아직 진행 중이면 완료될 때까지 대기
        while (isTyping) yield return null;

        //고정 딜레이 적용
        yield return new WaitForSecondsRealtime(autoPlayDelay);

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

        AudioClip clip = Resources.Load<AudioClip>($"Audio/Voice/{voiceKey}");
        return clip != null ? clip.length : 0f;
    }

}

    
[System.Serializable]
public class DialogLine
{
    //텍스트
    public string name;
    public string text;

    //리소스
    public string cgKey;
    public string cgPos;
    public string animation;
    public string voiceKey;
    public int speakerType;             //1: 아이돌 2: 프로듀서 3: 기타

    //이펙트
    public string effect;
    public float value;
    public float duration;
}

public class ScriptNode
{
    //노드를 타입별로 구분
    public enum NodeType {Line, BGM, SE, Transition, BG}

    //type == Line일 때 사용
    public NodeType type;
    public DialogLine line;

    //tpye = BGM/SE일 때 사용 
    public string track;                
    public float volume;
    public float seDuration;
    
    public string transition_effect;
    public string transition_se;
    public string bg;
    public string bgEffect;
    public string zoomPos;
    public float zoomValue;

    //대사 노드 생성자
    public ScriptNode(DialogLine line)
    {
        this.type = NodeType.Line;
        this.line = line;
    }

    //BGM / SE 노드 생성자
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

    //BG 노드 생성자
    public ScriptNode(NodeType type, string bgKey, string bgEffect = "", string zoomPos = "5", float zoomValue = 1.0f)
    {
        this.type = type;
        this.bg = bgKey;
        this.bgEffect = bgEffect;
        this.zoomPos = zoomPos;
        this.zoomValue = zoomValue;
    }
}