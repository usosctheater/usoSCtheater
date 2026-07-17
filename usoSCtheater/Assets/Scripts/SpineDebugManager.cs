using System.Collections.Generic;
using System.Linq;
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
    [SerializeField] private Button resetAllButton;              // 추가: 전체 트랙 초기화 버튼 (인스펙터에서 연결, 미할당 시 버튼 기능은 스킵)
    [SerializeField] private KeyCode resetAllKey = KeyCode.R;     // 추가: 전체 트랙 초기화 단축키 (인스펙터에서 변경 가능)

    private GameObject currentSpineObj;
    private SkeletonAnimation currentSkAnim;
    private bool isPaused = false;
    private bool isSliderDragging = false;

    // 변경: 트랙별 우선 노출 접두사 매핑 (트랙 3처럼 접두사가 여러 개면 배열 순서가 곧 우선순위)
    private static readonly string[] knownPrefixes = { "face_", "lip_", "arm_", "eye_" };   // 변경: eye_ 추가
    private static readonly Dictionary<int, string[]> trackPriorityPrefixes = new Dictionary<int, string[]>
    {
        { 1, new[] { "face_" } },
        { 2, new[] { "lip_" } },
        { 3, new[] { "arm_", "eye_" } }   // 변경: arm_ 다음으로 eye_ 우선순위
    };

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

        // 추가: 트랙 변경 시 우선순위 그룹이 바뀌므로 리스트 재정렬
        trackSelector.onValueChanged.AddListener(_ => RefreshAnimList());

        //재생 및 일시정지 버튼
        playPauseButton.onClick.AddListener(OnPlayPauseClicked);

        //슬라이더
        timeSlider.onValueChanged.AddListener(OnSliderChanged);

        // 추가: 전체 트랙 초기화 버튼 (연결되어 있을 때만)
        if (resetAllButton != null)
            resetAllButton.onClick.AddListener(ResetAllTracks);

        // 첫번째 Spine 선택
        if (spineObjects.Count > 0) OnSpineSelected(0);
    }

    void Update()
    {
        if (currentSkAnim == null) return;

        // 추가: 키보드 입력으로 전체 트랙 초기화
        if (Input.GetKeyDown(resetAllKey)) ResetAllTracks();

        UpdateTrackInfo();
        UpdateTimeInfo();
        // 변경: 본 리스트는 매 프레임 갱신 시 Instantiate/Destroy 부하가 커서 제거함.
        // 대신 onAnimitemClicked에서 애니메이션 선택/해제 시점에 1회만 갱신함.

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

        // 변경: 현재 선택된 트랙 기준으로 우선 노출 그룹 / 나머지 그룹으로 분리
        int currentTrack = trackSelector.value;
        List<Spine.Animation> priorityAnims = new List<Spine.Animation>();
        List<Spine.Animation> restAnims = new List<Spine.Animation>();

        foreach (var anim in currentSkAnim.skeleton.Data.Animations)
        {
            if (IsPriorityAnim(anim.Name, currentTrack))
                priorityAnims.Add(anim);
            else
                restAnims.Add(anim);
        }

        //우선 그룹 먼저, 이후 나머지 그룹 순서로 아이템 생성
        // 변경: 우선 그룹 내부를 접두사 우선순위(GetPriorityOrder) 기준으로 재정렬 (arm_ 다음 eye_ 순서 보장)
        priorityAnims = priorityAnims.OrderBy(a => GetPriorityOrder(a.Name, currentTrack)).ToList();

        foreach (var anim in priorityAnims) CreateAnimListItem(anim);
        foreach (var anim in restAnims) CreateAnimListItem(anim);
    }

    // 변경: 트랙별 우선 노출 대상인지 판별 (targetPrefix 단일 → targetPrefixes 배열 순회로 변경)
    private bool IsPriorityAnim(string animName, int track)
    {
        if (track == 0)
        {
            //Track 0은 알려진 접두사(face_/lip_/arm_/eye_)가 없는 애니메이션이 우선 노출 대상
            foreach (var prefix in knownPrefixes)
            {
                if (animName.StartsWith(prefix)) return false;
            }
            return true;
        }

        if (trackPriorityPrefixes.TryGetValue(track, out string[] targetPrefixes))
        {
            foreach (var prefix in targetPrefixes)
            {
                if (animName.StartsWith(prefix)) return true;
            }
        }

        return false;
    }

    // 추가: 우선 그룹 내부에서의 정렬 순서 (targetPrefixes 배열의 인덱스 = 우선순위, 예: arm_ 이 eye_ 보다 먼저)
    private int GetPriorityOrder(string animName, int track)
    {
        if (trackPriorityPrefixes.TryGetValue(track, out string[] targetPrefixes))
        {
            for (int i = 0; i < targetPrefixes.Length; i++)
            {
                if (animName.StartsWith(targetPrefixes[i])) return i;
            }
        }
        return 0;
    }

    // 추가: 애니메이션 리스트 아이템 생성 (기존 foreach 내부 로직을 분리하여 우선/나머지 그룹 양쪽에서 재사용)
    private void CreateAnimListItem(Spine.Animation anim)
    {
        GameObject item = Instantiate(animListItemPrefab, animListContent);
        TextMeshProUGUI label = item.GetComponentInChildren<TextMeshProUGUI>();
        Button btn = item.GetComponent<Button>();

        string animName = anim.Name;
        float duration = anim.Duration;

        label.text = $"{animName} ({duration:F2}s)";

        btn.onClick.AddListener(() => onAnimitemClicked(animName));
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

            // 추가: 트랙 해제 시점에 본 리스트 1회 갱신
            UpdateBoneList();
            return;
        }

        currentSkAnim.AnimationState.SetAnimation(track, animName, track == 0);

        // 추가: 애니메이션 선택 시점에 본 리스트 1회 갱신
        UpdateBoneList();

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

    // 추가: 버튼 클릭 또는 키보드 입력 시 호출되는 전체 트랙 초기화 함수
    private void ResetAllTracks()
    {
        if (currentSkAnim == null) return;

        currentSkAnim.AnimationState.ClearTracks();
        currentSkAnim.skeleton.SetToSetupPose();

        // 트랙 드롭다운도 0번으로 초기화 (OnSpineSelected와 동일한 관례 유지)
        trackSelector.SetValueWithoutNotify(0);

        // 추가: 전체 초기화 시점에 본 리스트 1회 갱신
        UpdateBoneList();
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
