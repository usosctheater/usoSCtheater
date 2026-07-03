using System.Collections.Generic;
using System.Xml;
using UnityEngine;

namespace UsoSCTheater.Ending
{
    /// <summary>
    /// Assets/Resources/EndingCredits/credits_{lang}.xml 을 파싱하여
    /// 줄 단위 문자열 리스트로 반환한다.
    /// </summary>
    public static class EndingCreditsLoader
    {
        public enum Language
        {
            KR,
            JP
        }

        /// <summary>
        /// 지정한 언어의 크레딧 XML을 로드한다.
        /// 파일이 없으면 KR로 폴백한다.
        /// </summary>
        public static List<string> LoadLines(Language language)
        {
            string resourcePath = GetResourcePath(language);
            TextAsset xmlAsset = Resources.Load<TextAsset>(resourcePath);

            if (xmlAsset == null && language != Language.KR)
            {
                Debug.LogWarning($"[EndingCreditsLoader] '{resourcePath}' 를 찾을 수 없어 KR로 폴백합니다.");
                xmlAsset = Resources.Load<TextAsset>(GetResourcePath(Language.KR));
            }

            if (xmlAsset == null)
            {
                Debug.LogError("[EndingCreditsLoader] 크레딧 XML 파일을 찾을 수 없습니다.");
                return new List<string>();
            }

            return ParseXml(xmlAsset.text);
        }

        private static string GetResourcePath(Language language)
        {
            string suffix = language == Language.JP ? "jp" : "kr";
            return $"EndingCredits/credits_{suffix}";
        }

        private static List<string> ParseXml(string xmlText)
        {
            var lines = new List<string>();

            var doc = new XmlDocument();
            doc.LoadXml(xmlText);

            XmlNodeList lineNodes = doc.SelectNodes("/EndingCredits/Line");
            if (lineNodes == null)
            {
                return lines;
            }

            foreach (XmlNode node in lineNodes)
            {
                // 빈 Line은 간격용 빈 줄로 그대로 추가
                lines.Add(node.InnerText ?? string.Empty);
            }

            return lines;
        }
    }
}
