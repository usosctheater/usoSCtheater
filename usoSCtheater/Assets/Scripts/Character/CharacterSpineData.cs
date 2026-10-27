using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEngine;

namespace UsoSCTheater.Character
{
    /// <summary>Spine 분류. LD = 커뮤니케이션 씬 Spine, SD = 엔딩 씬 Spine (2026-10-26 명칭 확정)</summary>
    public enum SpineKind { LD, SD, Unknown }

    /// <summary>
    /// Spine 키 1개의 해석 결과. 키 형식 = TE 내보내기 이름 {캐릭터 ID}_{타입}_{enzaId} (예: fuyuko_cb_1050180010)
    /// 타입에는 _가 들어갈 수 있음 (stand_costume, support_stand, awake_cb_costume ...)
    /// </summary>
    public sealed class SpineKeyInfo
    {
        public string Key { get; }
        public string CharacterId { get; }
        public string Type { get; }        // awake_ 접두어 포함 원본 타입
        public string EnzaId { get; }
        public SpineKind Kind { get; }
        public bool IsCostume { get; }     // 타입이 _costume으로 끝남 (엔딩 랜덤 후보에서 끌 수 있음)

        internal SpineKeyInfo(string key, string characterId, string type, string enzaId)
        {
            Key = key;
            CharacterId = characterId;
            Type = type;
            EnzaId = enzaId;
            Kind = SpineKeys.GetKind(type);
            IsCostume = type.EndsWith("_costume", StringComparison.OrdinalIgnoreCase);
        }

        //Resources.Load 경로: Spine/{Id}/{Key}_SkeletonData
        public string ResourcePath => $"{SpineKeys.SpineRoot}/{CharacterId}/{Key}{SpineKeys.SkeletonDataSuffix}";
    }

    /// <summary>Spine 키 규칙 (게임·등록 도구 공용 — 분류 기준은 여기 한 곳에만 둠)</summary>
    public static class SpineKeys
    {
        public const string SpineRoot = "Spine";                     // Resources/Spine
        public const string SkeletonDataSuffix = "_SkeletonData";    // spine-unity가 만드는 에셋 이름 접미어
        public const string AwakePrefix = "awake_";                  // TE: awake_idols 그룹 접두어

        private static readonly HashSet<string> LdTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "stand", "stand_costume", "support_stand" };
        private static readonly HashSet<string> SdTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "cb", "cb_costume" };

        //형식이 맞지 않으면 false (Id = 첫 _ 앞, EnzaId = 마지막 _ 뒤 숫자, Type = 그 사이)
        public static bool TryParse(string key, out SpineKeyInfo info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(key)) return false;
            key = key.Trim();

            int first = key.IndexOf('_');
            int last = key.LastIndexOf('_');
            if (first <= 0 || last <= first + 1 || last >= key.Length - 1) return false;

            string enzaId = key.Substring(last + 1);
            if (!enzaId.All(char.IsDigit)) return false;

            info = new SpineKeyInfo(key, key.Substring(0, first), key.Substring(first + 1, last - first - 1), enzaId);
            return true;
        }

        //awake_ 접두어는 떼고 판단. 목록에 없는 타입은 Unknown (예외는 발견 시 목록에 추가)
        public static SpineKind GetKind(string type)
        {
            if (string.IsNullOrEmpty(type)) return SpineKind.Unknown;
            string baseType = type.StartsWith(AwakePrefix, StringComparison.OrdinalIgnoreCase) ? type.Substring(AwakePrefix.Length) : type;
            if (LdTypes.Contains(baseType)) return SpineKind.LD;
            if (SdTypes.Contains(baseType)) return SpineKind.SD;
            return SpineKind.Unknown;
        }
    }

    /// <summary>
    /// Spine 인덱스 Resources/Data/CharacterSpineData.xml — 등록 도구(Tools > Spine > Register Spine)가 자동 생성, 직접 고치지 않음.
    /// 빌드 게임은 Resources 폴더 목록을 볼 수 없으므로 "캐릭터별 Spine 목록"은 이 파일로만 안다.
    /// Character·Type·Kind 속성은 사람이 읽기 위한 것이고, 게임은 Key를 해석해 사용한다.
    /// </summary>
    public static class CharacterSpineData
    {
        public const string DataPath = "Data/CharacterSpineData";                     // Resources.Load 경로
        public const string AssetPath = "Assets/Resources/Data/CharacterSpineData.xml"; // 등록 도구 쓰기 경로

        public static List<SpineKeyInfo> Parse(string xmlText)
        {
            var result = new List<SpineKeyInfo>();
            if (string.IsNullOrWhiteSpace(xmlText)) return result;

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xmlText);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element || node.Name != "Spine") continue;

                string key = node.Attributes["Key"]?.Value;
                if (!SpineKeys.TryParse(key, out SpineKeyInfo info))
                {
                    Debug.LogWarning($"[CharacterSpineData] 키 형식이 맞지 않음 → 건너뜀: '{key}'");
                    continue;
                }
                if (!seen.Add(info.Key))
                {
                    Debug.LogWarning($"[CharacterSpineData] 키 중복 → 첫 항목만 사용: {info.Key}");
                    continue;
                }
                result.Add(info);
            }
            return result;
        }

        //키 순서(Ordinal) 정렬 → 커밋 diff에 바뀐 줄만 보이게
        public static string Serialize(IEnumerable<SpineKeyInfo> entries)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<!-- 자동 생성: Tools > Spine > Register Spine. 직접 고치지 않음 -->\n");
            sb.Append("<CharacterSpineData>\n");
            foreach (SpineKeyInfo e in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
                sb.Append($"    <Spine Key=\"{e.Key}\" Character=\"{e.CharacterId}\" Type=\"{e.Type}\" Kind=\"{e.Kind}\" />\n");
            sb.Append("</CharacterSpineData>\n");
            return sb.ToString();
        }
    }
}
