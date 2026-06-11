using System.Collections;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header("리소스 경로")]
    [SerializeField] private string voicePath = "Audio/Voice";
    [SerializeField] private string bgmPath = "Audio/BGM";
    [SerializeField] private string sePath = "Audio/SE";

    private AudioSource voiceSource;
    private AudioSource bgmSource;
    private AudioSource seSource;


    void Awake()
    {
        voiceSource = gameObject.AddComponent<AudioSource>();
        bgmSource = gameObject.AddComponent<AudioSource>();
        seSource = gameObject.AddComponent<AudioSource>();

        bgmSource.loop = true;
    }

    // 보이스
    public void PlayVoice(string voiceKey)
    {
        //새로운 보이스 키 없으면 기존 재생 유지
        if (string.IsNullOrEmpty(voiceKey)) return;

        AudioClip clip = Resources.Load<AudioClip>($"{voicePath}/{voiceKey}");

        if (clip == null)
        {
            Debug.LogWarning($"[AudioManager] 보이스 파일 없음: {voiceKey}");
            return;
        }

        voiceSource.Stop();
        voiceSource.clip = clip;
        voiceSource.Play();
    }

    public void StopVoice()
    {
        voiceSource.Stop();
    }

    // BGM
    public void PlayBGM(string bgmKey, float volume = 1.0f)
    {
        //BGM 할당 없으면 무시
        if (string.IsNullOrEmpty(bgmKey)) return;
        //이미 BGM이 재생중이고, BGM의 이름이 같으면 무시
        if (bgmSource.isPlaying && bgmSource.clip != null && bgmSource.clip.name == bgmKey) return;

        AudioClip clip = Resources.Load<AudioClip>($"{bgmPath}/{bgmKey}");

        if (clip == null)
        {
            Debug.LogWarning($"[AudioManager] BGM 파일 없음: {bgmKey}");
            return;
        }

        bgmSource.volume = volume;
        bgmSource.Stop();
        bgmSource.clip = clip;
        bgmSource.loop = true;
        bgmSource.Play();
    }

    public void StopBGM()
    {
        if (bgmSource == null) return;
        
        bgmSource.Stop();
    }

    // SE
    public void PlaySE(string seKey, float volume = 1.0f, float duration = 0f)
    {
        if (string.IsNullOrEmpty(seKey)) return;

        AudioClip clip = Resources.Load<AudioClip>($"{sePath}/{seKey}");

        if (clip == null)
        {
            Debug.LogWarning($"[AudioManager] SE 파일 없음: {seKey}");
            return;
        }

        //Duration 받았으면 코루틴으로 처리, 아니면 일반 처리
        if (duration > 0f) StartCoroutine(PlaySEWithDuration(clip, volume, duration));
        else seSource.PlayOneShot(clip, volume);
    }

    private IEnumerator PlaySEWithDuration(AudioClip clip, float volume, float duration)
    {
        seSource.PlayOneShot(clip, volume);
        yield return new WaitForSeconds(duration);
        seSource.Stop();
    }
}
