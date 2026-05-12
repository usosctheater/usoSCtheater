using System;
using System.Collections;
using System.Numerics;
using System.Runtime.ExceptionServices;
using Microsoft.Unity.VisualStudio.Editor;
using UnityEditor.Search;
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

    [Header("Fade 설정")]
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Iris 설정")]
    [SerializeField] private UnityEngine.UI.Image irisImage;
    [SerializeField] private float irisOutDuration = 1.0f;
    [SerializeField] private float irisInDuration = 1.0f;

    [SerializeField] private AnimationCurve irisOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve irisInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    private Material irisMaterial;

    void Awake()
    {
        overlayImage.gameObject.SetActive(false);
        wipeImage.gameObject.SetActive(false);
        irisImage.gameObject.SetActive(false);

        //Material 인스턴스 복사 - 원본 수정 방지
        irisMaterial = new Material(irisImage.material);
        irisImage.material = irisMaterial;
    }

    public void PlayTransition(string effect, Action onComplete)
    {
        
        switch (effect.ToLower())
        {
            case "normal":
                onComplete?.Invoke();
            break;

            case "fade":
                StartCoroutine(FadeCoroutine(onComplete));
            break;

            case "wipe_left":
                StartCoroutine(WipeCoroutine(UnityEngine.Vector2.right, wipeDuration, onComplete));
            break;

            case "iris":
                StartCoroutine(IrisCoroutine(onComplete));
            break;

            default:
                Debug.LogWarning($"[EffectManager] 알 수 없는 트랜지션: {effect}");
                onComplete?.Invoke();
            break;
        }
    }

    //OverlayImage의 알파값 조절
    private IEnumerator FadeCoroutine(Action onComplete)
    {
        overlayImage.gameObject.SetActive(true);
        Color color = overlayImage.color;

        //FadeOut (0 > 1)
        float elapsed = 0f;

        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeOutDuration;
            color.a = fadeOutCurve.Evaluate(t);             //0 > 1
            overlayImage.color = color;
            yield return null;
        }

        color.a = 1f;
        overlayImage.color = color;

        //화면이 완전히 가려진 순간 콜백 호출 > 이 때 BG/CG 교체
        onComplete?.Invoke();

        elapsed = 0f;

        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeInDuration;

            color.a = 1.0f - fadeInCurve.Evaluate(t);       //1 > 0
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

    private IEnumerator IrisCoroutine(Action onComplete)
    {
        irisImage.gameObject.SetActive(true);

        //Iris Out > 구멍이 작아지며 화면을 덮음 (CutOff : 1 > 0)
        float elapsed = 0f;
        while (elapsed < irisOutDuration)
        {
            elapsed += Time.deltaTime;
            float t = irisOutCurve.Evaluate(elapsed / irisOutDuration);
            
            float scale = Mathf.Lerp(2.0f, 0.1f, t);
            irisMaterial.SetFloat("_Scale", scale);

            float cutoff = Mathf.Lerp(1.0f, 0.5f, t);
            irisMaterial.SetFloat("_Cutoff", cutoff);
            yield return null;
        }

        irisMaterial.SetFloat("_Scale", 0.1f);
        irisMaterial.SetFloat("_Cutoff", 0.5f);

        //화면이 완전히 덮인 순간 콜백 호출
        onComplete?.Invoke();

        //Iris In > 구멍이 커지며 화면이 드러남 (CutOFf : 0 > 1)
        elapsed = 0f;
        while (elapsed < irisInDuration)
        {
            elapsed += Time.deltaTime;
            float t = irisInCurve.Evaluate(elapsed / irisInDuration);
            
            float scale = Mathf.Lerp(0.1f, 2.0f, t);
            irisMaterial.SetFloat("_Scale", scale);

            float cutoff = Mathf.Lerp(0.5f, 1.0f, t);
            irisMaterial.SetFloat("_Cutoff", cutoff);
            yield return null;
        }

        irisMaterial.SetFloat("_Scale", 2.0f);
        irisMaterial.SetFloat("_Cutoff", 1.0f);

        irisImage.gameObject.SetActive(false);
    }

}
