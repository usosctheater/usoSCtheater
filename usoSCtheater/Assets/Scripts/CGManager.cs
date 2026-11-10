using System.Collections.Generic;
using UnityEngine;
using Spine.Unity;
using System.Linq;
using System.Collections;
using Spine;
using System.Xml;   // 추가: DefaultLipData.xml 파싱용
using UsoSCTheater.Character;   //[LD 불러오기] Spine 인덱스·로드는 CharacterDatabase 경유
using UsoSCTheater.Shared;   //[공유 라이브러리] SpineKeys · SpineKeyInfo · SpineKind

public class CGManager : MonoBehaviour
{
    //[LD 불러오기] 삭제: SpineEntry 클래스 + spineEntries(씬에 직렬화된 키 → Spine 오브젝트 목록)
    //  → 막 시작 때 PrepareAct가 Spine 인덱스(CharacterSpineData.xml) + 경로 규칙으로 필요한 Spine만 만듦

    [Header("Spine 오브젝트")]
    //[LD 불러오기] 추가: 막 시작 때 만드는 LD Spine의 부모 (Canvas > CG)
    [SerializeField] private Transform spineParent;

    //[LD 불러오기] 실행 중 생성 설정 (구 SpineRegisterTool이 씬에 만들던 오브젝트와 동일)
    private const string SPINE_SORTING_LAYER = "CG";
    private const int SPINE_SORTING_ORDER = 1;
    private const bool SPINE_PMA_VERTEX_COLORS = true;   //[E-004] 등록 도구에서 이동 — Straight Alpha 셰이더여도 항상 켬

    //[스테이지 레이아웃] 삭제: [Header("Spine Pos 설정")] screenWidth · basePosY · scale3 · scale5 · zoomYOffsetScale
    //  → 숫자 값은 Resources/Data/StageData.xml, 자리 규칙은 공유 라이브러리 StagePlanner, 줌 Y 보정은 head 기준점으로 대체
    //[스테이지 레이아웃] 추가: 무대 자리 (StageData.xml → StageTable → StagePlanner)
    private StagePlanner stage;
    private readonly List<string> stageWarnings = new List<string>();
    private const string ZOOM_PIVOT_BONE = "head";   //줌 기준점 (기본 자세 위치, 예외 오프셋은 필요할 때 StageData에)

    //키 > GameObj 빠른 접근용 딕셔너리
    //[LD 불러오기] 수정: CG 키 관련 사전은 모두 대소문자 무시 (ID 규칙·Spine 인덱스와 통일)
    //[스테이지 레이아웃] spineDict = 배치 층(부모 — 자리·크기·켜고 끄기), skeletonDict = 효과 층(Spine 자식 — 줌)
    private Dictionary<string, GameObject> spineDict = new Dictionary<string, GameObject>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SkeletonAnimation> skeletonDict = new Dictionary<string, SkeletonAnimation>(System.StringComparer.OrdinalIgnoreCase);   //[스테이지 레이아웃]
    private readonly Dictionary<string, Vector2> zoomPivotDict = new Dictionary<string, Vector2>(System.StringComparer.OrdinalIgnoreCase);   //[스테이지 레이아웃] head 위치 (자식 기준)
    //[LD 불러오기] 추가: 인덱스에 없거나 불러오기 실패한 키 (경고는 1번만)
    private readonly HashSet<string> failedKeys = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    //[스테이지 레이아웃] 삭제: positionDict (포지션 문자열 > X 좌표)

    private Dictionary<string, Coroutine> zoomDict = new Dictionary<string, Coroutine>(System.StringComparer.OrdinalIgnoreCase);

    //EndLoop용 Loop_Start 시간 저장
    private Dictionary<string, float> endLoopStartDict = new Dictionary<string, float>(System.StringComparer.OrdinalIgnoreCase);
    //EndLoop용 애니메이션 이름 저장
    private Dictionary<string, string> endLoopAnimDict = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    //립 연동용 코루틴 관리
    private Dictionary<string, Coroutine> lipCoroutineDict = new Dictionary<string, Coroutine>(System.StringComparer.OrdinalIgnoreCase);

