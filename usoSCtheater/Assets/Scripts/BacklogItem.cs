// using Microsoft.Unity.VisualStudio.Editor;
using TMPro;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.UI;

public class BacklogItem : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private UnityEngine.UI.Image sdIcon;
    [SerializeField] private UnityEngine.UI.Image bubble;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI dialogText;
    [SerializeField] private Button soundButton;
    [SerializeField] private GameObject sdIconRoot;

    [Header("sdIcon RectTransform 프리셋")]
    [SerializeField] private Vector2 sdOffset = new Vector2(5f, -60f);
    [SerializeField] private Vector2 sdIconSize = new Vector2(80f, 192f);

    private string voiceKey;
    private AudioManager audioManager;

    public void Setup(DialogLine line, Sprite bubbleSprite, Sprite sdSprite, AudioManager am, Sprite circleMask)
    {
        audioManager = am;
        voiceKey = line.voiceKey;

        bubble.sprite = bubbleSprite;

        nameText.text = line.name;
        dialogText.text = line.text;

        if (sdSprite == null) sdIconRoot.SetActive(false);
        else
        {
            sdIconRoot.SetActive(true);

            //마스크 설정
            UnityEngine.UI.Image maskImage = sdIconRoot.GetComponent<UnityEngine.UI.Image>();
            if (maskImage != null) maskImage.sprite = circleMask;

            //SD 이미지 크기 및 오프셋 적용
            RectTransform sdRect = sdIcon.GetComponent<RectTransform>();

            if (sdSprite != sdIcon.sprite)
            {
                sdIcon.sprite = sdSprite;
                sdIcon.SetNativeSize();
                sdRect.anchoredPosition = sdOffset;
                sdRect.localScale = Vector3.one;
                sdRect.sizeDelta = sdIconSize;
            }
        }

        if (string.IsNullOrEmpty(voiceKey)) soundButton.gameObject.SetActive(false);
        else
        {
            soundButton.gameObject.SetActive(true);
            soundButton.onClick.AddListener(OnSoundButtonClicked);
        }
    }

    private void OnSoundButtonClicked()
    {
        if (audioManager != null && !string.IsNullOrEmpty(voiceKey)) audioManager.PlayVoice(voiceKey);
    }
}
