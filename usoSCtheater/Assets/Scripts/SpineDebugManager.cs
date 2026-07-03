using System.Collections.Generic;
using System.Net.WebSockets;
using NUnit.Framework;
using Spine;
using Spine.Unity;
using TMPro;
// using UnityEditor.EditorTools;
// using UnityEditor.Search;
using UnityEngine;
using UnityEngine.UI;

public class SpineDebugManager : MonoBehaviour
{
    [Header("Spine 오브젝트 목록")]
    [SerializeField] private List<GameObject> spineObjects;

    [Header("UI - 왼쪽 패널")]
    [SerializeField] private TMP_Dropdown spineSelector;                //Spine 선택 드롭다운
    [SerializeField] private Transform animListContent;                 //애니메이션 리스트 부모
    [SerializeField] private GameObject animListItemPrefab;             //리스트 아이템 프리팹
    [SerializeField] private TMP_Dropdown trackSelector;                //트랙 선택 드롭다운 (0 ~ 3)

    [Header("UI - 오른쪽 패널")]
    [SerializeField] private TextMeshProUGUI trackInfoText;             //트랙별 애니메이션 정보
    [SerializeField] private TextMeshProUGUI timeInfoText;              //재생 시간 정보
    [SerializeField] private Transform boneListContent;                 //본 리스트 부모
    [SerializeField] private GameObject boneListItemPrefab;             //본 리스트 아이템 프리팹

    [Header("UI - 하단 컨트롤")]
    [SerializeField] private Button playPauseButton;
    [SerializeField] private TextMeshProUGUI playPauseText;
    [SerializeField] private Slider timeSlider;

    private GameObject currentSpineObj;
    private SkeletonAnimation currentSkAnim;
    private bool isPaused = false;
    private bool isSliderDragging = false;

    // === 추가: 외부(SpineSnapshotExporter 등)에서 현재 선택된 스파인을 읽을 수 있도록 public 프로퍼티 노출 ===
    public SkeletonAnimation CurrentSkeletonAnimation => currentSkAnim;

    // === 추가: 현재 선택된 스파인 오브젝트 이름 노출 ===
    public string CurrentSpineName => currentSpineObj != null ? currentSpineObj.name : "Unknown";

    void Start()
    {
        //Spine 선택 드롭다운 초기화
        spineSelector.ClearOptions();
        List<string> spineNames = new List<string>();
        foreach (var obj in spineObjects)   spineNames.Add(obj.name);
        spineSelector.AddOptions(spineNames);
        spineSelector.onValueChanged.AddListener(OnSpineSelected);

        //트랙 드롭다운 초기화
        trackSelector.ClearOptions();
        trackSelector.AddOptions(new List<string> { "Track 0", "Track 1", "Track 2", "Track 3" });

        //재생 및 일시정지 버튼
        playPauseButton.onClick.AddListener(OnPlayPauseClicked);

        //슬라이더
        timeSlider.onValueChanged.AddListener(OnSliderChanged);

        // 첫번째 Spine 선택
        if (spineObjects.Count > 0) OnSpineSelected(0);
    }

    void Update()
    {
        if (currentSkAnim == null) return;

        UpdateTrackInfo();
        UpdateTimeInfo();
        UpdateBoneList();

        //슬라이더 드래그 중이 아닐 때만 슬라이더 값 갱신
        if (!isSliderDragging && !isPaused)
        {
            var entry = currentSkAnim.AnimationState.GetCurrent(0);
            if (entry != null && entry.Animation.Duration > 0f)
            {
                //Duration 넘으면 루프
                float loopedTime = entry.TrackTime % entry.Animation.Duration;
                timeSlider.SetValueWithoutNotify(loopedTime / entry.Animation.Duration);
            }
            
        }
    }

    private void OnSpineSelected(int index)
    {
        //기존 스파인 모든 트랙 초기화
        if (currentSkAnim != null)
        {
            currentSkAnim.AnimationState.ClearTracks();
            currentSkAnim.Skeleton.SetToSetupPose();
        }

        //모든 Spine 비활성화
        foreach (var obj in spineObjects) obj.SetActive(false);

        currentSpineObj = spineObjects[index];
        currentSpineObj.SetActive(true);
        currentSkAnim = currentSpineObj.GetComponent<SkeletonAnimation>();

        isPaused = false;
        currentSkAnim.timeScale = 1f;
        playPauseText.text = "일시정지";

        //트랙 드롭다운도 초기화
        trackSelector.SetValueWithoutNotify(0);

        RefreshAnimList();
    }

