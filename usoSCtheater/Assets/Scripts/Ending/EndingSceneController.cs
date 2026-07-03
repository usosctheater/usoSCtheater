using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UsoSCTheater.Ending
{
    /// <summary>
    /// 엔딩 씬(스탭 롤) 전체 진행을 관리한다.
    /// 트랜지션 종료 후 외부에서 StartEnding()을 호출하면
    /// 크레딧 로드 → 스크롤/SD Spine/BGM 동시 시작 → 계산된 시간 후 전체 비활성화 순으로 진행한다.
    /// </summary>
    public class EndingSceneController : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject endingPanelRoot; // 좌/우 패널을 포함하는 최상위 오브젝트

        [Header("Components")]
        [SerializeField] private EndingCreditsScroller creditsScroller;
        [SerializeField] private EndingSDSpineController sdSpineController;
        [SerializeField] private EndingBGMPlayer bgmPlayer;

        [Header("Settings")]
        [SerializeField] private EndingCreditsLoader.Language language = EndingCreditsLoader.Language.KR;

        private Coroutine _endingRoutine;

        private void Start()
        {
            StartEnding();
        }

        /// <summary>
        /// 트랜지션이 끝난 직후 외부(씬 흐름 관리자 등)에서 호출한다.
        /// appearedIdolNames: 이번 시나리오에 등장한 캐릭터 이름 목록 (예: "Mei", "Asahi")
        /// </summary>
        public void StartEnding()
        {
            endingPanelRoot.SetActive(true);

            List<string> lines = EndingCreditsLoader.LoadLines(language);
            float totalTime = creditsScroller.Setup(lines);

            sdSpineController.Setup();
            bgmPlayer.Play();
            creditsScroller.StartScrolling();

            if (_endingRoutine != null)
            {
                StopCoroutine(_endingRoutine);
            }
            _endingRoutine = StartCoroutine(EndAfterDelay(totalTime));
        }

        private IEnumerator EndAfterDelay(float totalTime)
        {
            yield return new WaitForSeconds(totalTime);
            FinishEnding();
        }

        private void FinishEnding()
        {
            creditsScroller.StopScrolling();
            sdSpineController.StopAndClear();
            bgmPlayer.Stop();
            endingPanelRoot.SetActive(false);
        }
    }
}
