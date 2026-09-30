using System;

namespace UsoSCTheater.Recording
{
    /// <summary>
    /// 녹화 시작/종료 신호를 전달하는 순수 런타임 클래스.
    /// UnityEditor를 참조하지 않으므로 빌드에 포함되어도 안전하다.
    /// 에디터 녹화 도구(VNRecorderTool)가 이벤트를 구독해서 StartRecording / StopRecording을 호출하는 방식으로 연결한다.
    /// (SceneTransitionManager.OnBeforeLoad와 달리 Play마다 초기화되지 않으므로 에디터 도구의 구독이 유지된다)
    /// </summary>
    public static class RecordingSignal
    {
        //[녹화] 재생 시작 신호: (시나리오 폴더명, 막 이름). 막 이름 null = 모두 재생
        public static event Action<string, string> OnRecordingStartRequested;

        public static event Action OnRecordingStopRequested;

        /// <summary>
        /// [녹화] ScenarioPlayer가 재생을 시작할 때 호출한다. 녹화 모드가 꺼져 있거나 구독자가 없으면 무시된다.
        /// actName: 단일 막 재생이면 막 이름, 모두 재생이면 null
        /// </summary>
        public static void RequestStart(string scenarioFolder, string actName)
        {
            OnRecordingStartRequested?.Invoke(scenarioFolder, actName);
        }

        /// <summary>
        /// 재생 종료 지점(엔딩 종료, 단일 막 종료, ESC 강제 종료)에서 호출한다.
        /// 구독자가 없으면(=에디터 녹화 도구가 없는 빌드 환경) 아무 동작도 하지 않는다.
        /// </summary>
        public static void RequestStop()
        {
            OnRecordingStopRequested?.Invoke();
        }
    }
}
