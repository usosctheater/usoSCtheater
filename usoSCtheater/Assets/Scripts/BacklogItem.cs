using Microsoft.Unity.VisualStudio.Editor;
using TMPro;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.UI;

public class BacklogItem : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Image sdIcon;
    [SerializeField] private UnityEngine.UI.Image bubble;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI dialogText;
    [SerializeField] private Button soundButton;
    [SerializeField] private GameObject sdIconRoot;

    private string voiceKey;
    private AudioManager audioManager;

    public void Setup(DialogLine line, Sprite bubbleSprite, Sprite sdSprite, AudioManager am)
    {
        audioManager = am;
        voiceKey = line.voiceKey;

        bubble.sprite = bubbleSprite;

        nameText.text = line.name;
        dialogText.text = line.text;

        if (line.speakerType == 3) sdIconRoot.SetActive(false);
        else
        {
            sdIconRoot.SetActive(true);
            sdIcon.sprite = sdSprite;
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
