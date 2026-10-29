using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;

namespace UsoSCTheater.Transition
{
    /// <summary>
    /// [Iris 아이콘] 트랜지션 연출 값 Resources/Data/TransitionData.xml (사람이 편집) 읽기 — static, Play 시작마다 다시 읽음 (2026-10-29)
    /// 숫자 값의 원본은 이 문서 하나 (인스펙터에는 오브젝트·Sprite 연결만). 나중에 다른 트랜지션 값도 여기로 옮길 예정
    /// 문서가 없거나 깨지면 코드 기본값 + 경고 (연출만 기본값으로 동작)
    /// </summary>
    public static class TransitionData
    {
        public const string DataPath = ResourcePaths.Data + "/TransitionData";   // Resources.Load 경로
        public const string DefaultIconId = "default";                           // ScenarioData Iris="Default" · 기본 아이콘(283프로)의 보정 Id

        private const string IrisIconDefaultTag = "IrisIconDefault";
        private const string IrisIconTag = "IrisIcon";

        /// <summary>Iris 가운데 아이콘 표시 값. Scale = 배경 원 지름 대비 배율, OffsetY = 위로 올리는 양(배경 원 반지름 대비)</summary>
        public readonly struct IrisIconStyle
        {
            public readonly float Scale;
            public readonly float OffsetY;
            public IrisIconStyle(float scale, float offsetY) { Scale = scale; OffsetY = offsetY; }
        }

        // 문서의 IrisIconDefault가 없을 때만 쓰는 코드 기본값
        private const float FallbackUnitScale = 0.9f;
        private const float FallbackMemberScale = 0.8f;
        private const float FallbackDefaultScale = 0.9f;

        private static float unitScale, memberScale, defaultScale;
        private static Dictionary<string, float> scales;     // 아이콘 Id → 배율 (대소문자 무시, 지정한 것만)
        private static Dictionary<string, float> offsets;    // 아이콘 Id → OffsetY (대소문자 무시, 지정한 것만)

        //Domain Reload 비활성 → Play 시작마다 static 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => scales = null;

        //아이콘 Id + 종류 → 배율·Y 보정 (항목이 없으면 종류별 기본 배율, OffsetY 0)
        public static IrisIconStyle GetIrisIconStyle(string id, bool isUnit, bool isDefault)
        {
            EnsureLoaded();
            float baseScale = isDefault ? defaultScale : (isUnit ? unitScale : memberScale);
            if (string.IsNullOrEmpty(id)) return new IrisIconStyle(baseScale, 0f);

            float scale = scales.TryGetValue(id, out float s) ? s : baseScale;
            float offsetY = offsets.TryGetValue(id, out float o) ? o : 0f;
            return new IrisIconStyle(scale, offsetY);
        }

        private static void EnsureLoaded()
        {
            if (scales != null) return;
            scales = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            offsets = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            unitScale = FallbackUnitScale;
            memberScale = FallbackMemberScale;
            defaultScale = FallbackDefaultScale;

            TextAsset xml = Resources.Load<TextAsset>(DataPath);
            if (xml == null)
            {
                Debug.LogWarning($"[TransitionData] Resources/{DataPath}.xml 없음 → 코드 기본값 사용");
                return;
            }

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml.text);

                foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                {
                    if (node.NodeType != XmlNodeType.Element) continue;

                    if (node.Name == IrisIconDefaultTag)
                    {
                        unitScale = ReadFloat(node, "Unit", unitScale);
                        memberScale = ReadFloat(node, "Member", memberScale);
                        defaultScale = ReadFloat(node, "Default", defaultScale);
                    }
                    else if (node.Name == IrisIconTag)
                    {
                        string id = node.Attributes["Id"]?.Value?.Trim();
                        if (string.IsNullOrEmpty(id)) { Debug.LogWarning("[TransitionData] Id 없는 IrisIcon 줄 → 건너뜀"); continue; }
                        if (scales.ContainsKey(id) || offsets.ContainsKey(id)) { Debug.LogWarning($"[TransitionData] IrisIcon Id '{id}' 중복 → 첫 항목만 사용"); continue; }

                        if (node.Attributes["Scale"] != null) scales[id] = ReadFloat(node, "Scale", defaultScale);
                        if (node.Attributes["OffsetY"] != null) offsets[id] = ReadFloat(node, "OffsetY", 0f);
                    }
                }
            }
            catch (XmlException e)
            {
                Debug.LogWarning($"[TransitionData] TransitionData.xml 파싱 실패 → 코드 기본값 사용 — {e.Message}");
            }
        }

        //숫자 속성 읽기 (없거나 숫자가 아니면 fallback + 경고). 소수점은 항상 '.'
        private static float ReadFloat(XmlNode node, string attr, float fallback)
        {
            string value = node.Attributes[attr]?.Value;
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result)) return result;
            Debug.LogWarning($"[TransitionData] <{node.Name}> {attr}=\"{value}\" 숫자가 아님 → {fallback}");
            return fallback;
        }
    }
}
