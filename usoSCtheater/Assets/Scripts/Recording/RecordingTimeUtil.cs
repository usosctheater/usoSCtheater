using UnityEngine;

namespace UsoSCTheater.Recording
{
    /// <summary>
    /// 대사 진행 등 시나리오 페이싱에 쓰이는 대기 인스트럭션을 환경에 따라 다르게 생성한다.
    /// - 에디터(UNITY_EDITOR): WaitForSeconds (델타 타임 기준) → Recorder Cap 해제 가속에 올라탐
    /// - 빌드: WaitForSecondsRealtime (실제 시간 기준) → 백로그 등 Time.timeScale=0 일시정지와 호환
    /// UnityEditor 네임스페이스를 참조하지 않으므로 빌드에 포함되어도 안전하다.
    /// </summary>
    public static class RecordingTimeUtil
    {
        public static object PacingWait(float seconds)
        {
#if UNITY_EDITOR
            return new WaitForSeconds(seconds);
#else
            return new WaitForSecondsRealtime(seconds);
#endif
        }
    }
}
