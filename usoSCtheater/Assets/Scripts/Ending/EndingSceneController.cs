using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UsoSCTheater.Recording; // [녹화] 종료 신호용
using UsoSCTheater.SceneFlow; // [씬 전환] 엔딩 종료 후 목록 씬 복귀

namespace UsoSCTheater.Ending
{
    /// <summary>
    /// 엔딩 씬 전체 진행을 관리한다.
    /// 씬 시작 시 SD Spine/BGM을 시작하고, 정해진 시간이 지나거나 좌클릭 스킵하면 종료 후 목록 씬으로 돌아간다.   // [크레딧 폐기] 설명 수정
    /// </summary>
    public class EndingSceneController : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject endingPanelRoot; // 좌/우 패널을 포함하는 최상위 오브젝트

        [Header("Components")]
        [SerializeField] private EndingSDSpineController sdSpineController;
        [SerializeField] private EndingBGMPlayer bgmPlayer;

        [Header("Skip")]
        [SerializeField] private float skipInputDelay = 1f;   // [스킵] 엔딩 시작 후 이 시간(초) 동안은 좌클릭 스킵 무시 (대사 연타 클릭이 넘어오는 것 방지)

        private Coroutine _endingRoutine;
        private float _endingStartTime;   // [스킵] 스킵 입력 유예 계산용
        private bool _isFinished;         // [스킵] FinishEnding 중복 실행 방지 (타이머 종료 / 좌클릭 스킵)

        private void Start()
        {
            StartEnding();
        }

        // [스킵] 좌클릭 시 타이머를 기다리지 않고 즉시 엔딩 종료 처리 (엔딩 씬에는 별도 상호작용 버튼이 없음)
        private void Update()
        {
            if (_isFinished) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (Time.time - _endingStartTime < skipInputDelay) return;

            Debug.Log("[EndingSceneController] 좌클릭 스킵 → 엔딩 종료");
            FinishEnding();
        }

        /// <summary>
        /// 트랜지션이 끝난 직후 외부(씬 흐름 관리자 등)에서 호출한다.
        /// appearedIdolNames: 이번 시나리오에 등장한 캐릭터 이름 목록 (예: "Mei", "Asahi")
        /// </summary>
        public void StartEnding()
        {
            _isFinished = false;               // [스킵]
            _endingStartTime = Time.time;      // [스킵]
            endingPanelRoot.SetActive(true);

            float totalTime = 30f;   // [크레딧 폐기] 엔딩 재생 시간 (좌클릭으로 스킵 가능)

            sdSpineController.Setup();
            bgmPlayer.Play();

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

        // 엔딩 종료 처리. 타이머 종료(EndAfterDelay)와 좌클릭 스킵(Update) 모두 여기를 거친다
        private void FinishEnding()
        {
            if (_isFinished) return;   // [스킵] 중복 실행 방지
            _isFinished = true;

            if (_endingRoutine != null)   // [스킵] 스킵으로 들어온 경우 타이머 중단
            {
                StopCoroutine(_endingRoutine);
                _endingRoutine = null;
            }

            sdSpineController.StopAndClear();
            bgmPlayer.Stop();
            endingPanelRoot.SetActive(false);

            // [녹화] 엔딩 종료 시점에 녹화 중지 신호 전달 (구독자 없으면 무해하게 무시됨)
            RecordingSignal.RequestStop();

            // [씬 전환] 모두 재생의 끝 → 목록 씬 복귀
            SceneTransitionManager.GoToScenarioSelectScene();
        }
    }
}
