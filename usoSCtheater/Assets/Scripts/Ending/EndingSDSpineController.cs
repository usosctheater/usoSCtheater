using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;
using UsoSCTheater.Character;   //[엔딩 SD] CharacterDatabase (SD 키 조회·로드)
using UsoSCTheater.Scenario;    //[엔딩 SD] 재생한 시나리오의 등장인물

namespace UsoSCTheater.Ending
{
    /// <summary>
    /// 인스펙터에서 직접 연결된 SkeletonAnimation 오브젝트에
    /// 랜덤 애니메이션을 무한 반복 재생한다.
    /// [엔딩 SD] 시작 시 재생한 시나리오의 등장인물 중 SD가 있는 1명을 랜덤으로 골라 SkeletonData를 교체한다.
    /// 고를 수 없으면(시나리오 정보 없음 / SD 있는 등장인물 없음) 인스펙터에 연결된 기본 SD를 그대로 쓴다.
    /// </summary>
    public class EndingSDSpineController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SkeletonAnimation skeletonAnimation; // 인스펙터에서 직접 연결 (연결된 SkeletonData = 기본 SD)

        [Header("Animation Settings")]
        [SerializeField] private float crossFadeDuration = 0.2f;

        //[엔딩 SD] 추가
        [Header("SD 선택")]
        [SerializeField] private bool includeCostumeSD = true;   // 코스튬 SD(cb_costume 등)도 랜덤 후보에 포함

        private SkeletonAnimation _currentSkeletonAnimation;
        private List<string> _availableAnimationNames = new List<string>();
        private string _currentAnimationName;

        public void Setup()
        {
            if (skeletonAnimation == null)
            {
                Debug.LogError("[EndingSDSpineController] SkeletonAnimation이 연결되지 않았습니다.");
                return;
            }

            TryApplyRandomSD();   //[엔딩 SD] 추가: 등장인물 SD로 교체 (실패 시 기본 SD 유지)

            _currentSkeletonAnimation = skeletonAnimation;

            _availableAnimationNames.Clear();
            foreach (var animation in _currentSkeletonAnimation.Skeleton.Data.Animations)
            {
                _availableAnimationNames.Add(animation.Name);
            }

            if (_availableAnimationNames.Count == 0)
            {
                Debug.LogWarning("[EndingSDSpineController] SD Spine에 애니메이션이 없습니다.");
                return;
            }

            PlayRandomAnimation();
        }

        private void Update()
        {
            if (_currentSkeletonAnimation == null) return;

            TrackEntry track = _currentSkeletonAnimation.AnimationState.GetCurrent(0);
            if (track == null) return;

            // 애니메이션 종료 직전(0.1초 전)에 다음 애니메이션으로 전환
            if (track.AnimationTime >= track.AnimationEnd - 0.1f)
            {
                PlayRandomAnimation();
            }
        }

        private void PlayRandomAnimation()
        {
            if (_currentSkeletonAnimation == null || _availableAnimationNames.Count == 0) return;

            // 애니메이션이 1개 이상일 경우 동일 애니메이션 연속 재생 방지
            string nextAnimation;
            do
            {
                nextAnimation = _availableAnimationNames[Random.Range(0, _availableAnimationNames.Count)];
            } while (nextAnimation == _currentAnimationName && _availableAnimationNames.Count > 1);

            _currentAnimationName = nextAnimation;
            _currentSkeletonAnimation.AnimationState.SetAnimation(0, nextAnimation, false);
        }

        //[엔딩 SD] 추가: 등장인물 중 SD가 있는 인물을 먼저 랜덤 1명 → 그 인물의 SD 중 랜덤 1개
        //(SD가 많은 인물이 더 자주 나오지 않도록 인물 단위로 먼저 고름)
        private void TryApplyRandomSD()
        {
            if (!ScenarioSelection.HasSelection)
            {
                Debug.Log("[EndingSDSpineController] 선택된 시나리오 없음 (CommunicationScene 직접 Play 등) → 기본 SD");
                return;
            }

            ScenarioCatalog catalog = ScenarioCatalog.Load();
            ScenarioEntry entry = catalog != null ? catalog.Find(ScenarioSelection.ScenarioFolder) : null;
            if (entry == null || entry.characters == null || entry.characters.Count == 0)
            {
                Debug.Log($"[EndingSDSpineController] {ScenarioSelection.ScenarioFolder}: 등장인물 정보 없음 → 기본 SD");
                return;
            }

            var candidates = new List<List<string>>();   // 인물별 SD 키 목록 (SD가 있는 인물만)
            foreach (string id in entry.characters)
            {
                List<string> keys = CharacterDatabase.GetSpineKeys(id, SpineKind.SD, includeCostumeSD);
                if (keys.Count > 0) candidates.Add(keys);
            }
            if (candidates.Count == 0)
            {
                Debug.Log($"[EndingSDSpineController] {entry.folderName}: SD가 있는 등장인물 없음 ({string.Join(", ", entry.characters)}) → 기본 SD");
                return;
            }

            List<string> picked = candidates[Random.Range(0, candidates.Count)];
            string key = picked[Random.Range(0, picked.Count)];

            SkeletonDataAsset data = CharacterDatabase.LoadSkeletonData(key);
            if (data == null) return;   //경고는 LoadSkeletonData에서, 기본 SD 유지

            skeletonAnimation.skeletonDataAsset = data;
            skeletonAnimation.Initialize(true);
            Debug.Log($"[EndingSDSpineController] SD 선택: {key} (후보 {candidates.Count}명)");
        }

        public void StopAndClear()
        {
            if (_currentSkeletonAnimation != null)
            {
                _currentSkeletonAnimation.AnimationState.ClearTracks();
                _currentSkeletonAnimation = null;
            }
        }
    }
}
