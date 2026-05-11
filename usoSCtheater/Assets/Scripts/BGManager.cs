using UnityEngine;
using UnityEngine.UI;
public class BGManager : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private UnityEngine.UI.Image bgImage;

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
}