using System;

namespace UsoSCTheater.Recording
{
    /// <summary>
    /// 녹화 종료 신호를 전달하는 순수 런타임 클래스.
    /// UnityEditor를 참조하지 않으므로 빌드에 포함되어도 안전하다.
    /// 에디터 녹화 도구(VNRecorderTool)가 OnRecordingStopRequested를 구독해서
    /// StopRecording()을 호출하는 방식으로 연결한다.
    /// </summary>
    public static class RecordingSignal
    {
        public static event Action OnRecordingStopRequested;

        /// <summary>
        /// 엔딩 등 시나리오 종료 지점에서 호출한다.
        /// 구독자가 없으면(=에디터 녹화 도구가 없는 빌드 환경) 아무 동작도 하지 않는다.
        /// </summary>
        public static void RequestStop()
        {
            OnRecordingStopRequested?.Invoke();
        }
    }
}
