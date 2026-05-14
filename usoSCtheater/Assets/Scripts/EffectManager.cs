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
    [Header("매니저 연결")]
    [SerializeField] private AudioManager audioManager;

    [Header("UI 연결")]
    [SerializeField] private UnityEngine.UI.Image overlayImage;

    [Header("트랜지션 설정")]
    [SerializeField] private float transitionHoldDuration = 0.5f;

    [Header("Wipe Left 설정")]
    [SerializeField] private RectTransform wipeBarRect;                         //WipeBar 이동용 Rect
    [SerializeField] private RectTransform wipeMaskRect;                        //TransitionMask 조절용 Rect
    [SerializeField] private RectTransform wipeTransitionScene;                 //트랜지션 씬 전체 컨트롤용
    [SerializeField] private RectTransform gearRect;                            //Gear 회전용 Rect
    [SerializeField] private float wipeBarDuration = 3f;                        //WipeBar 이동 시간
    [SerializeField] private float gearRotateSpeed = 45f;                       //Gear 회전 속도
    [SerializeField] private AnimationCurve wipeBarCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private bool _onCompleteInvoked = false;
    float wipeMaskWidth = 2560f;


    [Header("Fade 설정")]
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float fadeInDuration = 0.5f;
    [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Iris Simple 설정")]
    [SerializeField] private UnityEngine.UI.Image irisImage;                                            //패널 이미지
    [SerializeField] private UnityEngine.UI.Image irisIcon;                                             //유닛 아이콘
    [SerializeField] private float irisOutDuration = 1.0f;
    [SerializeField] private float irisInDuration = 1.0f;
    [SerializeField] private float irisMinScale = 1.0f;
    [SerializeField] private float irisMaxScale = 13.0f;

    [SerializeField] private AnimationCurve irisOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve irisInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    private Material irisMaterial;

    void Awake()
    {
        overlayImage.gameObject.SetActive(false);

        //Iris 트랜지션 세팅
        irisImage.gameObject.SetActive(false);
        irisIcon.gameObject.SetActive(false);

        //Wipe 트랜지션 세팅
        wipeMaskRect.gameObject.SetActive(false);
        wipeTransitionScene.gameObject.SetActive(false);
        wipeBarRect.gameObject.SetActive(false);
    }

    public void PlayTransition(string effect, string se, Action onComplete)
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
                StartCoroutine(WipeCoroutine(se, onComplete));
            break;

            case "iris":
                StartCoroutine(IrisCoroutine(se, onComplete));
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

    private IEnumerator WipeCoroutine(string se, Action onComplete)
    {
        // | 구분자 있을 경우, se를 out과 in으로 분리해서 사용
        string[] seParts = se.Split('|');
        string se_out = seParts.Length > 0 ? seParts[0].Trim() : "";
        string se_in = seParts.Length > 1 ? seParts[1].Trim() : "";

        if (!string.IsNullOrEmpty(se_out)) audioManager.PlaySE(se_out);

        float screenW = Screen.width;

        //Mask 너비가 2560이므로 평행사변형 양 끝이 화면 밖까지 가게        
        float startX = -(screenW * 0.5f + wipeMaskWidth * 0.5f);                    //화면 왼쪽 밖
        float endX = screenW * 0.5f + wipeMaskWidth * 0.5f;                         //화면 오른쪽 밖

        wipeMaskRect.gameObject.SetActive(true);
        wipeBarRect.gameObject.SetActive(true);
        wipeTransitionScene.gameObject.SetActive(true);
        wipeBarRect.localRotation = UnityEngine.Quaternion.identity;

        //시작 위치 세팅
        wipeMaskRect.anchoredPosition = new UnityEngine.Vector2(startX, 0f);
        wipeTransitionScene.anchoredPosition = new UnityEngine.Vector2(-startX, 0f);

        //WipeBar와 평행사변형 사이의 Padding값
        float wipeBarOffset = wipeMaskWidth * 0.4f;
        wipeBarRect.anchoredPosition = new UnityEngine.Vector2(startX + wipeBarOffset, 0f);        //WipeBar 시작 위치는 평행사변형의 오른쪽

        //WipeBar/WipeMask 이동, Gear 회전
        float elapsed = 0f;
        while (elapsed < wipeBarDuration)
        {
            elapsed += Time.deltaTime;
            float t = wipeBarCurve.Evaluate(elapsed / wipeBarDuration);
            float posX = Mathf.Lerp(startX, endX, t);

            //WipeMask 이동
            wipeMaskRect.anchoredPosition = new UnityEngine.Vector2(posX, 0f);

            //TransitionScene 역방향 이동 (중앙 고정처럼 보이게)
            wipeTransitionScene.anchoredPosition = new UnityEngine.Vector2(-posX, 0f);

            //WipeBar는 Offset 유지하면서 이동
            float wipeBarX = posX + wipeBarOffset;
            if (wipeBarX > screenW * 0.5f) wipeBarX = posX  - wipeBarOffset;
            
            wipeBarRect.anchoredPosition = new UnityEngine.Vector2(wipeBarX, 0f);
            //Gear 회전
            gearRect.Rotate(0f, 0f, -gearRotateSpeed * Time.deltaTime);
            
            //Mask가 화면 중앙 근처일 때 콜백 호출
            if (!_onCompleteInvoked && posX >= 0f)
            {
                onComplete?.Invoke();
                _onCompleteInvoked = true;

                //반복문 분리하기 싫어서 위험하지만 그냥 배치
                if (!string.IsNullOrEmpty(se_in)) audioManager.PlaySE(se_in);
            }

            yield return null;
        }
        
        _onCompleteInvoked = false;

        //전부 비활성화
        wipeBarRect.gameObject.SetActive(false);
        wipeMaskRect.gameObject.SetActive(false);
        wipeTransitionScene.gameObject.SetActive(false);

    }

    private IEnumerator IrisCoroutine(string se, Action onComplete)
    {
        // | 구분자 있을 경우, se를 out과 in으로 분리해서 사용
        string[] seParts = se.Split('|');
        string se_out = seParts.Length > 0 ? seParts[0].Trim() : "";
        string se_in = seParts.Length > 1 ? seParts[1].Trim() : "";

        if (!string.IsNullOrEmpty(se_out)) audioManager.PlaySE(se_out);
        else Debug.Log("[EffectManager] SE_out is null");

        irisIcon.gameObject.SetActive(true);        

        irisImage.gameObject.SetActive(true);
        RectTransform rect = irisImage.GetComponent<RectTransform>();

        //Iris Out > 구멍이 작아지며 화면을 덮음 (CutOff : 1 > 0)
        float elapsed = 0f;
        while (elapsed < irisOutDuration)
        {
            elapsed += Time.deltaTime;

            float t = irisOutCurve.Evaluate(elapsed / irisOutDuration);
            float scale = Mathf.Lerp(irisMaxScale, irisMinScale, t);

            rect.localScale = new UnityEngine.Vector3(scale, scale, 1f);
            
            yield return null;
        }

        rect.localScale = new UnityEngine.Vector3(irisMinScale, irisMinScale, 1f);

        //화면이 완전히 덮인 순간 콜백 호출
        onComplete?.Invoke();

        //일정 시간 대기
        yield return new WaitForSeconds(transitionHoldDuration);

        if (!string.IsNullOrEmpty(se_in)) audioManager.PlaySE(se_in);
        else Debug.Log("[EffectManager] SE_in is null");

        //Iris In > 구멍이 커지며 화면이 드러남 (CutOFf : 0 > 1)
        elapsed = 0f;
        while (elapsed < irisInDuration)
        {
            elapsed += Time.deltaTime;

            float t = irisInCurve.Evaluate(elapsed / irisInDuration);
            float scale = Mathf.Lerp(irisMinScale, irisMaxScale, t);

            rect.localScale = new UnityEngine.Vector3(scale, scale, 1f);

            yield return null;
        }

        rect.localScale = new UnityEngine.Vector3(irisMaxScale, irisMaxScale, 1f);

        irisImage.gameObject.SetActive(false);
        irisIcon.gameObject.SetActive(false);
    }

}
