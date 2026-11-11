using System;
using System.Collections.Generic;
using UnityEngine;
using UsoSCTheater.Shared;   //[공유 코드 통합] 추가: IdTable · LanguageRules (삭제: using System.Xml)

namespace UsoSCTheater
{
    /// <summary>
    /// [다국어] UI 고정 문구 (static). 데이터: Resources/Data/UITextData.xml — &lt;Text Id="…" Text="한국어" Text_Ja="…"/&gt;
    /// Get(id) = 현재 언어 → 기본 언어. 없는 Id는 Id 그대로 (경고는 Id당 1번)
    /// 처음 한 번 읽어 사전에 보관 (이후 조회만 — 다국어 원칙: 성능 우선)
    /// </summary>
    public static class UIText
    {
        public const string DataPath = ResourcePaths.Data + "/UITextData";   // Resources.Load 경로
        public const string TextAttr = "Text";

        private static Dictionary<string, IReadOnlyDictionary<string, string>> byId;   // Id(대소문자 무시) → 속성(Text, Text_Ja …)   //[공유 코드 통합] 값 형식: 공유 IdEntry.Attrs
        private static HashSet<string> warned;

        //Domain Reload 비활성 → Play 시작마다 static 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Reload();

        //[검사] 캐시 비우기 (에디터에서 UITextData.xml을 고친 뒤 검사할 때)
        public static void Reload()
        {
            byId = null;
            warned = null;
        }

        //[검사] 모든 Id (Check UI Texts용)
        public static IReadOnlyCollection<string> Ids { get { EnsureLoaded(); return byId.Keys; } }

        public static string Get(string id)
        {
            EnsureLoaded();
            string key = id?.Trim() ?? "";
            if (!byId.TryGetValue(key, out var attrs))
            {
                warned ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (warned.Add(key)) Debug.LogWarning($"[UIText] UITextData.xml에 없는 Id '{key}' → Id 그대로 표시");
                return key;
            }
            if (LanguageRules.TryGet(attrs, TextAttr, Localization.Current, out string text)) return text;   //[공유 코드 통합] 수정: Localization.TryGet → LanguageRules.TryGet
            return LanguageRules.TryGet(attrs, TextAttr, Localization.Default, out text) ? text : "";
        }

        private static void EnsureLoaded()
        {
            if (byId != null) return;
            byId = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            //[공유 코드 통합] 수정: 해석(Id·중복·값 공백)은 공유 IdTable — 이전 XmlDocument 순회·try/catch 삭제
            TextAsset xml = Resources.Load<TextAsset>(DataPath);
            var warnings = new List<string>();
            foreach (IdEntry e in IdTable.Parse(xml != null ? xml.text : null, "Text", warnings)) byId[e.Id] = e.Attrs;
            foreach (string w in warnings) Debug.LogWarning($"[UIText] UITextData.xml: {w}");
        }
    }
}
