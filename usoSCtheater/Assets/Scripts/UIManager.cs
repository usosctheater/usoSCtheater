using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [Header("매니저 연결")]
    [SerializeField] private DialogManager dialogManager;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private BacklogManager backlogManager;

    [Header("대화창 UI")]
    [SerializeField] private Image dialogBoxImage;
    [SerializeField] private Sprite dialogBoxRed;
    [SerializeField] private Sprite dialogBoxBlue;
    [SerializeField] private Sprite dialogBoxGreen;

    [Header("씬 타이틀 UI")]
    [SerializeField] private GameObject sceneTitleUI;
    [SerializeField] private TextMeshProUGUI sceneTitleMainText;   // 변경: mainTitle용 텍스트
    [SerializeField] private TextMeshProUGUI sceneTitleSubText;    // 추가: subTitle용 텍스트
    [SerializeField] private float titleDisplayDuration = 3.0f;

    [Header("설정 UI")]
    [SerializeField] private Button settingButton;
    [SerializeField] private Image settingButtonImage;
    [SerializeField] private Sprite spriteSettingButton;
    [SerializeField] private Sprite spriteSettingButtonC;

    [SerializeField] private GameObject settingBack;
    [SerializeField] private RectTransform settingBackRect;

    [SerializeField] private Button autoButton;
    [SerializeField] private Image autoButtonImage;
    [SerializeField] private Sprite spriteAutoOn;
    [SerializeField] private Sprite spriteAutoOff;

    [SerializeField] private Button logButton;
    [SerializeField] private Button hideButton;
    [SerializeField] private RectTransform autoButtonRect;
    [SerializeField] private RectTransform logButtonRect;
    [SerializeField] private RectTransform hideButtonRect;

    [SerializeField] private float panelAnimDuration = 0.3f;
    [SerializeField] private float panelMaxHeight = 400f;

    [Header("비표시 UI 리스트")]
    [SerializeField] private List<GameObject> hideTargets;    

    private Coroutine titleCoroutine;

    private bool isPanelOpen = false;
    private bool isAutoPlay = false;
    private bool isHidden = false;
    public bool IsHidden => isHidden;

    private Coroutine panelCoroutine;
    public bool IsBacklogOpen => backlogManager.IsOpen;

    void Start()
    {
        backlogManager.Init(dialogManager, audioManager);

        //리스너 연결
        settingButton.onClick.AddListener(OnSettingButtonClicked);
        autoButton.onClick.AddListener(OnAutoButtonClicked);
        logButton.onClick.AddListener(OnLogButtonClicked);
        hideButton.onClick.AddListener(OnHideButtonClicked);

        //초기 상태 - 패널 닫힘
        settingBack.SetActive(false);
        autoButton.gameObject.SetActive(false);
        logButton.gameObject.SetActive(false);
        hideButton.gameObject.SetActive(false);

        settingBackRect.sizeDelta = new Vector2(settingBackRect.sizeDelta.x, 0f);
    }

    void Update()
    {
        //비표시 상태에서 아무 곳이나 클릭 시 UI 표시
        if (isHidden && Input.GetMouseButtonDown(0)) ToggleHide(false);
    }

    // 변경: mainTitle / subTitle 두 인자를 받도록 시그니처 변경
    public void ShowSceneTitle(string mainTitle, string subTitle)
    {
        if (string.IsNullOrEmpty(mainTitle) && string.IsNullOrEmpty(subTitle)) return;

        //이미 표시 중이라면 강제 중단 후 재시작
        if (titleCoroutine != null)
        {
            StopCoroutine(titleCoroutine);
            titleCoroutine = null;
        }

        titleCoroutine = StartCoroutine(ShowTitleCoroutine(mainTitle, subTitle));
    }

    private IEnumerator ShowTitleCoroutine(string mainTitle, string subTitle)
    {
        if (sceneTitleMainText != null) sceneTitleMainText.text = mainTitle;
        if (sceneTitleSubText  != null) sceneTitleSubText.text  = subTitle;
        sceneTitleUI.SetActive(true);

        yield return new WaitForSecondsRealtime(titleDisplayDuration);

        sceneTitleUI.SetActive(false);
        titleCoroutine = null;
    }

    private void OnSettingButtonClicked()
    {
        // Debug.Log("[UIManager] SettingButtonClicked");
        if (isHidden) return;

        if (isPanelOpen) ClosePanel();
        else OpenPanel();
    }

    private void OpenPanel()
    {
        isPanelOpen = true;
        settingButtonImage.sprite = spriteSettingButtonC;

        settingBack.SetActive(true);

        if (panelCoroutine != null) StopCoroutine(panelCoroutine);
        panelCoroutine = StartCoroutine(AnimatePanel(0f, panelMaxHeight));
    }

    private void ClosePanel()
    {
        isPanelOpen = false;
        settingButtonImage.sprite = spriteSettingButton;

        if (panelCoroutine != null) StopCoroutine(panelCoroutine);
        panelCoroutine = StartCoroutine(AnimatePanel(settingBackRect.sizeDelta.y, 0f, () =>
        {
            settingBack.SetActive(false);
        }));
    }

    private IEnumerator AnimatePanel(float from, float to, System.Action onComplete = null)
    {
        float elapsed = 0f;
        Vector2 size = settingBackRect.sizeDelta;

        //열릴 때만 버튼 순차 표시
        bool isOpening = to > from;

        //버튼 Y 위치의 절대값 기준으로 역산
        float hideThreshold = panelMaxHeight - Mathf.Abs(hideButtonRect.anchoredPosition.y);
        float logThreshold = panelMaxHeight - Mathf.Abs(logButtonRect.anchoredPosition.y);
        float autoThreshold = panelMaxHeight - Mathf.Abs(autoButtonRect.anchoredPosition.y);


        while(elapsed < panelAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / panelAnimDuration;
            size.y = Mathf.Lerp(from, to, t);
            settingBackRect.sizeDelta = size;

            //열릴 때는 아래에서부터, 닫힐 때는 위에서부터
            if (isOpening)
            {
                if (size.y >= hideThreshold) hideButton.gameObject.SetActive(true);
                if (size.y >= logThreshold) logButton.gameObject.SetActive(true);
                if (size.y >= autoThreshold) autoButton.gameObject.SetActive(true);
            }
            else
            {
                if (size.y < hideThreshold) hideButton.gameObject.SetActive(false);
                if (size.y < logThreshold) logButton.gameObject.SetActive(false);
                if (size.y < autoThreshold) autoButton.gameObject.SetActive(false);
            }

            yield return null;
        }
        
        size.y = to;
        settingBackRect.sizeDelta = size;
        onComplete?.Invoke();

    }

    private void OnLogButtonClicked()
    {
        ClosePanel();

        if (isAutoPlay)
        {
            isAutoPlay = false;
            autoButtonImage.sprite = spriteAutoOff;
            dialogManager.SetAutoPlay(false);
        }

        backlogManager.OpenBacklog();
    }

    private void OnAutoButtonClicked()
    {
        isAutoPlay = !isAutoPlay;
        autoButtonImage.sprite = isAutoPlay ? spriteAutoOn : spriteAutoOff;
        
        dialogManager.SetAutoPlay(isAutoPlay);
    }

    private void OnHideButtonClicked()
    {
        ToggleHide(true);
    }

    private void ToggleHide(bool hide)
    {
        isHidden = hide;

        foreach (var obj in hideTargets) obj.SetActive(!hide);

        if (hide) ClosePanel();
    }

    public void ToggleAutoPlay()
    {
        OnAutoButtonClicked();
    }

    public void SetDialogBoxType(int speakerType)
    {
        if (dialogBoxImage == null) return;

        dialogBoxImage.sprite = speakerType switch
        {
            1 => dialogBoxRed,
            2 => dialogBoxBlue,
            3 => dialogBoxGreen,
            _ => dialogBoxRed
        };

    }

}
