#nullable enable
using System;
using System.Collections.Generic;
using System.Xml;

namespace UsoSCTheater.Shared
{
    /// <summary>[공유 코드 통합] 추가(5회차): 애니메이션 1개의 재생 방식</summary>
    public enum SpineAnimMode   //UnityEngine.AnimationPlayMode 등과 겹치지 않는 이름
    {
        Hold,      //1회 재생 후 마지막 동작 유지
        Loop,      //계속 반복 (wait·blank)
        EndLoop,   //1회 재생 후 loop_start 시점부터 반복
    }

    /// <summary>
    /// [공유 코드 통합] 추가(5회차): Spine 애니메이션 재생 규칙 — 게임 CGManager·TE 무대 공용 (TE JS는 C#이 정한 트랙·방식대로 재생만).
    /// Animation 값 = 공백·쉼표로 나눈 애니메이션 이름들, 접두어로 트랙을 정함, 같은 트랙에 여러 개면 뒤의 것이 남음
    /// </summary>
    public static class AnimationRules
    {
        public const int TrackBase = 0, TrackFace = 1, TrackLip = 2, TrackArm = 3, TrackCount = 4;
        public const string LoopStartEvent = "loop_start";   //Spine 이벤트: 있으면 EndLoop
        private static readonly HashSet<string> forceLoop = new HashSet<string> { "wait", "blank" };   //이름은 정확히 (Spine 애니메이션 이름)
        public static bool IsForceLoop(string name) => forceLoop.Contains(name);

        public static List<string> Split(string? animation) => ActFormat.SplitList(animation);

        //접두어 → 트랙 (face_ 1, lip_ 2, arm_ 3, 그 밖 0 — eye_도 0). 접두어는 대소문자 무시
        public static int TrackOf(string name)
        {
            string lower = name.Trim().ToLowerInvariant();
            if (lower.StartsWith("face_", StringComparison.Ordinal)) return TrackFace;
            if (lower.StartsWith("lip_", StringComparison.Ordinal)) return TrackLip;
            if (lower.StartsWith("arm_", StringComparison.Ordinal)) return TrackArm;
            return TrackBase;
        }

        //재생 방식: loop_start 이벤트가 우선, 다음 강제 반복, 나머지 1회 후 유지
        public static SpineAnimMode ModeOf(string name, bool hasLoopStart) =>
            hasLoopStart ? SpineAnimMode.EndLoop : IsForceLoop(name) ? SpineAnimMode.Loop : SpineAnimMode.Hold;
    }

    /// <summary>
    /// [공유 코드 통합] 추가(5회차): 아이돌별 기본 립 (Resources/Data/DefaultLipData.xml — &lt;아이돌 키&gt;&lt;키워드 Moving Still/&gt;…).
    /// 요소 이름(아이돌 키·키워드)은 값이라 대소문자 무시
    /// </summary>
    public sealed class LipData
    {
        private readonly Dictionary<string, Dictionary<string, (string? Moving, string? Still)>> idols =
            new Dictionary<string, Dictionary<string, (string? Moving, string? Still)>>(StringComparer.OrdinalIgnoreCase);

        public static LipData Empty => new LipData();

        //XML 형식이 깨졌으면 XmlException
        public static LipData Parse(string xml)
        {
            var data = new LipData();
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(xml);
            if (doc.DocumentElement == null) return data;
            foreach (XmlNode idol in doc.DocumentElement.ChildNodes)
            {
                if (idol.NodeType != XmlNodeType.Element) continue;
                var categories = new Dictionary<string, (string?, string?)>(StringComparer.OrdinalIgnoreCase);
                foreach (XmlNode cat in idol.ChildNodes)
                    if (cat.NodeType == XmlNodeType.Element)
                        categories[cat.Name] = (cat.Attributes?["Moving"]?.Value, cat.Attributes?["Still"]?.Value);
                data.idols[idol.Name] = categories;
            }
            return data;
        }

