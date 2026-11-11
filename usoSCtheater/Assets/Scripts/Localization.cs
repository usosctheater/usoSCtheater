using System;
using System.Collections.Generic;
using UnityEngine;   //[공유 코드 통합] 삭제(3회차): using System.Xml
using UsoSCTheater.Shared;   //[공유 코드 통합] 추가: LanguageInfo · LanguageRules

namespace UsoSCTheater
{
    //[공유 코드 통합] 삭제: LanguageInfo 클래스 → 공유 LanguageInfo (IsDefault는 해석 때 정해짐, 이전: ReferenceEquals(this, Default))

    /// <summary>
    /// [다국어] 현재 언어 + 언어별 문자열 읽기 (static). 코드는 언어별로 분기하지 않고 여기 함수만 씀
    /// 데이터 규칙: {속성}_{언어 접미어} (예: Text_Ja), 기본 언어는 접미어 없는 속성. 속성 이름은 대소문자 구분 (막 XML 읽기는 공유 ActAttrs.Localized)
    /// 읽는 순서: 현재 언어 속성 → 기본 속성 (빈 값은 없는 것으로 봄)
    /// 언어 목록 Resources/Data/LanguageData.xml, 현재 언어는 PlayerPrefs에 저장
    /// 언어 변경은 목록 씬에서만 (파싱 때 현재 언어 값만 남기는 구조라 재생 중 변경은 반영되지 않음)
    /// [공유 코드 통합] 언어 목록 해석·속성 이름 규칙·이름 잇기는 공유 LanguageRules·CharIds
    /// </summary>
    public static class Localization
    {
        public const string DataPath = ResourcePaths.Data + "/" + LanguageRules.DataName;   //[공유 코드 통합] 수정: 파일 이름은 공유 상수
        //[공유 코드 통합] 삭제: Separator (→ LanguageRules.Separator)
        private const string PrefsKey = "Language";
        private static readonly LanguageInfo[] Fallback = { new LanguageInfo("ko", "한국어", true) };   //[공유 코드 통합] 추가: 파일이 없거나 깨졌을 때 (게임은 기본 언어 하나)

        /// <summary>언어가 바뀐 뒤 호출 (목록 다시 그리기 등)</summary>
        public static event Action Changed;

        private static List<LanguageInfo> languages;
        private static LanguageInfo defaultLanguage;
        private static LanguageInfo current;

        //Domain Reload 비활성 → Play 시작마다 static 초기화 (이전 Play의 구독자도 비움)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            languages = null;
            defaultLanguage = null;
            current = null;
            Changed = null;
        }

        public static IReadOnlyList<LanguageInfo> Languages { get { EnsureLoaded(); return languages; } }
        public static LanguageInfo Default { get { EnsureLoaded(); return defaultLanguage; } }
        public static LanguageInfo Current { get { EnsureCurrent(); return current; } }

        //코드로 찾기 (대소문자 무시), 없으면 null
        public static LanguageInfo Find(string code)
        {
            EnsureLoaded();
            return LanguageRules.Find(languages, code);   //[공유 코드 통합] 수정
        }

        //언어 변경 + 저장 + Changed 알림 (같은 언어면 아무것도 안 함)
        public static void SetLanguage(string code)
        {
            LanguageInfo next = Find(code);
            if (next == null)
            {
                Debug.LogWarning($"[Localization] 지원하지 않는 언어 코드 '{code}' → 변경 안 함");
                return;
            }
            if (ReferenceEquals(next, Current)) return;

            current = next;
            PlayerPrefs.SetString(PrefsKey, current.Code);
            PlayerPrefs.Save();
            Debug.Log($"[Localization] 언어 변경: {current.Code} ({current.NativeName})");
            Changed?.Invoke();
        }

        //다음 언어로 (LanguageData 순서, 끝이면 처음) — 디버그 키용
        public static void CycleLanguage()
        {
            EnsureLoaded();
            int index = languages.IndexOf(Current);
            SetLanguage(languages[(index + 1) % languages.Count].Code);
        }

        //[공유 코드 통합] 삭제: AttrName → LanguageRules.AttrName
        //[공유 코드 통합] 삭제(3회차): Get(XmlNode…)·TryGetAttr → 공유 ActAttrs.Localized (이름 대소문자 구분)

        //[공유 코드 통합] 삭제: TryGet → LanguageRules.TryGet
        //[공유 코드 통합] 삭제: HasInvalidLanguageSuffix → LanguageRules.HasInvalidSuffix(이름, Localization.Languages)
        //[공유 코드 통합] 삭제: Join → CharIds.Join(이름들, Localization.Current)

        private static void EnsureLoaded()
        {
            if (languages != null) return;
            //[공유 코드 통합] 수정: 해석(Code 정리·중복·Default·기본 목록 대체)은 공유 — 이전 XmlDocument 순회·try/catch 삭제
            TextAsset xml = Resources.Load<TextAsset>(DataPath);
            var warnings = new List<string>();
            languages = new List<LanguageInfo>(LanguageRules.Parse(xml != null ? xml.text : null, Fallback, warnings));
            foreach (string w in warnings) Debug.LogWarning($"[Localization] {w}");
            defaultLanguage = languages.Find(l => l.IsDefault);
        }

        private static void EnsureCurrent()
        {
            if (current != null) return;
            EnsureLoaded();
            string saved = PlayerPrefs.GetString(PrefsKey, "");
            current = Find(saved) ?? defaultLanguage;
            if (saved.Length > 0 && !string.Equals(saved, current.Code, StringComparison.OrdinalIgnoreCase))
                Debug.LogWarning($"[Localization] 저장된 언어 '{saved}'가 LanguageData에 없음 → 기본 언어 '{current.Code}'");
        }
    }
}
