using System;
using System.Collections;
using System.Numerics;
using Microsoft.Unity.VisualStudio.Editor;
using UnityEngine;
using UnityEngine.UI;

public class EffectManager : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private UnityEngine.UI.Image overlayImage;
    [SerializeField] private UnityEngine.UI.Image wipeImage;

    [Header("트랜지션 설정")]
    [SerializeField] private float fadeDuration = 0.5f;
    [SerializeField] private float wipeDuration = 0.5f;


    void Awake()
    {
        overlayImage.gameObject.SetActive(false);
        wipeImage.gameObject.SetActive(false);
    }

    public void PlayTransition(string effect, Action onComplete)
    {
        
        switch (effect.ToLower())
        {
            case "normal":
                onComplete?.Invoke();
            break;

            case "fade":
                StartCoroutine(FadeCoroutine(fadeDuration, onComplete));
            break;

            case "wipe_left":
                StartCoroutine(WipeCoroutine(UnityEngine.Vector2.right, wipeDuration, onComplete));
            break;

            default:
                Debug.LogWarning($"[EffectManager] 알 수 없는 트랜지션: {effect}");
                onComplete?.Invoke();
            break;
        }
    }

    //OverlayImage의 알파값 조절
    private IEnumerator FadeCoroutine(float duration, Action onComplete)
    {
        overlayImage.gameObject.SetActive(true);
        Color color = overlayImage.color;

        //FadeOut (0 > 1)
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            color.a = Mathf.Lerp(0f, 1f, elapsed / duration);
            overlayImage.color = color;
            yield return null;
        }

        color.a = 1f;
        overlayImage.color = color;

        //화면이 완전히 가려진 순간 콜백 호출 > 이 때 BG/CG 교체
        onComplete?.Invoke();

        elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            color.a = Mathf.Lerp(1f, 0f, elapsed / duration);
            overlayImage.color = color;
            yield return null;
        }

        color.a = 0f;
        overlayImage.color = color;

        overlayImage.gameObject.SetActive(false);
    }

    private IEnumerator WipeCoroutine(UnityEngine.Vector2 direction, float duration, Action onComplete)
    {
        wipeImage.gameObject.SetActive(true);

        RectTransform rect = wipeImage.GetComponent<RectTransform>();
        UnityEngine.Vector2 screenSize = new UnityEngine.Vector2(Screen.width, Screen.height);

        UnityEngine.Vector2 startPos = -direction * screenSize;
        UnityEngine.Vector2 endPos = direction * screenSize;

        rect.anchoredPosition = startPos;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            rect.anchoredPosition = UnityEngine.Vector2.Lerp(startPos, endPos, elapsed / duration);
            yield return null;
        }

        rect.anchoredPosition = endPos;
        wipeImage.gameObject.SetActive(false);

        onComplete?.Invoke();
    }
}
