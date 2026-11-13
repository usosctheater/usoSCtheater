using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEngine;
using UsoSCTheater.Shared;   //[공유 라이브러리] SpineKeys · SpineKeyInfo · SpineKind

namespace UsoSCTheater.Character
{
    //[공유 라이브러리] SpineKind · SpineKeyInfo · SpineKeys는 패키지 com.usosctheater.shared(Runtime/SpineKeys.cs)로 이동 (Unknown → Other)
    //  여기는 게임 전용 Spine 리소스 경로만 (Resources·spine-unity 값이라 공유 라이브러리에 두지 않음)
    public static class SpineResources
    {
        public const string SpineRoot = ResourcePaths.Spine;         // Resources/Spine //[리소스 정리] 폴더는 ResourcePaths
        public const string SkeletonDataSuffix = "_SkeletonData";    // spine-unity가 만드는 에셋 이름 접미어

        //Resources.Load 경로: Spine/{Id}/{Key}_SkeletonData (기존 SpineKeyInfo.ResourcePath 속성)
        public static string ResourcePath(this SpineKeyInfo info) => $"{ResourceFolders.SpineFolder(info.CharacterId)}/{info.Key}{SkeletonDataSuffix}";   //[공유 코드 통합] 수정(5회차): 배치 폴더는 공유 규칙
    }

    /// <summary>
    /// Spine 인덱스 Resources/Data/CharacterSpineData.xml — 등록 도구(Tools > Spine > Register Spine)가 자동 생성, 직접 고치지 않음.
    /// 빌드 게임은 Resources 폴더 목록을 볼 수 없으므로 "캐릭터별 Spine 목록"은 이 파일로만 안다.
    /// Character·Type·Kind 속성은 사람이 읽기 위한 것이고, 게임은 Key를 해석해 사용한다.
    /// </summary>
    public static class CharacterSpineData
    {
        public const string DataPath = ResourcePaths.Data + "/CharacterSpineData";                                    // Resources.Load 경로
        public const string AssetPath = ResourcePaths.AssetsRoot + "/" + ResourcePaths.Data + "/CharacterSpineData.xml"; // 등록 도구 쓰기 경로

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
