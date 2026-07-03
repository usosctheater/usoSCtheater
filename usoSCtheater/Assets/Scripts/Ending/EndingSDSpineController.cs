using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace UsoSCTheater.Ending
{
    /// <summary>
    /// 인스펙터에서 직접 연결된 SkeletonAnimation 오브젝트에
    /// 랜덤 애니메이션을 무한 반복 재생한다.
    /// </summary>
    public class EndingSDSpineController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SkeletonAnimation skeletonAnimation; // 인스펙터에서 직접 연결

        [Header("Animation Settings")]
        [SerializeField] private float crossFadeDuration = 0.2f;

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
