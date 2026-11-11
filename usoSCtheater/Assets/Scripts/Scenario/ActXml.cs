using System.Collections.Generic;
using System.Xml;
using UsoSCTheater.Shared;   //[공유 코드 통합] ActFormat · ActAttrs

namespace UsoSCTheater.Scenario
{
    /// <summary>
    /// 막(Act) XML ↔ 공유 ActAttrs 연결 (게임 쪽 XmlNode만 다룸). 형식·규칙은 공유 ActFormat·ActScript
    /// [공유 코드 통합] 삭제: RootTag·LegacyRootTag(&lt;Scene&gt; 대체 읽기 — 모든 막을 &lt;Act&gt;로 변환)·MainTitleAttr·SubTitleAttr·GetAttrIgnoreCase (속성 이름은 대소문자 구분)
    /// (엑셀 XML 변환 템플릿 Resources/XML은 2026-10-30 삭제 — 스크립트 작성은 TE로)
    /// </summary>
    public static class ActXml
    {
        //루트 노드 (<Act>), 없으면 null
        public static XmlNode GetRoot(XmlDocument doc) => doc.SelectSingleNode(ActFormat.RootTag);

        //노드 속성 → 공유 ActAttrs (파일 순서). node가 null이면 빈 목록
        public static ActAttrs Attrs(XmlNode node)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (node?.Attributes != null)
                foreach (XmlAttribute a in node.Attributes) list.Add(new KeyValuePair<string, string>(a.Name, a.Value));
            return new ActAttrs(list);
        }

        //루트 바로 아래 <Line> 줄들 (순서대로). root가 null이면 빈 목록
        public static List<ActAttrs> Lines(XmlNode root)
        {
            var lines = new List<ActAttrs>();
            if (root == null) return lines;
            foreach (XmlNode n in root.ChildNodes)
                if (n.NodeType == XmlNodeType.Element && n.Name == ActFormat.LineTag) lines.Add(Attrs(n));
            return lines;
        }
    }
}
