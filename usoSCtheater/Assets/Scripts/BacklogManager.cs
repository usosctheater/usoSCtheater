using System.Collections.Generic;
using System.Linq;
// using UnityEditor.Search;
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
    [SerializeField] private GameObject transitionDividerPrefab;

    [Header("스프라이트")]
    [SerializeField] private Sprite bubbleType1;                //아이돌 (분홍색)
    [SerializeField] private Sprite bubbleType2;                //프로듀서 (파란색)
    [SerializeField] private Sprite bubbleType3;                //기타 (초록색)
    [SerializeField] private Sprite dividerSprite1;             //normal
    [SerializeField] private Sprite dividerSprite2;             //Fade, Iris
    [SerializeField] private Sprite dividerSprite3;             //Wipe_Left

    [Header("SD 이미지")]
    [SerializeField] private Sprite defaultSDSprite;
    [SerializeField] private Sprite circleMaskSprite;
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

        //동일 화자가 연속해서 대사를 하는 경우, SDIcon 동기화를 위한 변수
        string lastCgKey = null;
        string lastSpeakerName = null;
        bool hideSD = false;

        settingPanel.SetActive(false);

        //기존 항목 제거
        foreach (Transform child in contentParent) Destroy(child.gameObject);

        //현재까지 읽은 노드 가져오기
        List<ScriptNode> readNodes = dialogManager.GetReadNodes();

        foreach (ScriptNode node in readNodes)
        {
            if (node.type == ScriptNode.NodeType.Line) 
            {
                DialogLine line = node.line;

                //CG = none이면 SD 숨김 처리
                if (!string.IsNullOrEmpty(line.cgKey) && line.cgKey.ToLower() == "none")
                {
                    lastCgKey = null;
                    lastSpeakerName = null;
                    line.cgKey = null;
                    hideSD = true;
                }
                //CG 없는데 이전 화자와 동일하면 cgKey 재사용
                else if (string.IsNullOrEmpty(line.cgKey) && line.name == lastSpeakerName)
                {
                    line.cgKey = lastCgKey;
                }
                //CG 있고 명시되어 있으면 기억 갱신
                else if (!string.IsNullOrEmpty(line.cgKey))
                {
                    lastCgKey = line.cgKey;
                    lastSpeakerName = line.name;
                    hideSD = false;
                }

                CreateDialogItem(line, hideSD);
            }
            else if (node.type == ScriptNode.NodeType.Transition)
            {
                CreateTransitionDivider(node.transition_effect);
                //트랜지션 시에도 기억 초기화
                lastCgKey = null;
                lastSpeakerName = null;
                hideSD = false;
            }
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

    private void CreateDialogItem(DialogLine line, bool hideSD = false)
    {
        GameObject item = Instantiate(dialogItemPrefab, contentParent);
        BacklogItem backlogItem = item.GetComponent<BacklogItem>();

        //hideSD = true거나, 화자가 프로듀셔면 sdSprite = null로 처리
        Sprite sdSprite;
        if (hideSD || line.speakerType == 2) sdSprite = null;
        else
        {
            sdSprite = GetSDSprite(line.cgKey);
            //hideSD도 아니고 프로듀서도 아닌데 SD키가 없다면, 디폴트로 처리
            if (sdSprite == null) sdSprite = defaultSDSprite;
        }

        backlogItem.Setup(
            line,
            GetBubbleSprite(line.speakerType),
            sdSprite,
            audioManager,
            circleMaskSprite
            );
    }

    private void CreateTransitionDivider(string effect)
    {
        GameObject item = Instantiate(transitionDividerPrefab, contentParent);
        TransitionDividerItem divider = item.GetComponent<TransitionDividerItem>();

        Sprite sprite = GetDividerSprite(effect);
        Image.Type imageType = GetDividerImageType(effect);

        divider.Setup(sprite, imageType);
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
        if (string.IsNullOrEmpty(cgKey)) return null;
        return Resources.Load<Sprite>($"{sdResourcePath}/{cgKey}");
    }

    private Sprite GetDividerSprite(string effect)
    {
        switch (effect.ToLower())
        {
            case "normal":          return dividerSprite1;
            case "fade":
            case "iris":            return dividerSprite2;
            case "wipe_left":       return dividerSprite2;
            default:                return dividerSprite1;
        }
    }

    private Image.Type GetDividerImageType(string effect)
    {
        switch (effect.ToLower())
        {
            case "normal":          return Image.Type.Sliced;
            case "fade":
            case "iris":            
            case "wipe_left":       return Image.Type.Tiled;
            default:                return Image.Type.Sliced;
        }
    }
}
