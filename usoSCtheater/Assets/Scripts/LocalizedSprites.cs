using System;
using System.Collections.Generic;
using UnityEngine;

namespace UsoSCTheater
{
    /// <summary>
    /// [다국어] 키로 불러오는 Sprite: {폴더}/{키}_{언어} → {폴더}/{키} (기본 언어는 접미어 없는 파일만, 파일 접미어는 소문자 _ja)
    /// 고른 경로(없음 포함)를 언어별로 캐시 → 일본어에서 _ja가 없는 키도 실패 조회는 1번뿐. Sprite는 붙잡지 않음(메모리 해제 방해 안 함)
    /// </summary>
    public static class LocalizedSprites
    {
        private static readonly Dictionary<string, string> resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // "{폴더}/{키}" → 실제 경로 (null = 파일 없음)
        private static string resolvedLanguage;

        //Domain Reload 비활성 → Play 시작마다 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            resolved.Clear();
            resolvedLanguage = null;
        }

        public static Sprite Load(string folder, string key)
        {
            LanguageInfo language = Localization.Current;
            string path = $"{folder}/{key}";
            if (language.IsDefault) return Resources.Load<Sprite>(path);

            if (resolvedLanguage != language.Code)
            {
                resolved.Clear();
                resolvedLanguage = language.Code;
            }
            if (resolved.TryGetValue(path, out string found)) return found == null ? null : Resources.Load<Sprite>(found);

            string localizedPath = $"{path}_{language.Code}";
            Sprite sprite = Resources.Load<Sprite>(localizedPath);
            if (sprite != null) { resolved[path] = localizedPath; return sprite; }

            sprite = Resources.Load<Sprite>(path);
            resolved[path] = sprite != null ? path : null;
            return sprite;
        }
    }
}
