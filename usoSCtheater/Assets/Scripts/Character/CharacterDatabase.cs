using System;
using System.Collections.Generic;
using System.Xml;
using Spine.Unity;   //[Spine 인덱스] SkeletonDataAsset
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
        public string GetName(string language, bool full = false) => LocalizedNames.Get(names, Id, language, full);   //[유닛] 이름 대체 규칙을 유닛과 공유
    }

    //[유닛] 추가: 유닛 1개 (UnitData.xml의 Unit 한 줄). Members = 캐릭터 ID (소문자)
    public class UnitEntry
    {
        public string Id { get; }
        public IReadOnlyList<string> Members { get; }
        private readonly Dictionary<string, string> names;

        internal UnitEntry(string id, List<string> members, Dictionary<string, string> names)
        {
            Id = id;
            Members = members;
            this.names = names;
        }

        public string GetName(string language, bool full = false) => LocalizedNames.Get(names, Id, language, full);

        //멤버 전원이 목록에 들어 있음 (게스트가 더 있어도 true, 대소문자 무시)
        public bool IsFullyIn(IEnumerable<string> characterIds)
        {
            if (Members.Count == 0 || characterIds == null) return false;
            var set = new HashSet<string>(characterIds, StringComparer.OrdinalIgnoreCase);
            foreach (string m in Members) if (!set.Contains(m)) return false;
            return true;
        }
    }

    //[유닛] 언어별 이름 대체 규칙 (캐릭터·유닛 공용): 요청 형식 → 같은 언어 다른 형식 → 기본 언어(Ko) → Id
    internal static class LocalizedNames
    {
        public static string Get(Dictionary<string, string> names, string id, string language, bool full)
        {
            if (string.IsNullOrEmpty(language)) language = CharacterDatabase.DefaultLanguage;

            if (TryGet(names, language, full, out string name)) return name;
            if (TryGet(names, language, !full, out name)) return name;
            if (TryGet(names, CharacterDatabase.DefaultLanguage, full, out name)) return name;
            if (TryGet(names, CharacterDatabase.DefaultLanguage, !full, out name)) return name;
            return id;
        }

        private static bool TryGet(Dictionary<string, string> names, string language, bool full, out string name)
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
        private static Dictionary<string, List<SpineKeyInfo>> spinesById;   //[Spine 인덱스] 캐릭터 Id → Spine 목록 (대소문자 무시)

        public const string UnitDataPath = "Data/UnitData";   //[유닛] Resources.Load 경로
        private static List<UnitEntry> units;                  //[유닛] 파일 순서 유지
        private static Dictionary<string, UnitEntry> unitsById;   //[유닛] 대소문자 무시

        //Domain Reload 비활성 → Play 시작마다 static 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Reload();

        //[Spine 인덱스] 캐시 비우기 (다음 조회 때 XML 다시 읽음). 등록 도구가 인덱스·CharacterData를 고친 뒤 호출
        public static void Reload()
        {
            byId = null;
            iconCache = null;
            spinesById = null;
            units = null;       //[유닛]
            unitsById = null;   //[유닛]
        }

        //[유닛] 전체 유닛 (UnitData.xml 순서)
        public static IReadOnlyList<UnitEntry> GetUnits()
        {
            EnsureUnitsLoaded();
            return units;
        }

        //[유닛] Id로 조회 (대소문자 무시), 없으면 null
        public static UnitEntry FindUnit(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId)) return null;
            EnsureUnitsLoaded();
            return unitsById.TryGetValue(unitId.Trim(), out var unit) ? unit : null;
        }

        private static void EnsureUnitsLoaded()
        {
            if (units != null) return;
            units = new List<UnitEntry>();
            unitsById = new Dictionary<string, UnitEntry>(StringComparer.OrdinalIgnoreCase);

            TextAsset xml = Resources.Load<TextAsset>(UnitDataPath);
            if (xml == null)
            {
                Debug.LogWarning($"[CharacterDatabase] Resources/{UnitDataPath}.xml 없음 → 유닛 없음으로 처리");
                return;
            }

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml.text);
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element || node.Name != "Unit") continue;

                string id = node.Attributes["Id"]?.Value?.Trim();
                if (string.IsNullOrEmpty(id) || unitsById.ContainsKey(id))
                {
                    Debug.LogWarning($"[CharacterDatabase] UnitData.xml: Id 없음 또는 중복('{id}') → 건너뜀");
                    continue;
                }

                var members = new List<string>();
                var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (attr.Name == "Id") continue;
                    if (attr.Name == "Member")
                    {
                        foreach (string m in attr.Value.Split(','))
                        {
                            string memberId = m.Trim().ToLowerInvariant();
                            if (memberId.Length == 0) continue;
                            if (Find(memberId) == null) Debug.LogWarning($"[CharacterDatabase] UnitData.xml '{id}': 멤버 '{memberId}'가 CharacterData.xml에 없음");
                            members.Add(memberId);
                        }
                        continue;
                    }
                    names[attr.Name] = attr.Value.Trim();   //Id·Member 외 속성 = 언어별 표기
                }

                var unit = new UnitEntry(id, members, names);
                units.Add(unit);
                unitsById[id] = unit;
            }
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

        //[Spine 인덱스] 캐릭터의 Spine 키 목록 (인덱스만 읽음, Spine 파일은 불러오지 않음). 없으면 빈 목록
        //includeCostume = false면 _costume 타입 제외
        public static List<string> GetSpineKeys(string id, SpineKind kind, bool includeCostume = true)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(id)) return result;
            EnsureSpineIndexLoaded();

            if (!spinesById.TryGetValue(id.Trim(), out var list)) return result;
            foreach (SpineKeyInfo info in list)
            {
                if (info.Kind != kind) continue;
                if (!includeCostume && info.IsCostume) continue;
                result.Add(info.Key);
            }
            return result;
        }

        //[LD 불러오기] 인덱스에 등록된 키인지 확인 + 인덱스의 정식 키 정보 반환 (대소문자 무시). 분류가 다르면 false
        public static bool TryGetSpine(string key, SpineKind kind, out SpineKeyInfo info)
        {
            info = null;
            if (!SpineKeys.TryParse(key, out SpineKeyInfo parsed)) return false;
            EnsureSpineIndexLoaded();
            if (!spinesById.TryGetValue(parsed.CharacterId, out var list)) return false;

            SpineKeyInfo found = list.Find(e => string.Equals(e.Key, parsed.Key, StringComparison.OrdinalIgnoreCase));
            if (found == null || found.Kind != kind) return false;
            info = found;
            return true;
        }

        //[Spine 인덱스] 키 → SkeletonDataAsset (Resources/Spine/{Id}/{Key}_SkeletonData). 이때만 Spine이 메모리에 올라옴. 없으면 null + 경고
        public static SkeletonDataAsset LoadSkeletonData(string key)
        {
            if (!SpineKeys.TryParse(key, out SpineKeyInfo info))
            {
                Debug.LogWarning($"[CharacterDatabase] Spine 키 형식이 맞지 않음: '{key}'");
                return null;
            }
            SkeletonDataAsset data = Resources.Load<SkeletonDataAsset>(info.ResourcePath);
            if (data == null) Debug.LogWarning($"[CharacterDatabase] SkeletonData 없음: Resources/{info.ResourcePath} (인덱스와 파일이 어긋남 → 등록 도구에서 [인덱스 다시 만들기])");
            return data;
        }

        private static void EnsureSpineIndexLoaded()
        {
            if (spinesById != null) return;
            spinesById = new Dictionary<string, List<SpineKeyInfo>>(StringComparer.OrdinalIgnoreCase);

            TextAsset xml = Resources.Load<TextAsset>(CharacterSpineData.DataPath);
            if (xml == null)
            {
                Debug.LogWarning($"[CharacterDatabase] Resources/{CharacterSpineData.DataPath}.xml 없음 → Tools > Spine > Register Spine에서 [인덱스 다시 만들기]");
                return;   //빈 사전 유지 → 경고는 1번만
            }

            foreach (SpineKeyInfo info in CharacterSpineData.Parse(xml.text))
            {
                if (!spinesById.TryGetValue(info.CharacterId, out var list))
                    spinesById[info.CharacterId] = list = new List<SpineKeyInfo>();
                list.Add(info);
            }
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