        //TE: 다시 읽은 내용으로 바꿈 (이 객체를 받아 둔 쪽이 새 내용을 봄)
        public void ReplaceWith(LipData other)
        {
            idols.Clear();
            foreach (var kv in other.idols) idols[kv.Key] = kv.Value;
        }

        //{아이돌}/{키워드} → {아이돌}/Default → 없으면 null
        public bool TryGet(string idolKey, string keyword, out (string? Moving, string? Still) entry)
        {
            entry = default;
            if (!idols.TryGetValue(idolKey, out var categories)) return false;
            return categories.TryGetValue(keyword.Length == 0 ? LipRules.DefaultCategory : keyword, out entry)
                || categories.TryGetValue(LipRules.DefaultCategory, out entry);
        }
    }

    /// <summary>
    /// [공유 코드 통합] 추가(5회차): 립 애니메이션 고르기 — 게임 CGManager.LipSyncCoroutine·TE 무대 공용.
    /// - Animation에 lip_xxx가 있으면: 말하는 중 = lip_xxx, 멈춤 = lip_xxx_s (없으면 아이돌별 기본값의 Still)
    /// - 없으면: 기본 트랙 애니메이션에서 키워드(끝 숫자 제거, anger1 → anger, 없으면 wait) → lip_{키워드} / lip_{키워드}_s, 없는 쪽은 아이돌별 기본값
    /// - 아이돌별 기본값: {아이돌}/{키워드} → {아이돌}/Default → 아이돌이 없으면 lip_wait / lip_wait_s (있을 때만)
    /// </summary>
    public static class LipRules
    {
        public const string LipPrefix = "lip_", StillSuffix = "_s", DefaultCategory = "Default", DefaultKeyword = "wait";
        public const string FallbackMoving = "lip_wait", FallbackStill = "lip_wait_s";

        //CG 키 → 아이돌 키 (첫 _ 앞, mei_stand_… → mei)
        public static string IdolKeyOf(string cgKey)
        {
            int i = cgKey.IndexOf('_');
            return i > 0 ? cgKey.Substring(0, i) : cgKey;
        }

        //anger1 → anger, smile3 → smile, 비어 있으면 wait
        public static string KeywordOf(string? animation) =>
            string.IsNullOrEmpty(animation) ? DefaultKeyword : animation!.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

        //animations = Animation 값을 나눈 이름들, exists = 그 Spine에 애니메이션이 있는지. 고를 수 없으면 null
        public static (string? Moving, string? Still) Resolve(IReadOnlyList<string> animations, string idolKey, LipData data, Func<string, bool> exists)
        {
            string? explicitLip = null, track0 = null;
            foreach (string a in animations)
            {
                if (explicitLip == null && a.StartsWith(LipPrefix, StringComparison.Ordinal)) explicitLip = a;
                if (track0 == null && AnimationRules.TrackOf(a) == AnimationRules.TrackBase) track0 = a;
            }

            if (explicitLip != null)
            {
                string still = explicitLip + StillSuffix;
                if (exists(still)) return (explicitLip, still);
                return (explicitLip, Default(idolKey, KeywordOf(explicitLip.Substring(LipPrefix.Length)), data, exists).Still);
            }

            string keyword = KeywordOf(track0);
            string moving = LipPrefix + keyword, stillName = moving + StillSuffix;
            bool hasMoving = exists(moving), hasStill = exists(stillName);
            var def = hasMoving && hasStill ? default : Default(idolKey, keyword, data, exists);
            return (hasMoving ? moving : def.Moving, hasStill ? stillName : def.Still);
        }

        private static (string? Moving, string? Still) Default(string idolKey, string keyword, LipData data, Func<string, bool> exists)
        {
            if (data.TryGet(idolKey, keyword, out var entry)) return entry;
            return (exists(FallbackMoving) ? FallbackMoving : null, exists(FallbackStill) ? FallbackStill : null);
        }
    }
}
