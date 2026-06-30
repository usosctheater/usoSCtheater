using System.IO;
using Spine.Unity;
using UnityEngine;

// Assets/Scripts/SpineSnapshotExporter.cs
// 디버그 씬의 DebugCamera에 붙여서 사용
public class SpineSnapshotExporter : MonoBehaviour
{
    [Header("캡처용 카메라 (DebugCamera 연결)")]
    public Camera captureCamera;

    [Header("SpineDebugManager 참조 (현재 선택된 스파인을 가져옴)")]
    public SpineDebugManager spineDebugManager;

    [Header("저장 폴더 (절대경로 또는 프로젝트 상대경로)")]
    public string saveFolder = "SpineSnapshots";

    [Header("스파인 Native Size 사용 여부")]
    public bool useSpineNativeSize = true;

    [Header("캡처 해상도 (useSpineNativeSize가 false일 때만 사용)")]
    public int width = 1024;
    public int height = 1024;

    [Header("배경 투명 처리 여부")]
    public bool transparentBackground = true;

    [Header("저장 핫키")]
    public KeyCode captureKey = KeyCode.F5;

    void Update()
    {
        if (Input.GetKeyDown(captureKey))
        {
            CaptureSnapshot();
        }
    }

    void CaptureSnapshot()
    {
        if (captureCamera == null)
        {
            Debug.LogError("[SpineSnapshotExporter] captureCamera가 지정되지 않았습니다.");
            return;
        }

        int captureWidth = width;
        int captureHeight = height;

        if (useSpineNativeSize)
        {
            if (spineDebugManager == null)
            {
                Debug.LogError("[SpineSnapshotExporter] spineDebugManager가 지정되지 않았습니다.");
                return;
            }

            SkeletonAnimation skeletonAnimation = spineDebugManager.CurrentSkeletonAnimation;

            if (skeletonAnimation == null || skeletonAnimation.Skeleton == null)
            {
                Debug.LogError("[SpineSnapshotExporter] 현재 선택된 스파인이 없습니다.");
                return;
            }

            captureWidth = Mathf.RoundToInt(skeletonAnimation.Skeleton.Data.Width);
            captureHeight = Mathf.RoundToInt(skeletonAnimation.Skeleton.Data.Height);

            if (captureWidth <= 0 || captureHeight <= 0)
            {
                Debug.LogWarning("[SpineSnapshotExporter] Native Size가 0 이하입니다. 기본 해상도를 사용합니다.");
                captureWidth = width;
                captureHeight = height;
            }
        }

        // 1. RenderTexture 준비 (알파 채널 포함 포맷)
        RenderTexture rt = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture prevRT = captureCamera.targetTexture;
        CameraClearFlags prevClearFlags = captureCamera.clearFlags;
        Color prevBgColor = captureCamera.backgroundColor;

        if (transparentBackground)
        {
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = new Color(0, 0, 0, 0);
        }

        captureCamera.targetTexture = rt;
        captureCamera.Render();

        // 2. RenderTexture -> Texture2D
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
        tex.Apply();

        // 3. 원래 상태 복구 (SpinePreviewRenderTexture 등으로 되돌림)
        captureCamera.targetTexture = prevRT;
        captureCamera.clearFlags = prevClearFlags;
        captureCamera.backgroundColor = prevBgColor;
        RenderTexture.active = prevActive;
        rt.Release();

        // 4. PNG 인코딩 및 저장
        byte[] pngData = tex.EncodeToPNG();
        Destroy(tex);

        string folderPath = Path.IsPathRooted(saveFolder)
            ? saveFolder
            : Path.Combine(Application.dataPath, "..", saveFolder);

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // === 변경: 파일명을 날짜 대신 스파인명/트랙별 애니메이션명/트랙시간으로 구성 ===
        string spineName = spineDebugManager != null ? spineDebugManager.CurrentSpineName : "Unknown";

        System.Collections.Generic.List<string> nameParts = new System.Collections.Generic.List<string> { "SpineSnapshot", spineName };

        float track0Time = 0f;
        SkeletonAnimation currentAnim = spineDebugManager != null ? spineDebugManager.CurrentSkeletonAnimation : null;

        if (currentAnim != null)
        {
            for (int i = 0; i <= 3; i++)
            {
                var entry = currentAnim.AnimationState.GetCurrent(i);
                if (entry != null)
                {
                    nameParts.Add(entry.Animation.Name);
                    if (i == 0) track0Time = entry.TrackTime;
                }
            }
        }

        nameParts.Add(track0Time.ToString("F2"));

        string fileName = string.Join("-", nameParts) + ".png";
        string fullPath = Path.Combine(folderPath, fileName);

        File.WriteAllBytes(fullPath, pngData);
        Debug.Log($"[SpineSnapshotExporter] 저장 완료: {fullPath}");
    }
}