    private static readonly string[] DEFAULT_LIP_ANIM = { "lip_wait" };
    private static readonly string[] DEFAULT_LIP_S_ANIM = { "lip_wait_s" };

    //아이돌별 디폴트 립(Moving/Still) 한 쌍
    private struct LipDefaultEntry
    {
        public string Moving;
        public string Still;
    }

    //아이돌 이름 > (카테고리 이름 > 디폴트 립 데이터)
    private Dictionary<string, Dictionary<string, LipDefaultEntry>> defaultLipDataDict
        = new Dictionary<string, Dictionary<string, LipDefaultEntry>>(System.StringComparer.OrdinalIgnoreCase);

    //트랙별 애니메이션 상태를 Inspector에서 실시간으로 보기 위한 디버그 구조체
    [System.Serializable]
    private class CGTrackDebugInfo
    {
        public string cgKey;
        public string track0_base;
        public string track1_face;
        public string track2_lip;
        public string track3_arm;
    }

    //현재 활성화된 CG들의 트랙 상태 (읽기 전용, Inspector 확인용)
    [Header("디버그 - 트랙별 애니메이션 상태 (읽기 전용)")]
    [SerializeField] private List<CGTrackDebugInfo> trackDebugInfo = new List<CGTrackDebugInfo>();

    //강제 무한 Loop용 애니메이션 목록
    private static readonly HashSet<string> FORCE_LOOP_ANIMS = new HashSet<string> {"wait", "blank"};

    void Awake()
    {
        //[LD 불러오기] 삭제: spineEntries → spineDict 구성 (이제 PrepareAct에서 필요한 Spine만 만듦)
        if (spineParent == null) Debug.LogError("[CGManager] spineParent(Canvas > CG) 미연결 — LD Spine을 만들 수 없습니다.");

        //[스테이지 레이아웃] 삭제: positionDict 3분할·5분할 등록 → StageData.xml
        LoadStageData();        //[스테이지 레이아웃] 추가
        LoadDefaultLipData();   // 추가: 아이돌별 디폴트 립 데이터 로드
    }

    //[스테이지 레이아웃] 추가: StageData.xml 읽기 (문제는 경고만 — 표가 비면 캐릭터가 나오지 않음)
    private void LoadStageData()
    {
        TextAsset xml = Resources.Load<TextAsset>(UsoSCTheater.ResourcePaths.Data + "/" + StageTable.DataName);
        var warnings = new List<string>();
        stage = new StagePlanner(StageTable.Parse(xml != null ? xml.text : null, warnings));
        foreach (string w in warnings) Debug.LogWarning($"[CGManager] {w}");
    }

    //Play 모드에서 Inspector로 트랙별 애니메이션 상태를 실시간 확인하기 위한 갱신 루프
#if UNITY_EDITOR
    void Update()
    {
        RefreshTrackDebugInfo();
    }

    private void RefreshTrackDebugInfo()
    {
        trackDebugInfo.Clear();

        foreach (var pair in spineDict)
        {
            if (!pair.Value.activeSelf) continue;

            //[스테이지 레이아웃] 수정: GetComponent → skeletonDict (SkeletonAnimation은 자식)
            if (!skeletonDict.TryGetValue(pair.Key, out SkeletonAnimation skAnim)) continue;

            trackDebugInfo.Add(new CGTrackDebugInfo
            {
                cgKey = pair.Key,
                track0_base = skAnim.AnimationState.GetCurrent(0)?.Animation?.Name ?? "-",
                track1_face = skAnim.AnimationState.GetCurrent(1)?.Animation?.Name ?? "-",
                track2_lip = skAnim.AnimationState.GetCurrent(2)?.Animation?.Name ?? "-",
                track3_arm = skAnim.AnimationState.GetCurrent(3)?.Animation?.Name ?? "-"
            });
        }
    }
#endif

