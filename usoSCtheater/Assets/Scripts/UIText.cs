using System;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;

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

        private static Dictionary<string, Dictionary<string, string>> byId;   // Id(대소문자 무시) → 속성(Text, Text_Ja …)
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
            if (Localization.TryGet(attrs, TextAttr, Localization.Current, out string text)) return text;
            return Localization.TryGet(attrs, TextAttr, Localization.Default, out text) ? text : "";
        }

        private static void EnsureLoaded()
        {
            if (byId != null) return;
            byId = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            TextAsset xml = Resources.Load<TextAsset>(DataPath);
            if (xml == null)
            {
                Debug.LogError($"[UIText] Resources/{DataPath}.xml 없음");
                return;
            }
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml.text);
                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.NodeType != XmlNodeType.Element || node.Name != "Text") continue;
                    string id = node.Attributes["Id"]?.Value?.Trim();
                    if (string.IsNullOrEmpty(id) || byId.ContainsKey(id))
                    {
                        Debug.LogWarning($"[UIText] UITextData.xml: Id 없음 또는 중복('{id}') → 건너뜀");
                        continue;
                    }
                    var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (XmlAttribute a in node.Attributes)
                        if (a.Name != "Id") attrs[a.Name] = a.Value;
                    byId[id] = attrs;
                }
            }
            catch (XmlException e)
            {
                Debug.LogError($"[UIText] UITextData.xml 파싱 실패 — {e.Message}");
            }
        }
    }
}