    private void OnPlayPauseClicked()
    {
        isPaused = !isPaused;
        currentSkAnim.timeScale = isPaused ? 0f : 1f;
        playPauseText.text = isPaused ? "재생" : "일시정지";
    }

    private void OnSliderChanged(float value)
    {
        if (!isPaused || currentSkAnim == null) return;

        isSliderDragging = true;

        for (int i = 0; i <= 3; i++)
        {
            var entry = currentSkAnim.AnimationState.GetCurrent(i);
            if (entry != null) entry.trackTime = entry.Animation.Duration * value;
        }

        isSliderDragging = false;
    }
    
    //애니메이션 리스트 갱신
    private void RefreshAnimList()
    {
        //기존 리스트 제거
        foreach (Transform child in animListContent) Destroy(child.gameObject);

        if (currentSkAnim == null) return;

        //트랙 초기화용 None 아이템
        GameObject noneItem = Instantiate(animListItemPrefab, animListContent);
        TextMeshProUGUI noneLabel = noneItem.GetComponentInChildren<TextMeshProUGUI>();
        Button noneBtn = noneItem.GetComponent<Button>();
        noneLabel.text = "None";
        noneBtn.onClick.AddListener(() => onAnimitemClicked(null));

        foreach (var anim in currentSkAnim.skeleton.Data.Animations)
        {
            GameObject item = Instantiate(animListItemPrefab, animListContent);
            TextMeshProUGUI label = item.GetComponentInChildren<TextMeshProUGUI>();
            Button btn = item.GetComponent<Button>();

            string animName = anim.Name;
            float duration = anim.Duration;

            label.text = $"{animName} ({duration:F2}s)";

            //클릭 시 해당 애니메이션을 선택된 트랙에 적용
            btn.onClick.AddListener(() => onAnimitemClicked(animName));
        }
    }

    //애니메이션 리스트 아이템 클릭 시
    private void onAnimitemClicked(string animName)
    {
        if (currentSkAnim == null) return;

        int track = trackSelector.value;

        //None 선택 시 트랙 초기화
        if (animName == null)
        {
            if (track == 0)
            {
                currentSkAnim.AnimationState.ClearTracks();
                currentSkAnim.skeleton.SetToSetupPose();
            }
            else
            {
                currentSkAnim.AnimationState.ClearTrack(track);    
            }
            
            return;
        }

        currentSkAnim.AnimationState.SetAnimation(track, animName, track == 0);

        //트랙이 0이 아니면 Complete시 마지막 프레임 고정
        if (track != 0)
        {
            var entry = currentSkAnim.AnimationState.GetCurrent(track);
            if (entry != null)
            {
                entry.Complete += (TrackEntry) =>
                {
                    TrackEntry.TimeScale = 0f;
                };
            }
        }
    }

    private void UpdateTrackInfo()
    {
        string info = "";
        for (int i = 0; i <= 3; i++)
        {
            var entry = currentSkAnim.AnimationState.GetCurrent(i);
            if (entry != null) info += $"[Track {i}]\n{entry.Animation.Name}\n{entry.TrackTime:F2}s / {entry.Animation.Duration:F2}s\n";
            else info += $"[Track {i} : None ]\n";
        }

        trackInfoText.text = info;
    }

    private void UpdateTimeInfo()
    {
        var entry = currentSkAnim.AnimationState.GetCurrent(0);
        if (entry != null)
            timeInfoText.text = $"[{entry.TrackTime:F2}s / {entry.Animation.Duration:F2}s]";
        else
            timeInfoText.text = "[0s / 0s]";
    }

    private void UpdateBoneList()
    {
        foreach (Transform child in boneListContent) Destroy(child.gameObject);

        foreach (var bone in currentSkAnim.Skeleton.Bones)
        {
            //기본 포즈에서 변화가 있는 본만 표시
            if (Mathf.Abs(bone.rotation) > 0.1f || Mathf.Abs(bone.x) > 0.1f || Mathf.Abs(bone.y) > 0.1f)
            {
                GameObject item = Instantiate(boneListItemPrefab, boneListContent);
                TextMeshProUGUI label = item.GetComponentInChildren<TextMeshProUGUI>();
                label.text = $"{bone.Data.Name} (rot: {bone.Rotation:F1})";
            }
        }
    }
}
