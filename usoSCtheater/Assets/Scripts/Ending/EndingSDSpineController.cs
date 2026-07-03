using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace UsoSCTheater.Ending
{
    /// <summary>
    /// 등장한 캐릭터 중 랜덤으로 하나를 골라 SD Spine을 로드하고,
    /// 랜덤 애니메이션을 크로스페이드로 무한 반복 재생한다.
    /// SD Spine 경로 규칙: Resources/Spine/SD/{idolName}/
    /// </summary>
    public class EndingSDSpineController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform spineSpawnRoot; // SD Spine 오브젝트가 생성될 위치

        [Header("Animation Settings")]
        [SerializeField] private float crossFadeDuration = 0.2f;

        private SkeletonAnimation _currentSkeletonAnimation;
        private List<string> _availableAnimationNames = new List<string>();

        /// <summary>
        /// 등장 캐릭터 이름 리스트(예: "Mei", "Asahi") 중 랜덤으로 하나 선택해 SD Spine을 생성한다.
        /// </summary>
        public void SetupRandomCharacter(List<string> appearedIdolNames)
        {
            if (appearedIdolNames == null || appearedIdolNames.Count == 0)
            {
                Debug.LogWarning("[EndingSDSpineController] 등장 캐릭터 목록이 비어 있습니다.");
                return;
            }

            string selectedIdol = appearedIdolNames[Random.Range(0, appearedIdolNames.Count)];
            string skeletonDataPath = $"Spine/SD/{selectedIdol}/{selectedIdol}_SkeletonData";

            SkeletonDataAsset skeletonDataAsset = Resources.Load<SkeletonDataAsset>(skeletonDataPath);
            if (skeletonDataAsset == null)
            {
                Debug.LogError($"[EndingSDSpineController] SkeletonDataAsset을 찾을 수 없습니다: {skeletonDataPath}");
                return;
            }

            // 기존에 생성된 SD Spine 오브젝트가 있으면 제거
            if (_currentSkeletonAnimation != null)
            {
                Destroy(_currentSkeletonAnimation.gameObject);
            }

            GameObject spineObj = new GameObject(selectedIdol + "_SD_Spine");
            spineObj.transform.SetParent(spineSpawnRoot, false);
            spineObj.transform.localPosition = Vector3.zero;

            _currentSkeletonAnimation = spineObj.AddComponent<SkeletonAnimation>();
            _currentSkeletonAnimation.skeletonDataAsset = skeletonDataAsset;
            _currentSkeletonAnimation.Initialize(true);

            _availableAnimationNames.Clear();
            foreach (var animation in _currentSkeletonAnimation.Skeleton.Data.Animations)
            {
                _availableAnimationNames.Add(animation.Name);
            }

            if (_availableAnimationNames.Count == 0)
            {
                Debug.LogWarning($"[EndingSDSpineController] '{selectedIdol}' SD Spine에 애니메이션이 없습니다.");
                return;
            }

            PlayRandomAnimation();

            // 애니메이션이 끝날 때마다 다음 랜덤 애니메이션으로 전환
            _currentSkeletonAnimation.AnimationState.Complete += OnAnimationComplete;
        }

        private void OnAnimationComplete(TrackEntry trackEntry)
        {
            PlayRandomAnimation();
        }

        private void PlayRandomAnimation()
        {
            if (_currentSkeletonAnimation == null || _availableAnimationNames.Count == 0)
            {
                return;
            }

            string nextAnimation = _availableAnimationNames[Random.Range(0, _availableAnimationNames.Count)];

            // loop: false로 재생 후 Complete 콜백에서 다음 애니메이션으로 다시 크로스페이드
            _currentSkeletonAnimation.AnimationState.SetAnimation(0, nextAnimation, false);
            // SetAnimation 직후 MixDuration을 직접 주고 싶다면 SetAnimation 대신 아래 방식 사용 가능:
            // _currentSkeletonAnimation.AnimationState.SetAnimation(0, nextAnimation, false).MixDuration = crossFadeDuration;
        }

        public void StopAndClear()
        {
            if (_currentSkeletonAnimation != null)
            {
                _currentSkeletonAnimation.AnimationState.Complete -= OnAnimationComplete;
                Destroy(_currentSkeletonAnimation.gameObject);
                _currentSkeletonAnimation = null;
            }
        }
    }
}
