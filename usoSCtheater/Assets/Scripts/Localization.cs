using System;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;

namespace UsoSCTheater
{
    /// <summary>[다국어] 지원 언어 1개 (LanguageData.xml의 Language 한 줄)</summary>
    public sealed class LanguageInfo
    {
        public string Code { get; }            // 언어 코드 (소문자, 예: ko, ja)
        public string NativeName { get; }      // 그 언어로 쓴 언어 이름 (로그·나중의 설정 UI용)
        public string ListSeparator { get; }   // 여러 이름을 이어 붙일 때 구분자
        public bool IsDefault => ReferenceEquals(this, Localization.Default);

        internal LanguageInfo(string code, string nativeName, string listSeparator)
        {
            Code = code;
            NativeName = nativeName;
            ListSeparator = listSeparator;
        }
    }

    /// <summary>
    /// [다국어] 현재 언어 + 언어별 문자열 읽기 (static). 코드는 언어별로 분기하지 않고 여기 함수만 씀
    /// 데이터 규칙: {속성}_{언어 코드} (예: Text_Ja), 기본 언어는 접미어 없는 속성. 속성 이름은 대소문자 무시
    /// 읽는 순서: 현재 언어 속성 → 기본 속성 (빈 값은 없는 것으로 봄)
    /// 언어 목록 Resources/Data/LanguageData.xml, 현재 언어는 PlayerPrefs에 저장
    /// 언어 변경은 목록 씬에서만 (파싱 때 현재 언어 값만 남기는 구조라 재생 중 변경은 반영되지 않음)
    /// </summary>
    public static class Localization
    {
        public const string DataPath = ResourcePaths.Data + "/LanguageData";   // Resources.Load 경로
        public const string Separator = "_";                                   // 속성 접미어 구분자 (Text_Ja)
        private const string PrefsKey = "Language";

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
            if (string.IsNullOrWhiteSpace(code)) return null;
            EnsureLoaded();
            return languages.Find(l => string.Equals(l.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
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

        //언어별 속성 이름: 기본 언어 → attr, 그 외 → attr_{code}
        public static string AttrName(string attr, LanguageInfo language) =>
            language == null || language.IsDefault ? attr : attr + Separator + language.Code;

        //XML 속성 읽기: 현재 언어 → 기본. 둘 다 없으면 ""
        public static string Get(XmlNode node, string attr) => Get(node, attr, Current);

        public static string Get(XmlNode node, string attr, LanguageInfo language)
        {
            if (language != null && !language.IsDefault && TryGetAttr(node, AttrName(attr, language), out string value)) return value;
            return TryGetAttr(node, attr, out value) ? value : "";
        }

        //속성 사전(이름 → 값, 대소문자 무시 사전)에서 한 언어 값만 (대체 없음). 대체 순서가 다른 곳(캐릭터 이름 등)에서 사용
        public static bool TryGet(IReadOnlyDictionary<string, string> attrs, string attr, LanguageInfo language, out string value)
        {
            value = null;
            return attrs != null && attrs.TryGetValue(AttrName(attr, language), out value) && !string.IsNullOrEmpty(value);
        }

        //여러 이름을 현재 언어 구분자로 이어 붙임
        public static string Join(IEnumerable<string> parts) => string.Join(Current.ListSeparator, parts);

        //속성 찾기 (이름 대소문자 무시, 빈 값은 없는 것으로 봄)
        private static bool TryGetAttr(XmlNode node, string name, out string value)
        {
            value = null;
            if (node?.Attributes == null) return false;
            foreach (XmlAttribute attr in node.Attributes)
            {
                if (!string.Equals(attr.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(attr.Value)) return false;
                value = attr.Value;
                return true;
            }
            return false;
        }

        private static void EnsureLoaded()
        {
            if (languages != null) return;
            languages = new List<LanguageInfo>();
            LanguageInfo marked = null;

            TextAsset xml = Resources.Load<TextAsset>(DataPath);
            if (xml == null) Debug.LogError($"[Localization] Resources/{DataPath}.xml 없음 → 기본 언어(ko)만 사용");
            else
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.LoadXml(xml.text);
                    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element || node.Name != "Language") continue;
                        string code = node.Attributes["Code"]?.Value?.Trim().ToLowerInvariant();
                        if (string.IsNullOrEmpty(code) || languages.Exists(l => l.Code == code))
                        {
                            Debug.LogWarning($"[Localization] LanguageData.xml: Code 없음 또는 중복('{code}') → 건너뜀");
                            continue;
                        }
                        var info = new LanguageInfo(code, node.Attributes["NativeName"]?.Value ?? code, node.Attributes["ListSeparator"]?.Value ?? ", ");
                        languages.Add(info);
                        if (string.Equals(node.Attributes["Default"]?.Value, "true", StringComparison.OrdinalIgnoreCase))
                        {
                            if (marked == null) marked = info;
                            else Debug.LogWarning($"[Localization] LanguageData.xml: Default가 여러 개 → 첫 항목 '{marked.Code}' 사용");
                        }
                    }
                }
                catch (XmlException e)
                {
                    Debug.LogError($"[Localization] LanguageData.xml 파싱 실패 — {e.Message}");
                }
            }

            //데이터가 없거나 깨져도 클라이언트가 멈추지 않게 기본 언어 1개는 보장
            if (languages.Count == 0) languages.Add(new LanguageInfo("ko", "한국어", ", "));
            defaultLanguage = marked ?? languages[0];
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
