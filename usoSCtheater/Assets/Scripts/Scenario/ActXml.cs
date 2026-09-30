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

        //루트 노드 반환 (<Act> 우선 → <Scene>), 둘 다 없으면 null
        public static XmlNode GetRoot(XmlDocument doc) =>
            doc.SelectSingleNode(RootTag) ?? doc.SelectSingleNode(LegacyRootTag);
    }
}
