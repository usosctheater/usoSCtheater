#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace UsoSCTheater.Shared
{
    /// <summary>막 XML 형식 (게임·TE 공용). 속성 이름은 대소문자 구분, 값(Type·Effect·키·ID)은 무시 (2026-11-11)</summary>
    public static class ActFormat
    {
        public const string RootTag = "Act", LineTag = "Line";
        public const string MainTitle = "MainTitle", SubTitle = "SubTitle";   //루트 (이전 mainTitle·subTitle)

        //Line 속성
        public const string Type = "Type", GroupId = "GroupId", CharId = "CharId", Name = "Name", Text = "Text", Voice = "Voice",
                            CG = "CG", Position = "Position", Animation = "Animation", ResourceKey = "ResourceKey",
                            Value = "Value", Track = "Track", Volume = "Volume", Effect = "Effect", SE = "SE", Duration = "Duration", SpeakerType = "SpeakerType";

        //종류 (값 — 대문자로 비교)
        public const string TypeText = "TEXT", TypeAudio = "AUDIO", TypeSE = "SE", TypeTransition = "TRANSITION", TypeBG = "BG",
                            TypeBreak = "BREAK", TypeSetCG = "SETCG", TypeCGGroup = "CGGROUP", TypeImage = "IMAGE";
        public const string CgNone = "none";   //CG="none" = 모두 내림 (공유 라이브러리 통합 6회차에 폐기 → SETCG Effect="clean_all")

        public static readonly IReadOnlyList<string> RootAttrs = new[] { MainTitle, SubTitle };
        public static readonly IReadOnlyList<string> LineAttrs = new[]
            { Type, GroupId, CharId, Name, Text, Voice, CG, Position, Animation, ResourceKey, Value, Track, Volume, Effect, SE, Duration, SpeakerType };
        public static readonly IReadOnlyList<string> TranslatableLineAttrs = new[] { Name, Text };   //TEXT 줄 (루트는 RootAttrs 전부)

        //속성 이름 교정: 아는 이름(번역 속성 포함)과 대소문자만 다르면 올바른 이름, 아니면 null (정확히 맞거나 모르는 이름)
        public static string? CorrectName(string name, bool root, IEnumerable<LanguageInfo> languages)
        {
            foreach (string known in KnownNames(root, languages))
                if (string.Equals(name, known, StringComparison.OrdinalIgnoreCase)) return name == known ? null : known;
            return null;
        }

        private static IEnumerable<string> KnownNames(bool root, IEnumerable<LanguageInfo> languages)
        {
            IReadOnlyList<string> plain = root ? RootAttrs : LineAttrs;
            IReadOnlyList<string> translatable = root ? RootAttrs : TranslatableLineAttrs;
            foreach (string a in plain) yield return a;
            foreach (LanguageInfo l in languages)
                if (!l.IsDefault)
                    foreach (string a in translatable) yield return LanguageRules.AttrName(a, l);
        }
    }

    /// <summary>
    /// 막의 루트 또는 줄 1개의 속성 목록 읽기 (게임: XmlNode 속성을 옮긴 목록, TE: ScriptLine.Attributes 그대로).
    /// 이름은 정확히 찾음, 키 값은 앞뒤 공백 지움, 숫자는 InvariantCulture. 빈 값은 없는 것과 같음
    /// </summary>
    public readonly struct ActAttrs
    {
        private readonly IReadOnlyList<KeyValuePair<string, string>>? attrs;

        public ActAttrs(IReadOnlyList<KeyValuePair<string, string>> attrs) => this.attrs = attrs;

        public IReadOnlyList<KeyValuePair<string, string>> All => attrs ?? Array.Empty<KeyValuePair<string, string>>();

        //속성 값 그대로 (없으면 null)
        public string? Find(string name)
        {
            foreach (var a in All) if (a.Key == name) return a.Value;
            return null;
        }

        public string Raw(string name) => Find(name) ?? "";        //대사·이름·제목처럼 그대로 쓰는 값
        public string Key(string name) => Raw(name).Trim();          //키·종류·효과 등 (앞뒤 공백 지움)
        public string Type => Key(ActFormat.Type).ToUpperInvariant();
        public bool Is(string type) => Type == type;

        //값 비교 (값은 대소문자 무시)
        public bool KeyIs(string name, string value) => string.Equals(Key(name), value, StringComparison.OrdinalIgnoreCase);

        public double Number(string name, double fallback) =>
            double.TryParse(Key(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        public int Int(string name, int fallback) =>
            int.TryParse(Key(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        //언어별 값: 기본 언어가 아니면 {이름}_{접미어} → 비었으면 기본 속성
        public string Localized(string name, LanguageInfo language) =>
            !language.IsDefault && Find(LanguageRules.AttrName(name, language)) is { Length: > 0 } v ? v : Raw(name);
    }

    /// <summary>스크립트 검사 결과 1건 (Attr = 문제가 있는 속성 이름)</summary>
    public sealed class ActIssue
    {
        public string Attr { get; }
        public string Message { get; }

        public ActIssue(string attr, string message)
        {
            Attr = attr;
            Message = message;
        }
    }

    /// <summary>막 스크립트 규칙 (게임·TE 공용): CG 키 모으기, 스크립트 검사</summary>
    public static class ActScript
    {
        //CG 속성을 쓰는 줄 종류
        public static bool UsesCg(string type) => type == ActFormat.TypeText || type == ActFormat.TypeSetCG || type == ActFormat.TypeCGGroup;

        //막의 CGGROUP 그룹 이름 (GroupId가 빈 줄은 무시, 값이라 대소문자 무시)
        public static HashSet<string> GroupIds(IEnumerable<ActAttrs> lines)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ActAttrs l in lines)
                if (l.Is(ActFormat.TypeCGGroup) && l.Key(ActFormat.GroupId) is { Length: > 0 } id) ids.Add(id);
            return ids;
        }

        //이 줄이 쓰는 Spine 키. 없으면 null: CG를 쓰지 않는 줄, 빈 CG, none, 그룹 이름 참조, GroupId가 빈 CGGROUP 줄
        public static string? SpineKeyOf(ActAttrs line, ISet<string> groupIds)
        {
            string type = line.Type;
            if (!UsesCg(type)) return null;
            if (type == ActFormat.TypeCGGroup && line.Key(ActFormat.GroupId).Length == 0) return null;
            string cg = line.Key(ActFormat.CG);
            if (cg.Length == 0 || string.Equals(cg, ActFormat.CgNone, StringComparison.OrdinalIgnoreCase) || groupIds.Contains(cg)) return null;
            return cg;
        }

        //막이 쓰는 Spine 키 (나온 순서, 중복 없음 — 대소문자 무시)
        public static List<string> SpineKeys(IReadOnlyList<ActAttrs> lines)
        {
            var groups = GroupIds(lines);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keys = new List<string>();
            foreach (ActAttrs l in lines)
                if (SpineKeyOf(l, groups) is { } k && seen.Add(k)) keys.Add(k);
            return keys;
        }

        //스크립트 검사 (경고만): 대소문자가 다른 속성 이름, 잘못된 언어 접미어, DB에 없는 CharId(TEXT), GroupId가 빈 CGGROUP
        public static void CheckLine(ActAttrs line, bool root, IReadOnlyList<LanguageInfo> languages, Func<string, bool> characterExists, ICollection<ActIssue> issues)
        {
            foreach (var a in line.All)
            {
                if (ActFormat.CorrectName(a.Key, root, languages) is { } correct)
                    issues.Add(new ActIssue(a.Key, $"대소문자가 다른 속성 '{a.Key}' → '{correct}' (읽히지 않음)"));
                else if (LanguageRules.HasInvalidSuffix(a.Key, languages))
                    issues.Add(new ActIssue(a.Key, $"잘못된 언어 접미어 속성 '{a.Key}' (LanguageData에 없는 언어이거나 기본 언어 — 기본 언어는 접미어 없이)"));
            }
            if (root) return;

            if (line.Is(ActFormat.TypeText))
                foreach (string id in CharIds.Split(line.Raw(ActFormat.CharId)))
                    if (!characterExists(id)) issues.Add(new ActIssue(ActFormat.CharId, $"CharacterData에 없는 CharId '{id}'"));

            if (line.Is(ActFormat.TypeCGGroup) && line.Key(ActFormat.GroupId).Length == 0)
                issues.Add(new ActIssue(ActFormat.GroupId, "GroupId가 없는 CGGROUP (무시됨)"));
        }
    }
}
