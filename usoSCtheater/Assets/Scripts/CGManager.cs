using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;

public class CGManager : MonoBehaviour
{
    [System.Serializable]
    public class SpineEntry
    {
        public string key;                  //XML의 CG 값과 매칭되는 이름
        public GameObject spineObject;      //해당 Spine의 GameObject
    }

    [Header("Spine 오브젝트")]
    [SerializeField] private List<SpineEntry> spineEntries;

    [Header("Spine Pos 설정")]
    [SerializeField] private float screenWidth = 1920f;
    [SerializeField] private float scale3 = 1.0f;           //3분할 기본 스케일
    [SerializeField] private float scale5 = 0.75f;          //5분할 기본 스케일

    //키 > GameObj 빠른 접근용 딕셔너리
    private Dictionary<string, GameObject> spineDict = new Dictionary<string, GameObject>();
    //포지션 문자열 > X 좌표 (ScreenWidth 기준 비율)
    private Dictionary<string, float> positionDict = new Dictionary<string, float>();

    void Awake()
    {
        //Spine 오브젝트 딕셔너리 구성
        foreach (SpineEntry entry in spineEntries)
        {
            spineDict[entry.key] = entry.spineObject;
            entry.spineObject.SetActive(false);
        }

        //포지션의 x좌표 등록 (화면 중심 기준, 단위는 px)
        //3분할
        positionDict["left"] = screenWidth * -0.15f; //-480
        positionDict["center"] = screenWidth * 0f; //0
        positionDict["right"] = screenWidth * 0.15f; //480

        //5분할
        positionDict["wide_far_left"] = screenWidth * -0.40f; //-768
        positionDict["wide_left"] = screenWidth * -0.20f; //-384
        positionDict["wide_center"] = screenWidth * 0f; //0
        positionDict["wide_right"] = screenWidth * 0.20f; //384
        positionDict["wide_far_right"] = screenWidth * 0.40f; //768
    }

    public void SetCG (string cgKey, string position, string animation)
    {
        //예외 처리
        if (!spineDict.ContainsKey(cgKey))
        {
            Debug.LogWarning($"[CGManager] 등록되지 않은 CG 키: {cgKey}");
            return;
        }

        GameObject spineObj = spineDict[cgKey];
        spineObj.SetActive(true);

        //위치 적용
        ApplyPosition(spineObj, position);

        //애니메이션 재생
        if (!string.IsNullOrEmpty(animation))
            PlayAnimation(spineObj, animation);
    }

    public void HideCG(string cgKey)
    {
        if (spineDict.ContainsKey(cgKey))
            spineDict[cgKey].SetActive(false);
    }

    public void HideAll()
    {
        foreach (var obj in spineDict.Values)
            obj.SetActive(false);
    }

    private void ApplyPosition(GameObject spineObj, string position)
    {
        if (!positionDict.ContainsKey(position))
        {
            Debug.LogWarning($"[CGManager] 등록되지 않은 포지션: {position}");
            return;
        }

        //위치 적용
        Vector3 pos = spineObj.transform.localPosition;
        pos.x = positionDict[position];
        spineObj.transform.localPosition = pos;

        //스케일 적용
        float scale = position.StartsWith("wide_") ? scale5 : scale3;
        spineObj.transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void PlayAnimation(GameObject spineObj, string animName)
    {
        SkeletonAnimation skAnim = spineObj.GetComponent<SkeletonAnimation>();
        
        if (skAnim == null)
        {
            Debug.LogWarning($"[CGManager] SkeletonAnimation 컴포넌트 없음: {spineObj.name}");
            return;
        }

        skAnim.AnimationState.SetAnimation(0, animName, false);
    }
}
