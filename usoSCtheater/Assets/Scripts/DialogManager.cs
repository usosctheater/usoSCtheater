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

    private List<DialogLine> lines = new List<DialogLine>();
    private int currentIndex = 0;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        LoadXML();
        ShowLine();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            NextLine();
        }
    }

    private void LoadXML()
    {
        lines.Clear();

        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xmlFile.text);

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
    }

    private void ShowLine()
    {
        if (currentIndex >= lines.Count)
        {
            Debug.Log("대화 종료");
            return;
        }
        
        DialogLine line = lines[currentIndex];

        //텍스트 처리
        nameText.text = line.name;
        dialogText.text = line.text;

        //리소스 처리
        // CG - 키가 있을 때만
        if (!string.IsNullOrEmpty(line.cgKey))
        {
            cgManager.SetCG(line.cgKey, line.cgPos, line.animation);
            //Debug.Log($"[CG] {line.cgKey} / 위치: {line.cgPos}");
        }

        // 애니메이션 — none이 아닐 때만
        if (!string.IsNullOrEmpty(line.animation) && line.animation != "none")
        {
            // animationManager.Play(line.animation);
            Debug.Log($"[Animation] {line.animation}");
        }

        // 보이스 — 키가 있을 때만
        if (!string.IsNullOrEmpty(line.voiceKey))
        {
            // audioManager.PlayVoice(line.voiceKey);
            Debug.Log($"[Voice] {line.voiceKey}");
        }
    }

    private void NextLine()
    {
        currentIndex++;
        ShowLine();
    }

    //속성이 없거나 비어있는 경우엔 빈 문자열 반환하는 함수
    private string GetAttr(XmlNode node, string key)
    {
        XmlAttribute attr = node.Attributes[key];
        return (attr != null) ? attr.Value : "";
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
