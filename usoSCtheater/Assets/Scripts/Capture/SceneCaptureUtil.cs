// [캡처] TEXT 라인 자동 스크린샷 유틸리티
// AsyncGPUReadback 기반 - 1920x1080 고정 해상도, 논블로킹 캡처
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UsoSCTheater.Capture
{
    public static class SceneCaptureUtil
    {
        private const int CaptureWidth = 1920;
        private const int CaptureHeight = 1080;
        private const string EditorPrefKey = "VNCapture_Enabled";
        private const string TurboEnabledKey = "VNCapture_TurboEnabled";
        private const string TurboFramerateKey = "VNCapture_TurboFramerate";

        // 캡처 기능 활성화 여부 (VNCaptureTool 에디터 창에서 토글)
        public static bool CaptureEnabled
        {
#if UNITY_EDITOR
            get => EditorPrefs.GetBool(EditorPrefKey, false);
            set => EditorPrefs.SetBool(EditorPrefKey, value);
#else
            get => false;
            set { /* 빌드에서는 항상 비활성 */ }
#endif
        }

        // [고속 모드] Recorder 없이도 Time.captureFramerate를 직접 걸어 가속 재생
        public static bool TurboEnabled
        {
#if UNITY_EDITOR
            get => EditorPrefs.GetBool(TurboEnabledKey, false);
            set => EditorPrefs.SetBool(TurboEnabledKey, value);
#else
            get => false;
            set { }
#endif
        }

        // 고속 모드 시 사용할 고정 프레임레이트. 낮을수록(적을수록) 가속 배율이 커짐
        // (실제 프레임을 낼 수 있는 만큼 빠르게 진행 + 매 프레임 시뮬레이션 시간만 1/N로 고정되기 때문)
        public static int TurboFramerate
        {
#if UNITY_EDITOR
            get => EditorPrefs.GetInt(TurboFramerateKey, 30);
            set => EditorPrefs.SetInt(TurboFramerateKey, value);
#else
            get => 0;
            set { }
#endif
        }

        // TEXT 라인 1개 캡처. AutoPlayCoroutine에서 해당 라인 종료 시점에 호출.
        // sceneName: 현재 씬 XML 이름, lineIndex: scriptNodes 상의 인덱스, targetCamera: 캡처 대상 카메라
        public static void CaptureLine(string sceneName, int lineIndex, Camera targetCamera)
        {
#if UNITY_EDITOR
            if (!CaptureEnabled) return;

            if (targetCamera == null)
            {
                Debug.LogWarning("[SceneCaptureUtil] captureCamera가 지정되지 않아 캡처를 건너뜁니다.");
                return;
            }

            var rt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            var prevTarget = targetCamera.targetTexture;

            // 고정 해상도로 카메라를 직접 렌더 (Game 뷰 창 크기와 무관)
            targetCamera.targetTexture = rt;
            targetCamera.Render();
            targetCamera.targetTexture = prevTarget;

            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, request =>
            {
                rt.Release();
                Object.DestroyImmediate(rt);

                if (request.hasError)
                {
                    Debug.LogWarning("[SceneCaptureUtil] AsyncGPUReadback 실패 - 캡처 건너뜀");
                    return;
                }

                byte[] rawData = request.GetData<byte>().ToArray();
                byte[] pngBytes = ImageConversion.EncodeArrayToPNG(
                    rawData,
                    UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                    (uint)CaptureWidth, (uint)CaptureHeight);

                string folder = GetOutputFolder(sceneName);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"{lineIndex:D4}.png");

                // 파일 저장은 별도 스레드에서 (메인 스레드 진행 방해 없음, 강제 덮어쓰기)
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { File.WriteAllBytes(path, pngBytes); }
                    catch (System.Exception e) { Debug.LogWarning($"[SceneCaptureUtil] 저장 실패: {e.Message}"); }
                });
            });
#endif
        }

#if UNITY_EDITOR
        // 저장 폴더: <프로젝트 루트>/CaptureOutput/{씬 이름}/ (Assets 밖 - Unity 임포트 대상 제외)
        private static string GetOutputFolder(string sceneName)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "CaptureOutput", sceneName);
        }
#endif
    }
}
