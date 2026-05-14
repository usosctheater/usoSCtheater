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

    public void setZoom(string pos, float scale)
    {
        if (bgImage == null) return;

        RectTransform rect = bgImage.GetComponent<RectTransform>();

        //Scale 적용
        rect.localScale = new Vector3(scale, scale, 1f);

        //키패드 방향 기준 Pivot 오프셋 계산
        //Scale이 커질수록 이동 거리도 커짐
        float offsetX = 0f;
        float offsetY = 0f;
        float moveX = rect.rect.width * (scale - 1f) * 0.5f;
        float moveY = rect.rect.height * (scale - 1f) * 0.5f;

        switch(pos)
        {
            case "1" : offsetX = moveX; offsetY = moveY; break;
            case "2" : offsetX = 0f; offsetY = moveY; break;
            case "3" : offsetX = -moveX; offsetY = moveY; break;
            case "4" : offsetX = moveX; offsetY = 0f; break;
            case "5" : offsetX = 0f; offsetY = 0f; break;
            case "6" : offsetX = -moveX; offsetY = 0f; break;
            case "7" : offsetX = moveX; offsetY = -moveY; break;
            case "8" : offsetX = 0f; offsetY = -moveY; break;
            case "9" : offsetX = -moveX; offsetY = -moveY; break;
            default: offsetX = 0f; offsetY = 0f; break;
        }

        rect.anchoredPosition = new Vector2(offsetX, offsetY);
    }

    public void hideZoom()
    {
        if (bgImage == null) return;

        RectTransform rect = bgImage.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;
        rect.anchoredPosition = Vector2.zero;
    }
}