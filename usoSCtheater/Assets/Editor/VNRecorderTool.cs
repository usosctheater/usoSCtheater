using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UsoSCTheater.Recording;

namespace UsoSCTheater.EditorTools
{
    /// <summary>
    /// Tools > Recording > Enable Recording Mode 체크박스로 녹화 모드를 켜두면,
    /// ScenarioPlayer의 재생 시작 신호(RecordingSignal.RequestStart)를 받는 시점에 Game View를 mp4로 녹화 시작하고,
    /// 종료 신호(엔딩 종료 / 단일 막 종료 / ESC 강제 종료)를 받으면 자동으로 정지/저장한다.
    /// [녹화 대응] Play 진입 시 자동 시작 → 재생 시작 신호로 변경 (목록 씬은 녹화하지 않음). 한 Play에서 재생할 때마다 파일 1개.
    /// 저장: Recordings/{시나리오}/ALL_{타임스탬프}.mp4 (모두 재생), Recordings/{시나리오}/{막}_{타임스탬프}.mp4 (단일 막)
    /// Recorder 관련 코드는 전부 UnityEditor 참조이므로 빌드에는 포함되지 않는다.
    /// </summary>
    [InitializeOnLoad]
    public static class VNRecorderTool
    {
        private const string MenuPath = "Tools/Recording/Enable Recording Mode";
        private const string PrefKey = "UsoSCTheater_RecordingModeEnabled";

        private const int OutputWidth = 1920;
        private const int OutputHeight = 1080;
        private const float FrameRate = 30f;
        private const string PlayAllPrefix = "ALL";   // [녹화 대응] 모두 재생 파일 접두어

        private static RecorderController _recorderController;

        static VNRecorderTool()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RecordingSignal.OnRecordingStartRequested += OnRecordingStartRequested;   // [녹화 대응] 재생 시작 신호로 녹화 시작
            RecordingSignal.OnRecordingStopRequested += StopRecording;
        }

        [MenuItem(MenuPath)]
        private static void ToggleRecordingMode()
        {
            bool current = IsRecordingModeEnabled();
            EditorPrefs.SetBool(PrefKey, !current);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggleRecordingMode()
        {
            Menu.SetChecked(MenuPath, IsRecordingModeEnabled());
            return true;
        }

        private static bool IsRecordingModeEnabled()
        {
            return EditorPrefs.GetBool(PrefKey, false);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // [녹화 대응] EnteredPlayMode 자동 시작 제거 → 녹화 시작은 OnRecordingStartRequested에서만
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // Play를 중간에 강제 종료한 경우에도 미완성 파일이 남지 않도록 정리
                StopRecording();
            }
        }

        // [녹화 대응] ScenarioPlayer 재생 시작 신호. actName null = 모두 재생
        private static void OnRecordingStartRequested(string scenarioFolder, string actName)
        {
            if (!IsRecordingModeEnabled()) return;

            // 이전 녹화가 남아 있으면 먼저 저장 (정상 흐름에서는 종료 신호로 이미 정리됨)
            StopRecording();
            StartRecording(scenarioFolder, actName);
        }

        private static void StartRecording(string scenarioFolder, string actName)
        {
            // [녹화] 녹화 시작 시 자동재생을 강제로 켜준다 (재생 시작 시점이라 UIManager가 항상 존재)
            var uiManager = UnityEngine.Object.FindFirstObjectByType<UIManager>();
            if (uiManager != null)
            {
                uiManager.EnableAutoPlay();
            }
            else
            {
                Debug.LogWarning("[VNRecorderTool] UIManager를 찾을 수 없어 자동재생을 켜지 못했습니다.");
            }

            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();

            var movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = "VN_MovieRecorder";
            movieSettings.Enabled = true;
            movieSettings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;

            // [녹화 대응] 파일명: {ALL 또는 막 이름}_{타임스탬프}, 폴더: 시나리오 (런타임 선택값 기준)
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string scenarioName = SanitizeFileName(scenarioFolder, "Unknown");
            string prefix = SanitizeFileName(actName, PlayAllPrefix);
            string outputFolder = Path.Combine(Application.dataPath, "..", "Recordings", scenarioName);
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }
            movieSettings.OutputFile = Path.Combine(outputFolder, $"{prefix}_{timestamp}");

            movieSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = OutputWidth,
                OutputHeight = OutputHeight
            };

            controllerSettings.AddRecorderSettings(movieSettings);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant; // CS1061/CS0103 수정: FrameRatePlaybackType → FrameRatePlayback
            controllerSettings.FrameRate = FrameRate;
            controllerSettings.CapFrameRate = false; // Cap 해제 → 실시간보다 빠르게 캡처

            _recorderController = new RecorderController(controllerSettings);
            _recorderController.PrepareRecording();
            _recorderController.StartRecording();

            Debug.Log($"[VNRecorderTool] 녹화 시작 → {movieSettings.OutputFile}.mp4");
        }

        private static void StopRecording()
        {
            if (_recorderController != null && _recorderController.IsRecording())
            {
                _recorderController.StopRecording();
                Debug.Log("[VNRecorderTool] 녹화 종료 → mp4 저장 완료");
            }
            _recorderController = null;
        }

        // [녹화 대응] 인스펙터 값(SerializedObject)으로 시나리오 이름을 읽던 GetScenarioName() 삭제 → 재생 시작 신호의 런타임 선택값 사용
        // 파일/폴더명 불가 문자는 '_'로 치환, 비어 있으면 fallback
        private static string SanitizeFileName(string value, string fallback)
        {
            if (string.IsNullOrEmpty(value)) return fallback;

            string name = value;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrEmpty(name) ? fallback : name;
        }
    }
}
