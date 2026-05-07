using System.Collections;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DialogManager : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI dialogText;

    [Header("XML 파일")]
    [SerializeField] private TextAsset xmlFile;

    [Header("매니저 연결")]
    [SerializeField] private CGManager cgManager;
    [SerializeField] private AudioManager audioManager;

    [Header("타이핑 설정")]
    [SerializeField] private float typingSpeed = 0.1f;          //글자 당 딜레이 (s)

    private List<DialogLine> lines = new List<DialogLine>();
    //대사/BGM/SE를 순서대로 담을 노드 리스트
    private List<ScriptNode> scriptNodes = new List<ScriptNode>();
    private int currentIndex = 0;
    private Coroutine typingCoroutine;                          //타이핑 효과용 코루틴
    private bool isTyping = false;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        LoadXML();
        ProcessNext();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {   
            if (isTyping) SkipTyping();
                else ProcessNext();
        }
    }

    private void LoadXML()
    {
        lines.Clear();

        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xmlFile.text);

        //노드 정보를 다양화 하여, 처리 코드 변경으로 폐기
        /**
        XmlNodeList lineNodes = doc.SelectNodes("Scene/Line");

        foreach (XmlNode node in lineNodes)
        {
            DialogLine line = new DialogLine();

            //텍스트 로드
            line.name = node.Attributes["Name"].Value;
            line.text = node.Attributes["Text"].Value;

            //리소스 로드 (없으면 빈 문자열)
            line.cgKey = GetAttr(node, "CG");
            line.cgPos = GetAttr(node, "Position");
            line.animation = GetAttr(node, "Animation");
            line.voiceKey = GetAttr(node, "Voice");

            lines.Add(line);
        }

        Debug.Log($"총 {lines.Count}줄 로드 완료");
        **/

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

                    scriptNodes.Add(new ScriptNode(line));
                    break;

                case "BGM":
                    scriptNodes.Add(new ScriptNode(ScriptNode.NodeType.BGM, GetAttr(node, "Track"), float.TryParse(GetAttr(node, "Volume"), out float bgmVol) ? bgmVol : 1.0f));
                    break;

                case "SE":
                    scriptNodes.Add(new ScriptNode(ScriptNode.NodeType.SE, GetAttr(node, "Track"), float.TryParse(GetAttr(node, "Volume"), out float seVol) ? seVol : 1.0f));
                    break;

                default:
                    Debug.LogWarning($"[DialogManager] 알 수 없는 태그: {node.Name}");
                    break;
            }

        }

        Debug.Log($"총 {scriptNodes.Count}개 노드 로드 완료");
    }

    private void ShowLine(DialogLine line)
    {
        
        //인자로 넘겨받는 구조로 변경
        // DialogLine line = lines[currentIndex];

        //텍스트 처리
        nameText.text = line.name;
        // 코루틴 처리로 변경해서 기존 처리 라인 주석
        // dialogText.text = line.text;

        //이전 타이핑 코루틴 진행중이라면 중단시킴
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypeText(line));

        //리소스 처리
        // CG - 키가 있을 때만
        if (!string.IsNullOrEmpty(line.cgKey))
        {
            cgManager.SetCG(line.cgKey, line.cgPos, line.animation);
            //Debug.Log($"[CG] {line.cgKey} / 위치: {line.cgPos}");
        }

        //보이스 재생
        audioManager.PlayVoice(line.voiceKey);
    }

    //노드 다양화로 ProcessNext() 로 기능 이전
    /**
    private void NextLine()
    {
        currentIndex++;
        ShowLine();
    }
    **/

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
                    audioManager.PlaySE(node.track, node.volume);
                    continue;

                case ScriptNode.NodeType.Line:
                    ShowLine(node.line);
                    return;
            }
        }

        Debug.Log("씬 종료");
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
            yield return new WaitForSeconds(typingSpeed);
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

        DialogLine line = scriptNodes[currentIndex - 1].line;
        dialogText.text = line.text;
        isTyping = false;
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
}

public class ScriptNode
{
    //노드를 타입별로 구분
    public enum NodeType {Line, BGM, SE}

    public NodeType type;
    public DialogLine line;             //type == Line일 때 사용
    public string track;                //tpye = BGM/SE일 때 사용
    public float volume;

    //대사 노드 생성자
    public ScriptNode(DialogLine line)
    {
        this.type = NodeType.Line;
        this.line = line;
    }

    //BGM / SE 노드 생성자
    public ScriptNode(NodeType type, string track, float volume = 1.0f)
    {
        this.type = type;
        this.track = track;
        this.volume = volume;
    }
}