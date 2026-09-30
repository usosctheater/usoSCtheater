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
    /// Play 진입 시 자동으로 Game View를 mp4로 녹화 시작하고,
    /// RecordingSignal(엔딩 FinishEnding 등)로부터 종료 신호를 받으면 자동으로 정지/저장한다.
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

        private static RecorderController _recorderController;

        static VNRecorderTool()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
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
            if (state == PlayModeStateChange.EnteredPlayMode && IsRecordingModeEnabled())
            {
                StartRecording();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // Play를 중간에 강제 종료한 경우에도 미완성 파일이 남지 않도록 정리
                StopRecording();
            }
        }

        private static void StartRecording()
        {
            // [녹화] 녹화 모드로 Play 진입 시 자동재생을 강제로 켜준다
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

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string scenarioName = GetScenarioName();
            string outputFolder = Path.Combine(Application.dataPath, "..", "Recordings", scenarioName);
            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }
            movieSettings.OutputFile = Path.Combine(outputFolder, timestamp);

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

        // [녹화] 파일명용 시나리오 이름: ScenarioPlayer.defaultScenarioFolder("NKS", 기존 값 "Scene/NKS"도 마지막 조각 사용)
        // defaultScenarioFolder는 private SerializeField라 SerializedObject로 인스펙터 값을 직접 읽는다
        // [용어 정리] SceneManager.scenePath → ScenarioPlayer.defaultScenarioFolder (4단계에서 런타임 선택값 기준으로 변경 예정)
        private static string GetScenarioName()
        {
            const string Fallback = "Unknown";

            var scenarioPlayer = UnityEngine.Object.FindFirstObjectByType<global::ScenarioPlayer>();
            if (scenarioPlayer == null)
            {
                Debug.LogWarning("[VNRecorderTool] ScenarioPlayer를 찾을 수 없어 시나리오 이름을 Unknown으로 저장합니다.");
                return Fallback;
            }

            var prop = new SerializedObject(scenarioPlayer).FindProperty("defaultScenarioFolder");
            string scenarioFolder = prop != null ? prop.stringValue : null;
            if (string.IsNullOrEmpty(scenarioFolder)) return Fallback;

            string name = scenarioFolder.TrimEnd('/').Split('/')[^1];
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrEmpty(name) ? Fallback : name;
        }
    }
}
