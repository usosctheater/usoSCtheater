using UnityEngine;

namespace UsoSCTheater.Ending
{
    /// <summary>
    /// 엔딩 BGM 재생/정지를 담당한다. 항상 루프로 재생하며,
    /// 페이드인/아웃 없이 즉시 재생 및 정지한다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EndingBGMPlayer : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip bgmClip;

        public void Play()
        {
            if (audioSource == null || bgmClip == null)
            {
                Debug.LogWarning("[EndingBGMPlayer] AudioSource 또는 BGM 클립이 설정되지 않았습니다.");
                return;
            }

            audioSource.clip = bgmClip;
            audioSource.loop = true;
            audioSource.Play();
        }

        public void Stop()
        {
            if (audioSource != null)
            {
                audioSource.Stop();
            }
        }
    }
}
