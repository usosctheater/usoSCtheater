using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    [Header("리소스 경로")]
    [SerializeField] private string voicePath = "Audio/Voice";
    [SerializeField] private string bgmPath = "Audio/BGM";
    [SerializeField] private string sePath = "Audio/SE";

    //Voice 여러 개 저장 가능하도록 List로 변경
    private List<AudioSource> voiceSources = new List<AudioSource>();
    private AudioSource bgmSource;
    private AudioSource seSource;

    private Dictionary<string, AudioSource> audioSlots = new Dictionary<string, AudioSource>();

    //[시나리오] 현재 시나리오의 보이스 하위 폴더명 (예: "IL"). 비어 있으면 voicePath 루트만 검색
    private string scenarioVoiceFolder = "";


    void Awake()
    {
        string[] slotKeys = {"bgm", "1", "2", "3"};
        foreach (string key in slotKeys)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.loop = (key == "bgm");
            audioSlots[key] = source;
        }

        voiceSources.Add(gameObject.AddComponent<AudioSource>());
        seSource = gameObject.AddComponent<AudioSource>();
    }

    // 보이스
    public void PlayVoice(string voiceKey)
    {
        //새로운 보이스 키 없으면 기존 재생 유지
        if (string.IsNullOrEmpty(voiceKey)) return;

        string[] keys = voiceKey.Split(new char[] { ' ', ','}, System.StringSplitOptions.RemoveEmptyEntries);

        //List 부족 시 추가 생성
        while (voiceSources.Count < keys.Length) voiceSources.Add(gameObject.AddComponent<AudioSource>());

        for (int i = 0; i < keys.Length; i++)
        {
            string trimmed = keys[i].Trim();
            AudioClip clip = LoadVoiceClip(trimmed);   //[시나리오] 시나리오 폴더 우선 → 루트 폴백

            if (clip == null)
            {
                UnityEngine.Debug.LogWarning($"[AudioManager] 보이스 파일 없음: {trimmed} (시나리오 폴더: '{scenarioVoiceFolder}', 루트 폴백 포함)");
                continue;
            }

            voiceSources[i].Stop();
            voiceSources[i].clip = clip;
            voiceSources[i].Play();
        }
    }

    //[시나리오] SceneManager가 시나리오 로드 시 호출 (시나리오 폴더명 = 보이스 폴더명)
    public void SetScenarioVoiceFolder(string folderName)
    {
        scenarioVoiceFolder = string.IsNullOrEmpty(folderName) ? "" : folderName.Trim().Trim('/');
    }

    //[시나리오] 보이스 검색: Voice/{시나리오}/{키} 우선 → Voice/{키} 폴백
    //폴더 간 파일명 중복 허용. 루트 폴백으로 Voice="OT/OT001" 같은 다른 폴더 참조도 동작
    private AudioClip LoadVoiceClip(string key)
    {
        if (!string.IsNullOrEmpty(scenarioVoiceFolder))
        {
            AudioClip clip = Resources.Load<AudioClip>($"{voicePath}/{scenarioVoiceFolder}/{key}");
            if (clip != null) return clip;
        }
        return Resources.Load<AudioClip>($"{voicePath}/{key}");
    }

    //보이스 길이 조회 (PlayVoice와 동일한 경로 규칙 사용). 다중 키는 가장 긴 길이 반환
    public float GetVoiceDuration(string voiceKey)
    {
        if (string.IsNullOrEmpty(voiceKey)) return 0f;

        float maxDuration = 0f;
        foreach (string key in voiceKey.Split(new char[] { ' ', ','}, System.StringSplitOptions.RemoveEmptyEntries))
        {
            AudioClip clip = LoadVoiceClip(key.Trim());
            if (clip != null) maxDuration = Mathf.Max(maxDuration, clip.length);
        }
        return maxDuration;
    }

    public void StopVoice()
    {
        foreach (var voice in voiceSources) voice.Stop();
    }

    // Audio
    public void PlayAudio(string slot, string track, float volume = 1.0f, bool loop = false)
    {
        if (!audioSlots.ContainsKey(slot))
        {
            UnityEngine.Debug.LogWarning($"[AudioManager] 존재하지 않는 슬롯: {slot}");
            return;
        }

        if (string.IsNullOrEmpty(track)) return;

        AudioSource source = audioSlots[slot];
        string path = slot == "bgm" ? bgmPath : sePath;
        AudioClip clip = Resources.Load<AudioClip>($"{path}/{track}");

        if (clip == null)
        {
            UnityEngine.Debug.LogWarning($"[AudioManager] 오디오 파일 없음: {track}");
            return;
        }

        if (source.isPlaying) UnityEngine.Debug.LogWarning($"[AudioManager] 슬롯 {slot} 강제 덮어쓰기: {source.clip?.name} → {track}");

        source.loop = (slot == "bgm") ? true : loop;
        source.volume = volume;
        source.Stop();
        source.clip = clip;
        source.Play();
    }

    public void StopAudio(string slot)
    {
        if (!audioSlots.ContainsKey(slot))
        {
            UnityEngine.Debug.LogWarning($"[AudioManager] 존재하지 않는 슬롯: {slot}");
            return;
        }
        
        audioSlots[slot].Stop();
    }

    public void StopAllAudio()
    {
        foreach(var source in audioSlots.Values) source.Stop();
    }

    // SE
    public void PlaySE(string seKey, float volume = 1.0f, float duration = 0f)
    {
        if (string.IsNullOrEmpty(seKey)) return;

        AudioClip clip = Resources.Load<AudioClip>($"{sePath}/{seKey}");

        if (clip == null)
        {
            UnityEngine.Debug.LogWarning($"[AudioManager] SE 파일 없음: {seKey}");
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
