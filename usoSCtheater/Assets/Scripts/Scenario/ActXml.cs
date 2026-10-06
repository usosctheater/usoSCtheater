using System;
using System.Xml;

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 막(Act) XML 공통 파싱 도우미.
    /// 루트 태그: 신규 &lt;Act&gt;, 기존 &lt;Scene&gt;도 계속 읽는다.
    /// 모든 막 XML과 엑셀 변환 템플릿(Resources/XML)이 &lt;Act&gt;로 전환되기 전까지 Legacy 폴백을 제거하지 말 것.
    /// </summary>
    public static class ActXml
    {
        public const string RootTag = "Act";
        public const string LegacyRootTag = "Scene";

        //[목록 UI] 막 헤더 속성 이름 (ScenarioCatalogSync에서 제목 읽기에 사용)
        public const string MainTitleAttr = "mainTitle";
        public const string SubTitleAttr = "subTitle";

        //루트 노드 반환 (<Act> 우선 → <Scene>), 둘 다 없으면 null
        public static XmlNode GetRoot(XmlDocument doc) =>
            doc.SelectSingleNode(RootTag) ?? doc.SelectSingleNode(LegacyRootTag);

        //[제목 읽기] 속성 값을 대소문자 구분 없이 조회 (예: subTitle / SubTitle 모두 허용). 없으면 ""
        //철자가 다른 속성(예: MainTtitle)은 읽지 않음 → 제목이 안 나오면 XML 오타 확인
        public static string GetAttrIgnoreCase(XmlNode node, string attrName)
        {
            if (node?.Attributes == null) return "";
            foreach (XmlAttribute attr in node.Attributes)
            {
                if (string.Equals(attr.Name, attrName, StringComparison.OrdinalIgnoreCase)) return attr.Value ?? "";
            }
            return "";
        }
    }
}
