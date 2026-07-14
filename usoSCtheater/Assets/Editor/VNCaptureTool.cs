// [캡처] TEXT 라인 자동 캡처 활성화 토글 에디터 창
using UnityEditor;
using UnityEngine;
using UsoSCTheater.Capture;

namespace UsoSCTheater.Editor
{
    public class VNCaptureTool : EditorWindow
    {
        [MenuItem("Tools/VN Capture Tool")]
        private static void ShowWindow()
        {
            GetWindow<VNCaptureTool>("VN Capture");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("TEXT 라인 자동 캡처", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            bool enabled = SceneCaptureUtil.CaptureEnabled;
            bool newEnabled = EditorGUILayout.Toggle("캡처 활성화", enabled);
            if (newEnabled != enabled) SceneCaptureUtil.CaptureEnabled = newEnabled;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "활성화 시 플레이모드에서 오토플레이로 TEXT 라인이 종료될 때마다\n" +
                "자동으로 스크린샷을 캡처합니다 (1920x1080 고정).\n" +
                "저장 경로: <프로젝트 루트>/CaptureOutput/{씬 이름}/\n" +
                "DialogManager의 captureCamera 필드에 캡처용 카메라 할당 필요.",
                MessageType.Info);
        }
    }
}
