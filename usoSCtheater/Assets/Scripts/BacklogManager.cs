using System.Collections.Generic;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.UI;

public class BacklogManager : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private GameObject backlogUI;
    [SerializeField] private Transform contentParent;
    [SerializeField] private Button closeButton;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private GameObject settingPanel;

    [Header("프리팹")]
    [SerializeField] private GameObject dialogItemPrefab;

    [Header("스프라이트")]
    [SerializeField] private Sprite bubbleType1;                //아이돌 (분홍색)
    [SerializeField] private Sprite bubbleType2;                //프로듀서 (파란색)
    [SerializeField] private Sprite bubbleType3;                //기타 (초록색)

    [Header("SD 이미지")]
    [SerializeField] private Sprite defaultSDSprite;
    [SerializeField] private string sdResourcePath = "SD";

    private DialogManager dialogManager;
    private AudioManager audioManager;

    public bool IsOpen => backlogUI.activeSelf;

    void Awake()
    {
        closeButton.onClick.AddListener(CloseBacklog);
        backlogUI.SetActive(false);
    }

    public void Init(DialogManager dm, AudioManager am)
    {
        dialogManager = dm;
        audioManager = am;
    }

    public void OpenBacklog()
    {
        //백로그 열면 씬 시간 정지
        Time.timeScale = 0f;

        settingPanel.SetActive(false);

        //기존 항목 제거
        foreach (Transform child in contentParent) Destroy(child.gameObject);

        //현재까지 읽은 노드 가져오기
        List<ScriptNode> readNodes = dialogManager.GetReadNodes();

        foreach (ScriptNode node in readNodes)
        {
            if (node.type == ScriptNode.NodeType.Line) CreateDialogItem(node.line);
            else if (node.type == ScriptNode.NodeType.Transition) CreateTransitionDivider();
        }

        backlogUI.SetActive(true);

        //스크롤 가장 아래로
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    public void CloseBacklog()
    {
        //백로그 닫으면 시간 정상화
        Time.timeScale = 1f;

        settingPanel.SetActive(true);
        backlogUI.SetActive(false);
    }

    private void CreateDialogItem(DialogLine line)
    {
        GameObject item = Instantiate(dialogItemPrefab, contentParent);
        BacklogItem backlogItem = item.GetComponent<BacklogItem>();
        backlogItem.Setup(line, GetBubbleSprite(line.speakerType), GetSDSprite(line.cgKey), audioManager);
    }

    private void CreateTransitionDivider()
    {
        //구분선 코드
    }

    private Sprite GetBubbleSprite(int speakerType)
    {
        switch (speakerType)
        {
            case 1: return bubbleType1;
            case 2: return bubbleType2;
            case 3: return bubbleType3;
            default: return bubbleType3;
        }
    }

    private Sprite GetSDSprite(string cgKey)
    {
        if (string.IsNullOrEmpty(cgKey)) return defaultSDSprite;

        Sprite sd = Resources.Load<Sprite>($"{sdResourcePath}/{cgKey}");
        return sd != null ? sd : defaultSDSprite;
    }
}
