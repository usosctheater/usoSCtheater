using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UsoSCTheater.Recording; // [녹화] 에디터/빌드 페이싱 분기용

public class ImageManager : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private Image pictureImage;   // Key로 로드한 이미지 출력용
    [SerializeField] private Image frameImage;     // 인스펙터 고정 프레임 - pictureImage와 항상 함께 합성

    [Header("리소스 경로")]
    [SerializeField] private string imagePath = "Image";

    [Header("기본 노출 시간")]
    [SerializeField] private float defaultDuration = 1f;   // Duration 미지정 시 사용 (테스트 후 조정 가능)

    private Coroutine imageCoroutine;

    // duration이 음수(미지정)면 defaultDuration을 사용
    public void ShowImage(string imageKey, float duration)
    {
        if (string.IsNullOrEmpty(imageKey))
        {
            Debug.LogWarning("[ImageManager] Image 키가 비어있습니다.");
            return;
        }

        Sprite sprite = Resources.Load<Sprite>($"{imagePath}/{imageKey}");

        if (sprite == null)
        {
            Debug.LogWarning($"[ImageManager] Image 파일 없음: {imageKey}");
            return;
        }

        //이전에 노출 중이던 이미지가 있다면 중단 후 새로 시작
        if (imageCoroutine != null) StopCoroutine(imageCoroutine);

        float appliedDuration = duration >= 0f ? duration : defaultDuration;
        imageCoroutine = StartCoroutine(ShowImageCoroutine(sprite, appliedDuration));
    }

    private IEnumerator ShowImageCoroutine(Sprite sprite, float duration)
    {
        pictureImage.sprite = sprite;
        pictureImage.gameObject.SetActive(true);
        if (frameImage != null) frameImage.gameObject.SetActive(true);

        yield return RecordingTimeUtil.PacingWait(duration);

        pictureImage.gameObject.SetActive(false);
        if (frameImage != null) frameImage.gameObject.SetActive(false);

        imageCoroutine = null;
    }

    public void HideImage()
    {
        if (imageCoroutine != null)
        {
            StopCoroutine(imageCoroutine);
            imageCoroutine = null;
        }

        pictureImage.gameObject.SetActive(false);
        if (frameImage != null) frameImage.gameObject.SetActive(false);
    }
}
