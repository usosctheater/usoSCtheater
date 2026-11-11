#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace UsoSCTheater.Shared
{
    /// <summary>지원 언어 1개 (LanguageData.xml의 Language 1줄). 게임·TE 공용</summary>
    public sealed class LanguageInfo
    {
        public string Code { get; }            //언어 코드 (소문자, 예: ko, ja)
        public string NativeName { get; }      //그 언어로 쓴 언어 이름
        public bool IsDefault { get; }         //기본 언어 (속성 접미어 없음)
        public string ListSeparator { get; }   //여러 이름을 이어 붙일 때 구분자
        public string Suffix { get; }          //속성 접미어 표기 = 첫 글자 대문자 (ja → Ja)

        public LanguageInfo(string code, string nativeName, bool isDefault, string listSeparator = ", ")
        {
            Code = code;
            NativeName = nativeName;
            IsDefault = isDefault;
            ListSeparator = listSeparator;
            Suffix = code.Length == 0 ? "" : char.ToUpperInvariant(code[0]) + code.Substring(1).ToLowerInvariant();
        }
    }

    /// <summary>
    /// 언어 규칙: LanguageData.xml 해석, 언어별 속성 이름 {속성}_{접미어}, 잘못된 접미어 판정
    /// 속성 이름은 대소문자 구분, 값(Code 등)은 무시 (2026-11-11 결정)
    /// </summary>
    public static class LanguageRules
    {
        public const string DataName = "LanguageData";   //Resources/Data/LanguageData.xml, TE Data\LanguageData.xml
        public const string Separator = "_";

        //XML → 언어 목록. 문제는 warnings에 한 줄씩. 쓸 수 있는 언어가 없으면 fallback (프로그램마다: 게임 ko / TE ko·ja)
        //기본 언어 = Default="true"인 첫 줄, 없으면 첫 줄. NativeName이 없으면 코드, ListSeparator가 없으면 ", "
        public static IReadOnlyList<LanguageInfo> Parse(string? xml, IReadOnlyList<LanguageInfo> fallback, ICollection<string> warnings)
        {
            XElement? root = null;
            if (string.IsNullOrWhiteSpace(xml)) warnings.Add("LanguageData가 없거나 비어 있음 → 기본 목록 사용");
            else
            {
                try { root = XDocument.Parse(xml!).Root; }
                catch (XmlException e) { warnings.Add($"LanguageData XML 오류 → 기본 목록 사용 ({e.Message})"); }
            }

            var items = new List<(string Code, string Native, bool Default, string Separator)>();
            foreach (XElement e in root?.Elements("Language") ?? Enumerable.Empty<XElement>())
            {
                string code = ((string?)e.Attribute("Code") ?? "").Trim().ToLowerInvariant();
                if (code.Length == 0 || items.Any(x => x.Code == code))
                {
                    warnings.Add($"LanguageData: Code 없음 또는 중복('{code}') → 건너뜀");
                    continue;
                }
                bool isDefault = string.Equals(((string?)e.Attribute("Default"))?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
                items.Add((code, (string?)e.Attribute("NativeName") ?? code, isDefault, (string?)e.Attribute("ListSeparator") ?? ", "));
            }
            if (items.Count == 0)
            {
                if (root != null) warnings.Add("LanguageData에 쓸 수 있는 Language가 없음 → 기본 목록 사용");
                return fallback;
            }
            if (items.Count(x => x.Default) > 1) warnings.Add("LanguageData: Default가 여러 개 → 첫 항목 사용");
            int def = Math.Max(0, items.FindIndex(x => x.Default));
            return items.Select((x, i) => new LanguageInfo(x.Code, x.Native, i == def, x.Separator)).ToList();
        }

        //코드로 찾기 (값이라 대소문자 무시), 없으면 null
        public static LanguageInfo? Find(IEnumerable<LanguageInfo> languages, string? code)
        {
            string c = code?.Trim() ?? "";
            return c.Length == 0 ? null : languages.FirstOrDefault(l => string.Equals(l.Code, c, StringComparison.OrdinalIgnoreCase));
        }

        //언어별 속성 이름: 기본 언어 → attr, 그 외 → attr_{접미어} (Text_Ja)
        public static string AttrName(string attr, LanguageInfo language) => language.IsDefault ? attr : attr + Separator + language.Suffix;

        //속성 사전(이름 → 값)에서 한 언어 값만 (대체 없음, 빈 값은 없는 것으로)
        public static bool TryGet(IReadOnlyDictionary<string, string> attrs, string attr, LanguageInfo language, out string value)
        {
            if (attrs.TryGetValue(AttrName(attr, language), out string? v) && v.Length > 0) { value = v; return true; }
            value = "";
            return false;
        }

        //잘못된 언어 접미어인지: 마지막 '_' 뒤가 기본 언어가 아닌 언어의 접미어(Ja 등)와 정확히 같지 않음. '_'가 없으면 false
        public static bool HasInvalidSuffix(string attrName, IEnumerable<LanguageInfo> languages)
        {
            int index = attrName.LastIndexOf(Separator, StringComparison.Ordinal);
            if (index <= 0 || index == attrName.Length - 1) return false;
            string suffix = attrName.Substring(index + 1);
            return !languages.Any(l => !l.IsDefault && l.Suffix == suffix);
        }
    }

    /// <summary>캐릭터·유닛 이름의 언어별 대체 순서 (게임 표시 기준): 요청 형식 → 같은 언어 다른 형식 → 기본 언어 요청 형식 → 기본 언어 다른 형식 → Id</summary>
    public static class LocalizedNames
    {
        public const string Name = "Name";           //이름 (언어별: Name_Ja …)
        public const string FullName = "FullName";   //성까지 포함한 이름 (FullName_Ja …)

        public static string Get(IReadOnlyDictionary<string, string> attrs, string id, LanguageInfo language, LanguageInfo defaultLanguage, bool full)
        {
            string primary = full ? FullName : Name, other = full ? Name : FullName;
            if (LanguageRules.TryGet(attrs, primary, language, out string name)) return name;
            if (LanguageRules.TryGet(attrs, other, language, out name)) return name;
            if (LanguageRules.TryGet(attrs, primary, defaultLanguage, out name)) return name;
            if (LanguageRules.TryGet(attrs, other, defaultLanguage, out name)) return name;
            return id;
        }
    }

    /// <summary>막 XML CharId 값 (쉼표로 여러 명)</summary>
    public static class CharIds
    {
        //"a, b" → [a, b] (앞뒤 공백·빈 항목 제외)
        public static IEnumerable<string> Split(string? ids) =>
            (ids ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);

        //여러 이름을 그 언어 구분자로 이어 붙임 (ko ", ", ja "、")
        public static string Join(IEnumerable<string> names, LanguageInfo language) => string.Join(language.ListSeparator, names);
    }

    /// <summary>Id가 있는 데이터 문서 1줄 (CharacterData의 Character, UnitData의 Unit, UITextData의 Text)</summary>
    public sealed class IdEntry
    {
        public string Id { get; }
        public IReadOnlyDictionary<string, string> Attrs { get; }   //Id 외 속성 (이름은 대소문자 구분, 값은 앞뒤 공백 지움)

        public IdEntry(string id, IReadOnlyDictionary<string, string> attrs)
        {
            Id = id;
            Attrs = attrs;
        }
    }

    /// <summary>Id 데이터 문서 해석 (게임·TE 공용). 문제는 warnings에 한 줄씩, 예외를 던지지 않음</summary>
    public static class IdTable
    {
        //element 요소만, 파일 순서. Id 없음은 건너뜀, Id 중복(값이라 대소문자 무시)은 첫 항목. 비었거나 XML 오류는 빈 목록
        public static List<IdEntry> Parse(string? xml, string element, ICollection<string> warnings)
        {
            var list = new List<IdEntry>();
            if (string.IsNullOrWhiteSpace(xml))
            {
                warnings.Add("데이터가 없거나 비어 있음");
                return list;
            }
            XElement? root;
            try { root = XDocument.Parse(xml!).Root; }
            catch (XmlException e)
            {
                warnings.Add($"XML 오류 ({e.Message})");
                return list;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement e in root?.Elements(element) ?? Enumerable.Empty<XElement>())
            {
                string id = ((string?)e.Attribute("Id") ?? "").Trim();
                if (id.Length == 0) { warnings.Add($"Id 없는 {element} → 건너뜀"); continue; }
                if (!seen.Add(id)) { warnings.Add($"Id '{id}' 중복 → 첫 항목 사용"); continue; }
                var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (XAttribute a in e.Attributes())
                    if (a.Name.LocalName != "Id") attrs[a.Name.LocalName] = a.Value.Trim();
                list.Add(new IdEntry(id, attrs));
            }
            return list;
        }
    }
}
