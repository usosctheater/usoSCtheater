using System;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;

namespace UsoSCTheater.Character
{
    /// <summary>캐릭터 1명 (CharacterData.xml의 Character 한 줄). Id = TE nickName (Spine 폴더·CG 키 앞부분과 같음)</summary>
    public class CharacterEntry
    {
        public string Id { get; }
        private readonly Dictionary<string, string> names;   // 속성 이름(Ko, KoFull, Ja, JaFull ...) → 표기

        internal CharacterEntry(string id, Dictionary<string, string> names)
        {
            Id = id;
            this.names = names;
        }

        /// <summary>
        /// 언어별 이름. full = true면 성까지 포함한 이름({언어}Full).
        /// 없으면 같은 언어의 다른 형식 → 기본 언어(Ko) → Id 순으로 대체
        /// </summary>
        public string GetName(string language, bool full = false)
        {
            if (string.IsNullOrEmpty(language)) language = CharacterDatabase.DefaultLanguage;

            if (TryGet(language, full, out string name)) return name;
            if (TryGet(language, !full, out name)) return name;
            if (TryGet(CharacterDatabase.DefaultLanguage, full, out name)) return name;
            if (TryGet(CharacterDatabase.DefaultLanguage, !full, out name)) return name;
            return Id;
        }

        private bool TryGet(string language, bool full, out string name)
        {
            return names.TryGetValue(full ? language + CharacterDatabase.FullSuffix : language, out name)
                   && !string.IsNullOrEmpty(name);
        }
    }

    /// <summary>
    /// 캐릭터 통합 데이터베이스 (static). 모든 씬은 캐릭터 정보를 여기서만 가져온다.
    /// 데이터: Resources/Data/CharacterData.xml (사람이 편집)
    /// 리소스는 직접 참조하지 않고 경로 규칙으로 필요할 때 불러옴 (DB를 읽어도 다른 캐릭터 리소스가 메모리에 올라오지 않게)
    /// </summary>
    public static class CharacterDatabase
    {
        public const string DataPath = "Data/CharacterData";   // Resources.Load 경로
        public const string IconRoot = "CharacterIcon";        // 목록 아이콘: Resources/CharacterIcon/{Id}
        public const string DefaultLanguage = "Ko";
        public const string FullSuffix = "Full";               // 성까지 포함한 이름 속성 접미어 (KoFull, JaFull ...)

        private static Dictionary<string, CharacterEntry> byId;   // 대소문자 무시
        private static Dictionary<string, Sprite> iconCache;      // 없는 아이콘(null)도 저장 → 경고·로드 반복 방지

        //Domain Reload 비활성 → Play 시작마다 static 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            byId = null;
            iconCache = null;
        }

        //Id로 조회 (대소문자 무시), 없으면 null
        public static CharacterEntry Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            EnsureLoaded();
            return byId.TryGetValue(id.Trim(), out var entry) ? entry : null;
        }

        //표시 이름 (DB에 없으면 입력값 그대로)
        public static string GetName(string id, string language = DefaultLanguage, bool full = false)
            => Find(id)?.GetName(language, full) ?? id;

        //목록 아이콘: Resources/CharacterIcon/{Id}. 없으면 null (아이콘당 경고 1번)
        public static Sprite LoadIcon(string id)
        {
            CharacterEntry entry = Find(id);
            if (entry == null) return null;

            iconCache ??= new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            if (iconCache.TryGetValue(entry.Id, out var cached)) return cached;

            Sprite sprite = Resources.Load<Sprite>($"{IconRoot}/{entry.Id}");
            if (sprite == null) Debug.LogWarning($"[CharacterDatabase] 아이콘 없음: Resources/{IconRoot}/{entry.Id}");
            iconCache[entry.Id] = sprite;
            return sprite;
        }

        private static void EnsureLoaded()
        {
            if (byId != null) return;
            byId = new Dictionary<string, CharacterEntry>(StringComparer.OrdinalIgnoreCase);

            TextAsset xml = Resources.Load<TextAsset>(DataPath);
            if (xml == null)
            {
                Debug.LogError($"[CharacterDatabase] Resources/{DataPath}.xml 없음");
                return;   //빈 사전 유지 → 에러는 1번만
            }

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml.text);

            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element || node.Name != "Character") continue;

                string id = node.Attributes["Id"]?.Value?.Trim();
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning("[CharacterDatabase] Id 없는 Character 줄 → 건너뜀");
                    continue;
                }
                if (byId.ContainsKey(id))
                {
                    Debug.LogWarning($"[CharacterDatabase] Id '{id}' 중복 → 첫 항목만 사용");
                    continue;
                }

                //Id 외 속성 = 언어별 표기 (언어 추가 시 코드 수정 불필요)
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (attr.Name == "Id") continue;
                    names[attr.Name] = attr.Value.Trim();
                }

                byId[id] = new CharacterEntry(id, names);
            }
        }
    }
}
