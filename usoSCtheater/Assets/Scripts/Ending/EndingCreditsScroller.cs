using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace UsoSCTheater.Ending
{
    /// <summary>
    /// 좌측 패널의 크레딧 텍스트를 고정 속도로 아래에서 위로 스크롤한다.
    /// 총 소요 시간을 계산해 EndingSceneController가 종료 타이밍을 잡을 수 있게 한다.
    /// </summary>
    public class EndingCreditsScroller : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private RectTransform scrollContainer; // 텍스트를 담는 RectTransform (이동 대상)
        [SerializeField] private TMP_Text creditsText;           // 실제 텍스트 컴포넌트
        [SerializeField] private RectTransform viewportRect;    // 좌측 패널 영역 (화면 높이 기준)

        [Header("Scroll Settings")]
        [SerializeField] private float scrollSpeed = 40f; // px/sec, 고정 속도

        private float _totalScrollDistance;
        private float _elapsed;
        private bool _isScrolling;

        /// <summary>
        /// 줄 리스트를 받아 텍스트를 세팅하고 시작 위치를 잡는다.
        /// 반환값은 스크롤이 끝나기까지 걸리는 총 시간(초)이다.
        /// </summary>
        public float Setup(List<string> lines)
        {
            var sb = new StringBuilder();
            foreach (string line in lines)
            {
                sb.AppendLine(line);
            }
            creditsText.text = sb.ToString();

            // 강제로 레이아웃 갱신해서 텍스트 높이를 정확히 구한다.
            Canvas.ForceUpdateCanvases();
            creditsText.ForceMeshUpdate();

            float textHeight = creditsText.preferredHeight;
            float viewportHeight = viewportRect.rect.height;

            // 시작 위치: 화면 아래쪽 바깥 (viewport 아래)
            // 종료 위치: 텍스트가 화면 위쪽 바깥으로 완전히 사라질 때까지
            _totalScrollDistance = textHeight + viewportHeight;

            Vector2 startPos = scrollContainer.anchoredPosition;
            startPos.y = -viewportHeight; // 화면 바로 아래에서 시작
            scrollContainer.anchoredPosition = startPos;

            float totalTime = _totalScrollDistance / scrollSpeed;
            return totalTime;
        }

        public void StartScrolling()
        {
            _elapsed = 0f;
            _isScrolling = true;
        }

        public void StopScrolling()
        {
            _isScrolling = false;
        }

        private void Update()
        {
            if (!_isScrolling)
            {
                return;
            }

            _elapsed += Time.deltaTime;

            Vector2 pos = scrollContainer.anchoredPosition;
            pos.y = -viewportRect.rect.height + (scrollSpeed * _elapsed);
            scrollContainer.anchoredPosition = pos;
        }
    }
}
