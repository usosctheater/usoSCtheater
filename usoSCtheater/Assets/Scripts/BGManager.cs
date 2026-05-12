using UnityEngine;
using UnityEngine.UI;
public class BGManager : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private UnityEngine.UI.Image bgImage;

    [Header("Flashback 설정")]
    [SerializeField] private Image flashbackOverlay;
    [SerializeField] [Range(0f, 1f)] private float flashbackAlpha = 0.3f;

    [Header("리소스 경로")]
    [SerializeField] private string bgPath = "BG";

    public void SetBG(string bgKey)
    {
        if (string.IsNullOrEmpty(bgKey))
        {
            Debug.LogWarning("[BGManager] BG 키가 비어있습니다.");
            return;
        }

        Sprite sprite = Resources.Load<Sprite>($"{bgPath}/{bgKey}");

        if (sprite == null)
        {
            Debug.LogWarning($"[BGManager] BG 파일 없음: {bgKey}");
            return;
        }

        bgImage.sprite = sprite;
        bgImage.gameObject.SetActive(true);
    }

    public void HideBG()
    {
        bgImage.gameObject.SetActive(false);
    }

    public void SetFlashback()
    {
        if (flashbackOverlay == null)
        {
            Debug.LogWarning("[BGManager] flashbackOverlay가 연결되지 않았습니다.");
            return;
        }

        Color color = flashbackOverlay.color;
        color.a = flashbackAlpha;
        flashbackOverlay.color = color;
        flashbackOverlay.gameObject.SetActive(true);
    }

    public void HideFlashback()
    {
        if (flashbackOverlay == null) return;
        flashbackOverlay.gameObject.SetActive(false);
    }
}