using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using System.Linq;
using System.Collections;

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
    [SerializeField] private float basePosY = -100f;
    [SerializeField] private float scale3 = 60f;           //3분할 기본 스케일
    [SerializeField] private float scale5 = 40f;          //5분할 기본 스케일
    [SerializeField] private float zoomYOffsetScale = 5f;     //줌인 할 때 Y축 보정계수

    //키 > GameObj 빠른 접근용 딕셔너리
    private Dictionary<string, GameObject> spineDict = new Dictionary<string, GameObject>();
    //포지션 문자열 > X 좌표 (ScreenWidth 기준 비율)
    private Dictionary<string, float> positionDict = new Dictionary<string, float>();

    private Dictionary<string, Coroutine> zoomDict = new Dictionary<string, Coroutine>();

    //EndLoop용 Loop_Start 시간 저장
    private Dictionary<string, float> endLoopStartDict = new Dictionary<string, float>();
    //EndLoop용 애니메이션 이름 저장
    private Dictionary<string, string> endLoopAnimDict = new Dictionary<string, string>();
    //립 연동용 코루틴 관리
    private Dictionary<string, Coroutine> lipCoroutineDict = new Dictionary<string, Coroutine>();

    private static readonly string[] DEFAULT_LIP_ANIM = { "lip_wait", "lip_bitter_smile" };
    private static readonly string[] DEFAULT_LIP_S_ANIM = { "lip_wait_s", "lip_bitter_smile_s" };


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

    public void SetCG (string cgKey, string position, string animation, float voiceDuration = 0f)
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
        if (!string.IsNullOrEmpty(animation)) PlayAnimation(spineObj, animation);

        if (voiceDuration > 0f)
        {
            if (lipCoroutineDict.ContainsKey(cgKey) && lipCoroutineDict[cgKey] != null) StopCoroutine(lipCoroutineDict[cgKey]);
            lipCoroutineDict[cgKey] = StartCoroutine(LipSyncCoroutine(cgKey, animation, voiceDuration));
        }
    }

    public void HideCG(string cgKey)
    {
        if (spineDict.ContainsKey(cgKey))
            spineDict[cgKey].SetActive(false);
    }

    public void HideAll()
    {
        foreach (var obj in spineDict.Values) obj.SetActive(false);
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

        //cgKey 역조회
        string cgKey = spineDict.FirstOrDefault(kvp => kvp.Value == spineObj).Key;

        //공백과 쉼표를 구분자로 분리
        string[] animations = animName.Split(new char[] { ' ', ','}, System.StringSplitOptions.RemoveEmptyEntries);

        //모든 트랙 초기화
        skAnim.AnimationState.ClearTracks();
        skAnim.skeleton.SetToSetupPose();

        //이전 EndLoop 등록 정보 초기화
        var keysToRemove = endLoopStartDict.Keys.Where(k => k.StartsWith(cgKey + "_")).ToList();
        foreach (var key in keysToRemove)        {
            endLoopStartDict.Remove(key);
            endLoopAnimDict.Remove(key);
        }

        foreach (string anim in animations)
        {
            string trimmed = anim.Trim();
            int track = GetTrackIndex(trimmed);

            //SkeletonData에서 Loop_Start 이벤트 존재 여부 및 시간 조회
            float loopStartTime = GetLoopStartTime(skAnim, trimmed);
            bool hasEndLoop = loopStartTime >= 0f;

            //Loop_Start 이벤트가 있으면 loop=false로 1회 재생, 없다면 기존처럼 loop=true로 재생
            Spine.TrackEntry entry = skAnim.AnimationState.SetAnimation(track, trimmed, !hasEndLoop);

            if (hasEndLoop)
            {
                //Complete 이벤트 핸들러 등록
                string dictKey = cgKey + "_" + track;
                endLoopStartDict[dictKey] = loopStartTime;
                endLoopAnimDict[dictKey] = trimmed;

                float relayTime = GetRelayTime(skAnim, trimmed, out string relayAnimName);

                //Complete 이벤트 구독
                entry.Complete += (TrackEntry) =>
                {
                    OnAnimationComplete(skAnim, cgKey, track);

                    //relay가 있으면 Complete 시점에 arm 애니메이션 재생 후 마지막 프레임은 정지
                    if (!string.IsNullOrEmpty(relayAnimName)) PlayArmDown(skAnim, relayAnimName);
                };
            }
        }
    }

    private int GetTrackIndex(string animName)
    {
        string lower = animName.ToLower();

        if (lower.StartsWith("face_")) return 1;
        if (lower.StartsWith("lip_")) return 2;
        if (lower.StartsWith("arm_")) return 3;
        return 0;
    }

    public void SetZoom(string cgKey, string position, float targetScale, float duration)
    {
        if (!spineDict.ContainsKey(cgKey))
        {
            Debug.LogWarning($"[CGManager] 등록되지 않은 CG 키: {cgKey}");
            return;
        }

        GameObject spineObj = spineDict[cgKey];

        float baseScale = position.StartsWith("wide_") ? scale5 : scale3;
        float finalScale = baseScale * targetScale;

        //새로운 줌 세팅 시, 기존 줌 코루틴 실행중이면 강제 중단
        if (zoomDict.ContainsKey(cgKey) && zoomDict[cgKey] != null)
        {
            StopCoroutine(zoomDict[cgKey]);
            zoomDict[cgKey] = null;
        }

        if (duration <= 0f)
        {
            //dur 0이면 즉시 적용
            spineObj.transform.localScale = new Vector3(finalScale, finalScale, 1f);

            //Y축 보정값도 적용
            float currentScale = spineObj.transform.localScale.x;
            float targetY = basePosY -(finalScale - currentScale) * zoomYOffsetScale;
            spineObj.transform.localPosition = new Vector3(spineObj.transform.localPosition.x, targetY, 0f);
        }
        else
        {
            //아니면 코루틴
            zoomDict[cgKey] = StartCoroutine(ZoomCoroutine(spineObj, finalScale, duration));
        }
    }

    private IEnumerator ZoomCoroutine(GameObject spineObj, float finalScale, float duration)
    {
        float elapsed = 0f;
        float currentScale = spineObj.transform.localScale.x;
        float currentY = spineObj.transform.localPosition.y;
        float targetY = basePosY -(finalScale - currentScale) * zoomYOffsetScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float scale = Mathf.Lerp(currentScale, finalScale, t);
            float posY = Mathf.Lerp(currentY, targetY, t);
            
            spineObj.transform.localScale = new Vector3(scale, scale, 1f);
            spineObj.transform.localPosition = new Vector3(spineObj.transform.localPosition.x, posY, 0f);
            yield return null;
        }

        spineObj.transform.localScale = new Vector3(finalScale, finalScale, 1f);
        spineObj.transform.localPosition = new Vector3(spineObj.transform.localPosition.x, targetY, 0f);
        
    }

    public void ClearZoom(string cgKey)
    {
        if (!spineDict.ContainsKey(cgKey)) return;

        if (zoomDict.ContainsKey(cgKey) && zoomDict[cgKey] != null)
        {
            StopCoroutine(zoomDict[cgKey]);
            zoomDict[cgKey] = null;
        }

        spineDict[cgKey].transform.localScale = new Vector3(scale3, scale3, 1f);
        spineDict[cgKey].transform.localPosition = new Vector3(spineDict[cgKey].transform.localPosition.x, basePosY, 0f);
    }

    public void ClearAllZoom()
    {
        foreach (var key in spineDict.Keys) ClearZoom(key);
    }

    private float GetLoopStartTime(SkeletonAnimation skAnim, string animName)
    {
        var animData = skAnim.Skeleton.Data.FindAnimation(animName);
        if (animData == null) return -1f;
        
        foreach (var timeline in animData.Timelines)
        {
            if (timeline is Spine.EventTimeline eventTimeline)
            {
                for (int i = 0; i < eventTimeline.Events.Length; i++)
                {
                    if (eventTimeline.Events[i].Data.Name == "loop_start")
                    {
                        return eventTimeline.Frames[i];
                    }
                }
            }
        }
        return -1f;
    }

    private float GetRelayTime(SkeletonAnimation skAnim, string animName, out string relayAnimName)
    {
        relayAnimName = null;
        var animData = skAnim.Skeleton.Data.FindAnimation(animName);
        if (animData == null) return -1f;

        foreach (var timeline in animData.Timelines)
        {
            if (timeline is Spine.EventTimeline eventTimeline)
            {
                for (int i = 0; i < eventTimeline.Events.Length; i++)
                {
                    if (eventTimeline.Events[i].Data.Name == "relay")
                    {
                        relayAnimName = eventTimeline.Events[i].String;
                        return eventTimeline.Frames[i];
                    }
                }
            }
        }
        return -1f;
    }

    private void OnAnimationComplete(SkeletonAnimation skAnim, string cgKey, int track)
    {
        string dictKey = cgKey + "_" + track;
        if (!endLoopStartDict.ContainsKey(dictKey)) return;
            
        float loopStartTime = endLoopStartDict[dictKey];
        string animName = endLoopAnimDict[dictKey];

        //해당 트랙에 loopStartTime부터 loop=true로 애니메이션 재생
        Spine.TrackEntry newEntry = skAnim.AnimationState.SetAnimation(track, animName, false);
        newEntry.TrackTime = loopStartTime;

        //다음 Complete시에도 동일하게 반복
        newEntry.Complete += (TrackEntry) =>
        {
            OnAnimationComplete(skAnim, cgKey, track);
        };
    }

    private void PlayArmDown(SkeletonAnimation skAnim, string armAnimName)
    {
        var animData = skAnim.Skeleton.Data.FindAnimation(armAnimName);
        if (animData == null) 
        {
            Debug.LogWarning($"[CGManager] arm 애니메이션 없음: {armAnimName}");
            return;
        }

        Spine.TrackEntry armEntry = skAnim.AnimationState.SetAnimation(3, armAnimName, false);

        armEntry.Complete += (trackEntry) =>
        {
            // 마지막 프레임에서 timeScale = 0으로 정지
            trackEntry.TimeScale = 0f;
        };
    }

    //보이스 길이만큼 입 움직임 재생 후 정지 립으로 교체
    private IEnumerator LipSyncCoroutine(string cgKey, string animName, float voiceDuration)
    {
        if (!spineDict.ContainsKey(cgKey)) yield break;
        SkeletonAnimation skAnim = spineDict[cgKey].GetComponent<SkeletonAnimation>();
        if (skAnim == null) yield break;

        //립 애니메이션 이름 결정
        string[] animations = animName.Split(new char[] { ' ', ','}, System.StringSplitOptions.RemoveEmptyEntries);
        string explicitLip = System.Array.Find(animations, a => a.Trim().StartsWith("lip_"));

        string lipMoving;
        string lipStill;

        if (!string.IsNullOrEmpty(explicitLip))
        {
            //명시적인 Lip 애니메이션 지정이 있는 경우
            lipMoving = explicitLip.Trim();
            string candidate = lipMoving + "_s";
            lipStill = skAnim.Skeleton.Data.FindAnimation(candidate) != null ? candidate : ResolveLipAnim(skAnim, DEFAULT_LIP_S_ANIM);
        }
        else
        {
            //Lip 애니메이션 지정이 없는 경우, 0번 트랙 애니메이션에서 추출 시도
            string track0Anim = System.Array.Find(animations, a => GetTrackIndex(a.Trim()) == 0)?. Trim();
            string lipKey = ExtractLipKey(track0Anim);

            string candidate = "lip_" + lipKey;
            string candidateS = "lip_" + lipKey + "_s";

            lipMoving = skAnim.Skeleton.Data.FindAnimation(candidate) != null ? candidate : ResolveLipAnim(skAnim, DEFAULT_LIP_ANIM);
            lipStill = skAnim.Skeleton.Data.FindAnimation(candidateS) != null ? candidateS : ResolveLipAnim(skAnim, DEFAULT_LIP_S_ANIM);

        }

        //보이스 재생 중에는 움직이는 Lip 애니메이션 재생
        skAnim.AnimationState.SetAnimation(2, lipMoving, true);
        
        yield return new WaitForSeconds(voiceDuration);

        //보이스 종료 후에는 정지 Lip 애니메이션으로 교체
        if (spineDict.ContainsKey(cgKey) && spineDict[cgKey].activeSelf) skAnim.AnimationState.SetAnimation(2, lipStill, true);

        lipCoroutineDict[cgKey] = null;
    }

    //0번 트랙에서 키워드 추출하는 함수
    private string ExtractLipKey(string animName)
    {
        if (string.IsNullOrEmpty(animName)) return "wait";

        //anger1 > anger / smile3 > smile
        string trimmed = animName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return trimmed;
    }

    private string ResolveLipAnim(SkeletonAnimation skAnim, string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (skAnim.Skeleton.Data.FindAnimation(candidate) != null) return candidate;
        }

        return null;
    }
}
