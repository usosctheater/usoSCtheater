using System.Collections.Generic;
using Spine.Unity;
using TMPro;
using UnityEditor.EditorTools;
using UnityEditor.Search;
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
    private string pendingAnimName = "";                                  //적용 대기 중인 애니메이션 이름

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
        trackSelector.AddOptions(new List<string> { "트랙 0", "트랙 1", "트랙 2", "트랙 3" });

        //재생 및 일시정지 버튼
        playPauseButton.onClick.AddListener(OnPlayPauseClicked);

        //슬라이더
        timeSlider.onValueChanged.AddListener(OnSliderChanged);

        // 첫번째 Spine 선택
        if (spineObjects.Count > 0) OnSpineSelected(0);
    }

    void Update()
    {
        
    }

    private void OnSpineSelected(int index)
    {
        
    }

    private void OnPlayPauseClicked()
    {
        
    }

    private void OnSliderChanged(float value)
    {
        
    }
}
