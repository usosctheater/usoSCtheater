using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UsoSCTheater.SceneFlow
{
    /// <summary>
    /// 16:9 레터박스 (스테이지 레이아웃 4회차) — 창 비율이 16:9가 아니면 카메라 화면 영역을 16:9로 줄이고 남는 곳(좌우 또는 위아래 막대)은 검정.
    /// 씬에 붙일 것 없음: 게임 시작 때 1번 만들고 씬이 바뀌어도 유지 (DontDestroyOnLoad). 다시 계산은 화면 크기·씬이 바뀐 프레임에만
    /// 카메라 방식 캔버스는 이 영역에 맞춰지고 CanvasScaler가 1920×1080 기준으로 줄이거나 키움 → 캔버스 안은 늘 1920×1080단위
    /// Overlay 캔버스는 카메라 영역을 따르지 않으므로 쓰지 않음 (Canvas_Transition·목록 Canvas는 카메라 방식)
    /// </summary>
    public sealed class Letterbox : MonoBehaviour
    {
        private const float TargetAspect = 1920f / 1080f;
        private const float BarsDepth = -100f;   //씬 카메라(-1·0)보다 먼저 화면 전체를 검정으로 지움

        private Camera bars;
        private readonly List<Camera> cameras = new List<Camera>();
        private int lastWidth, lastHeight;
        private bool dirty = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject(nameof(Letterbox));
            DontDestroyOnLoad(go);
            go.AddComponent<Letterbox>();
        }

        private void Awake()
        {
            bars = gameObject.AddComponent<Camera>();
            bars.clearFlags = CameraClearFlags.SolidColor;
            bars.backgroundColor = Color.black;
            bars.cullingMask = 0;   //아무것도 그리지 않고 지우기만
            bars.depth = BarsDepth;
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => dirty = true;

        private void LateUpdate()
        {
            if (!dirty && Screen.width == lastWidth && Screen.height == lastHeight) return;
            dirty = false;
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            if (lastWidth <= 0 || lastHeight <= 0) return;

            float window = (float)lastWidth / lastHeight;
            Rect rect = window > TargetAspect
                ? new Rect((1f - TargetAspect / window) / 2f, 0f, TargetAspect / window, 1f)    //넓음 → 좌우 막대
                : new Rect(0f, (1f - window / TargetAspect) / 2f, 1f, window / TargetAspect);   //높음 → 위아래 막대

            cameras.Clear();
            cameras.AddRange(Camera.allCameras);
            foreach (Camera cam in cameras)
                if (cam != bars) cam.rect = rect;
        }
    }
}