    //[LD 불러오기] 막에서 쓰는 LD Spine을 미리 만듦. 이미 만든 키는 건너뜀 — 씬이 끝나면 씬과 함께 해제
    public void PrepareAct(IEnumerable<string> cgKeys)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int created = 0;
        foreach (string key in cgKeys)
            if (!spineDict.ContainsKey(key) && TryCreateSpine(key, out _)) created++;
        sw.Stop();
        Debug.Log($"[CGManager] 막 Spine 준비: 새로 {created}개 / 보유 {spineDict.Count}개 — {sw.ElapsedMilliseconds}ms");   //막 시작 멈춤 관찰용
    }

    //[LD 불러오기] 키 → 오브젝트. 미리 못 만든 키면 그 자리에서 만듦 (경고)
    private bool TryGetSpine(string cgKey, out GameObject obj)
    {
        obj = null;
        if (string.IsNullOrEmpty(cgKey) || failedKeys.Contains(cgKey)) return false;
        if (spineDict.TryGetValue(cgKey, out obj)) return true;

        Debug.LogWarning($"[CGManager] 막 시작 때 준비되지 않은 CG 키 → 지금 불러옴: {cgKey}");
        return TryCreateSpine(cgKey, out obj);
    }

    //[LD 불러오기] 인덱스 확인 → SkeletonData 로드 → CG/{캐릭터}/{키} 오브젝트 생성 (비활성)
    private bool TryCreateSpine(string cgKey, out GameObject obj)
    {
        obj = null;
        if (failedKeys.Contains(cgKey)) return false;
        if (!CharacterDatabase.TryGetSpine(cgKey, SpineKind.LD, out SpineKeyInfo info))
        {
            failedKeys.Add(cgKey);
            Debug.LogWarning($"[CGManager] 등록되지 않은 CG 키 (인덱스에 LD로 없음 → Tools > Spine > Register Spine): {cgKey}");
            return false;
        }
        if (spineDict.TryGetValue(info.Key, out obj)) return true;   //대소문자만 다른 키

        SkeletonDataAsset data = CharacterDatabase.LoadSkeletonData(info.Key);   //실패 경고는 DB에서
        if (data == null || spineParent == null)
        {
            failedKeys.Add(cgKey);
            return false;
        }

        //[스테이지 레이아웃] 변경: {키} = 배치 층(빈 오브젝트), 그 아래 Spine = 효과 층(SkeletonAnimation)
        obj = new GameObject(info.Key);
        obj.SetActive(false);   //AddComponent 직후 Awake(Initialize)가 데이터 없이 돌지 않도록 먼저 끔 (자식도 부모가 꺼져 있어 Awake가 돌지 않음)
        obj.transform.SetParent(GetCharacterFolder(info.CharacterId), false);

        GameObject spine = new GameObject("Spine");   //[스테이지 레이아웃] 추가: 효과 층
        spine.transform.SetParent(obj.transform, false);

        SkeletonAnimation skAnim = spine.AddComponent<SkeletonAnimation>();   //MeshRenderer·MeshFilter는 자동 추가   //[스테이지 레이아웃] obj → spine
        skAnim.skeletonDataAsset = data;
        skAnim.pmaVertexColors = SPINE_PMA_VERTEX_COLORS;
        skAnim.Initialize(false);

        MeshRenderer meshRenderer = spine.GetComponent<MeshRenderer>();   //[스테이지 레이아웃] obj → spine
        if (meshRenderer != null)
        {
            meshRenderer.sortingLayerName = SPINE_SORTING_LAYER;
            meshRenderer.sortingOrder = SPINE_SORTING_ORDER;
        }

        spineDict[info.Key] = obj;
        skeletonDict[info.Key] = skAnim;                  //[스테이지 레이아웃]
        zoomPivotDict[info.Key] = GetZoomPivot(skAnim);   //[스테이지 레이아웃]
        return true;
    }

    //[스테이지 레이아웃] 추가: 줌 기준점 = head 본의 기본 자세 위치 (없으면 원점 + 경고)
    private Vector2 GetZoomPivot(SkeletonAnimation skAnim)
    {
        skAnim.Skeleton.SetToSetupPose();
        skAnim.Skeleton.UpdateWorldTransform();
        Bone head = skAnim.Skeleton.FindBone(ZOOM_PIVOT_BONE);
        if (head != null) return new Vector2(head.WorldX, head.WorldY);
        Debug.LogWarning($"[CGManager] {skAnim.skeletonDataAsset.name}: '{ZOOM_PIVOT_BONE}' 본 없음 → 줌 기준점 = 원점");
        return Vector2.zero;
    }

    //[LD 불러오기] CG 아래 캐릭터 폴더 (없으면 생성)
    private Transform GetCharacterFolder(string characterId)
    {
        foreach (Transform child in spineParent)
            if (string.Equals(child.name, characterId, System.StringComparison.OrdinalIgnoreCase)) return child;

        Transform folder = new GameObject(characterId).transform;
        folder.SetParent(spineParent, false);
        return folder;
    }

    //[스테이지 레이아웃] 추가: TEXT·SETCG 1줄마다 SetCG 전에 1번 (앞뒤 순서 — CGGROUP 멤버는 같은 값)
    public void BeginLine() => stage.StartLine();

    public void SetCG (string cgKey, string position, string animation, float voiceDuration = 0f)
    {
        //[LD 불러오기] 수정: 미등록 경고 + spineDict[cgKey] → TryGetSpine (경고는 그 안에서)
        if (!TryGetSpine(cgKey, out GameObject spineObj)) return;

        //[스테이지 레이아웃] 수정: ApplyPosition → 자리 규칙. Pos 오류·분할 불일치는 경고 후 이 줄에서 건너뜀 (애니메이션·립도)
        stageWarnings.Clear();
        bool shown = stage.Show(cgKey, position, stageWarnings);
        foreach (string w in stageWarnings) Debug.LogWarning($"[CGManager] {w}");
        if (!shown) return;

        spineObj.SetActive(true);
        ApplyStage();   //[스테이지 레이아웃] 등장과 함께 무대 전원 다시 배치 (자동 배치 이동 + 앞뒤 정렬)

        //애니메이션 재생
        if (!string.IsNullOrEmpty(animation)) PlayAnimation(spineObj.name, animation);   //[스테이지 레이아웃] spineObj → 키 (오브젝트 이름 = 등록 키, 이전 역조회 결과와 같음)

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
        stage.Remove(cgKey);   //[스테이지 레이아웃] 남은 캐릭터는 옮기지 않음
    }

    public void HideAll()
    {
        foreach (var obj in spineDict.Values) obj.SetActive(false);
        stage.Clear();   //[스테이지 레이아웃]
    }

    public void ClearAllCGState()
    {
        //립 코루틴 모두 중단
        foreach (var coroutine in lipCoroutineDict.Values)
        {
            if (coroutine != null) StopCoroutine(coroutine);
        }
        lipCoroutineDict.Clear();

        //EndLoop 정보 초기화
        endLoopStartDict.Clear();
        endLoopAnimDict.Clear();

        //CG 비활성화 및 줌 초기화
        HideAll();
        ClearAllZoom();
    }

    //[스테이지 레이아웃] 삭제: ApplyPosition (자리 이름 → X·Y·크기, 줌 코루틴 중지 + 크기 되돌림)
    //  → ApplyStage (부모만 — 줌은 자식이라 다음 CG 줄에서 풀리지 않음)
    //[스테이지 레이아웃] 추가: 무대 전원의 배치 층 위치·크기 + 앞뒤 정렬 (뒤 → 앞으로 정렬 순서 1, 2, 3…)
    private void ApplyStage()
    {
        int order = SPINE_SORTING_ORDER;
        foreach (StagePlacement p in stage.Placements())
        {
            if (!spineDict.TryGetValue(p.Key, out GameObject obj)) continue;
            obj.transform.localPosition = new Vector3((float)p.X, (float)p.BaseY, 0f);
            obj.transform.localScale = new Vector3((float)p.Scale, (float)p.Scale, 1f);
            if (skeletonDict.TryGetValue(p.Key, out SkeletonAnimation sk)) sk.GetComponent<MeshRenderer>().sortingOrder = order++;
        }
    }

    //[스테이지 레이아웃] 수정: (GameObject spineObj, …) → (string cgKey, …) — 역조회(FirstOrDefault) 삭제
    private void PlayAnimation(string cgKey, string animName)
    {
        if (!skeletonDict.TryGetValue(cgKey, out SkeletonAnimation skAnim))
        {
            Debug.LogWarning($"[CGManager] SkeletonAnimation 없음: {cgKey}");
            return;
        }

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

            bool shouldLoop;
            bool shouldHold;

            if (hasEndLoop)
            {
                //LoopStart 이벤트가 있으면 1회 재생 후 EndLoop
                shouldLoop = false;
                shouldHold = false;
            }
            else if (FORCE_LOOP_ANIMS.Contains(trimmed))
            {
                //강제 무한 Loop 애니메이션이라면 무한 루프
                shouldLoop = true;
                shouldHold = false;
            }
            else
            {
                //둘 다 아닐 경우 1회 재생 후 마지막 동작 유지
                shouldLoop = false;
                shouldHold = true;
            }

            //Loop_Start 이벤트가 있으면 loop=false로 1회 재생, 없다면 기존처럼 loop=true로 재생
            Spine.TrackEntry entry = skAnim.AnimationState.SetAnimation(track, trimmed, shouldLoop);

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

                    //ArmDown 기능 일시 비활성화
                    //relay가 있으면 Complete 시점에 arm 애니메이션 재생 후 마지막 프레임은 정지
                    // if (!string.IsNullOrEmpty(relayAnimName)) PlayArmDown(skAnim, relayAnimName);
                };
            }
            else if (shouldHold)
            {
                entry.Complete += (TrackEntry) => TrackEntry.TimeScale = 0f;
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

    //[스테이지 레이아웃] 수정: 줌 = 효과 층(자식)만 — 배치와 따로라 다음 CG 줄에서 풀리지 않음, 끝낼 때는 Value = 1 줌
    //  기준점(head) 고정: 자식 위치 = 기준점 × (1 − 배율). 매번 배율로 다시 계산 → Y 누적·5분할 크기 되돌림·시간 0 보정 문제 없음
    //  삭제: position 인자 (크기는 부모가 가짐), basePosY·zoomYOffsetScale 보정
    public void SetZoom(string cgKey, float zoom, float duration)
    {
        //[LD 불러오기] 수정: ContainsKey + 경고 → TryGetSpine
        if (!TryGetSpine(cgKey, out _)) return;
        StopZoom(cgKey);

        if (duration <= 0f) ApplyZoom(cgKey, zoom);
        else zoomDict[cgKey] = StartCoroutine(ZoomCoroutine(cgKey, zoom, duration));
    }

    private IEnumerator ZoomCoroutine(string cgKey, float target, float duration)
    {
        float from = skeletonDict[cgKey].transform.localScale.x;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            ApplyZoom(cgKey, Mathf.Lerp(from, target, t / duration));
            yield return null;
        }
        ApplyZoom(cgKey, target);
        zoomDict[cgKey] = null;
    }

    //[스테이지 레이아웃] 추가
    private void ApplyZoom(string cgKey, float zoom)
    {
        if (!skeletonDict.TryGetValue(cgKey, out SkeletonAnimation sk)) return;
        Vector2 pivot = zoomPivotDict[cgKey];
        sk.transform.localScale = new Vector3(zoom, zoom, 1f);
        sk.transform.localPosition = new Vector3((float)StageLayout.ZoomOffset(pivot.x, zoom), (float)StageLayout.ZoomOffset(pivot.y, zoom), 0f);
    }

    //[스테이지 레이아웃] 추가
    private void StopZoom(string cgKey)
    {
        if (zoomDict.TryGetValue(cgKey, out Coroutine zoom) && zoom != null) StopCoroutine(zoom);
        zoomDict[cgKey] = null;
    }

    public void ClearZoom(string cgKey)
    {
        if (!skeletonDict.ContainsKey(cgKey)) return;   //[스테이지 레이아웃] spineDict → skeletonDict
        StopZoom(cgKey);
        ApplyZoom(cgKey, 1f);   //[스테이지 레이아웃] scale3·basePosY 대신 배율 1 (크기는 부모)
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
        //[스테이지 레이아웃] 수정: spineDict[cgKey].GetComponent → skeletonDict (SkeletonAnimation은 자식)
        if (!skeletonDict.TryGetValue(cgKey, out SkeletonAnimation skAnim)) yield break;

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

            if (skAnim.Skeleton.Data.FindAnimation(candidate) != null)
            {
                lipStill = candidate;
            }
            else
            {
                // 변경: "lip_smile1" -> "smile1" -> 끝자리 숫자 제거 -> "smile" (카테고리 키워드), 아이돌별 디폴트 립 조회
                string keyword = ExtractLipKey(lipMoving.Substring(4));
                lipStill = GetDefaultLipEntry(ExtractIdolKey(cgKey), keyword, skAnim).Still;
            }
        }
        else
        {
            //Lip 애니메이션 지정이 없는 경우, 0번 트랙 애니메이션에서 추출 시도
            string track0Anim = System.Array.Find(animations, a => GetTrackIndex(a.Trim()) == 0)?. Trim();
            string lipKey = ExtractLipKey(track0Anim);

            string candidate = "lip_" + lipKey;
            string candidateS = "lip_" + lipKey + "_s";

            // 변경: 하드코딩된 디폴트 대신, 아이돌별 디폴트 립 데이터로 폴백
            bool needDefault = skAnim.Skeleton.Data.FindAnimation(candidate) == null
                             || skAnim.Skeleton.Data.FindAnimation(candidateS) == null;

            LipDefaultEntry defaultEntry = needDefault ? GetDefaultLipEntry(ExtractIdolKey(cgKey), lipKey, skAnim) : default;

            lipMoving = skAnim.Skeleton.Data.FindAnimation(candidate) != null ? candidate : defaultEntry.Moving;
            lipStill = skAnim.Skeleton.Data.FindAnimation(candidateS) != null ? candidateS : defaultEntry.Still;
        }

        //보이스 재생 중에는 움직이는 Lip 애니메이션 재생
        skAnim.AnimationState.SetAnimation(2, lipMoving, true);
        
        yield return new WaitForSeconds(voiceDuration);

        //보이스 종료 후에는 정지 Lip 애니메이션으로 교체
        if (spineDict.ContainsKey(cgKey) && spineDict[cgKey].activeSelf)
        {
            if (lipStill != null)
            {
                skAnim.AnimationState.SetAnimation(2, lipStill, true);
            }
            else
            {
                //Lip 애니메이션을 최종적으로 못찾았을 경우, 그냥 애니메이션을 정지시킴
            Spine.TrackEntry track2 = skAnim.AnimationState.GetCurrent(2);
            if (track2 != null) track2.TimeScale = 0f;
            }
        }
        
        lipCoroutineDict[cgKey] = null;
    }

    private string ResolveLipAnim(SkeletonAnimation skAnim, string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (skAnim.Skeleton.Data.FindAnimation(candidate) != null) return candidate;
        }

        return null;
    }

    //DefaultLipData.xml을 읽어 아이돌별/카테고리별 디폴트 립 데이터를 구성
    private void LoadDefaultLipData()
    {
        TextAsset xmlAsset = Resources.Load<TextAsset>(UsoSCTheater.ResourcePaths.Data + "/DefaultLipData");   //[리소스 정리] 폴더는 ResourcePaths
        if (xmlAsset == null)
        {
            Debug.LogWarning("[CGManager] DefaultLipData.xml을 찾을 수 없습니다. 하드코딩된 안전망만 사용됩니다.");
            return;
        }

        XmlDocument doc = new XmlDocument();
        doc.LoadXml(xmlAsset.text);

        foreach (XmlNode idolNode in doc.DocumentElement.ChildNodes)
        {
            if (idolNode.NodeType != XmlNodeType.Element) continue;

            Dictionary<string, LipDefaultEntry> categoryDict
                = new Dictionary<string, LipDefaultEntry>(System.StringComparer.OrdinalIgnoreCase);

            foreach (XmlNode categoryNode in idolNode.ChildNodes)
            {
                if (categoryNode.NodeType != XmlNodeType.Element) continue;

                categoryDict[categoryNode.Name] = new LipDefaultEntry
                {
                    Moving = categoryNode.Attributes["Moving"]?.Value,
                    Still = categoryNode.Attributes["Still"]?.Value
                };
            }

            defaultLipDataDict[idolNode.Name] = categoryDict;
        }
    }

    //아이돌(cgKey) + 키워드 기준으로 디폴트 립(Moving/Still) 조회
    private LipDefaultEntry GetDefaultLipEntry(string idolKey, string keyword, SkeletonAnimation skAnim)
    {
        string categoryKey = string.IsNullOrEmpty(keyword) ? "Default" : keyword;

        if (defaultLipDataDict.TryGetValue(idolKey, out var categoryDict))
        {
            if (categoryDict.TryGetValue(categoryKey, out var entry)) return entry;
            if (categoryDict.TryGetValue("Default", out var defaultEntry)) return defaultEntry;
        }

        //데이터에 아이돌 자체가 없거나 Default 카테고리도 없는 극단적 예외 상황 - 하드코딩된 안전망 사용
        return new LipDefaultEntry
        {
            Moving = ResolveLipAnim(skAnim, DEFAULT_LIP_ANIM),
            Still = ResolveLipAnim(skAnim, DEFAULT_LIP_S_ANIM)
        };
    }

    public void UpdateLipOnly(string cgKey, string animation, float voiceDuration)
    {
        if (!spineDict.ContainsKey(cgKey)) return;
        if (voiceDuration <= 0f) return;

        //기존 코루틴 중단 후, 새로운 립 코루틴 실행
        if (lipCoroutineDict.ContainsKey(cgKey) && lipCoroutineDict[cgKey] != null) StopCoroutine(lipCoroutineDict[cgKey]);

        lipCoroutineDict[cgKey] = StartCoroutine(LipSyncCoroutine(cgKey, animation, voiceDuration));
    }

    public void RestartLipSync(string cgKey, string animation, float voiceDuration)
    {
        if (!spineDict.ContainsKey(cgKey)) return;
        if (voiceDuration <= 0f) return;

        //기존 코루틴 중단 후, 새로운 립 코루틴 실행
        if (lipCoroutineDict.ContainsKey(cgKey) && lipCoroutineDict[cgKey] != null) StopCoroutine(lipCoroutineDict[cgKey]);

        lipCoroutineDict[cgKey] = StartCoroutine(LipSyncCoroutine(cgKey, animation, voiceDuration));
    }

    //0번 트랙에서 키워드 추출하는 함수
    private string ExtractLipKey(string animName)
    {
        if (string.IsNullOrEmpty(animName)) return "wait";

        //anger1 > anger / smile3 > smile
        string trimmed = animName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return trimmed;
    }

    //cgKey에서 아이돌 명 추출하는 함수
    private string ExtractIdolKey(string cgKey)
    {
        if (string.IsNullOrEmpty(cgKey)) return cgKey;

        int underscoreIndex = cgKey.IndexOf('_');
        return underscoreIndex > 0 ? cgKey.Substring(0, underscoreIndex) : cgKey;
    }

}
