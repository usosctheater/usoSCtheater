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
            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();

            var movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = "VN_MovieRecorder";
            movieSettings.Enabled = true;
            movieSettings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string outputFolder = Path.Combine(Application.dataPath, "..", "Recordings");
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
    }
}
