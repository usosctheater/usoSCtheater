using System.IO;
using System.Text.RegularExpressions;

namespace SCSpineManager.Services;

/// <summary>
/// 저장 폴더·파일 이름 규칙 (2026-10-08 확정)
/// - 폴더: {DressType}/{DressName}/ — Windows 파일명에 쓸 수 없는 문자는 지우기만 함 (치환 없음)
/// - 파일: {그룹 접두어}{spineType}_{ID} + .json / .atlas.txt / .png
///   그룹 접두어: idols 없음, awake_idols "awake_", support_idols "support_", idol_evolution_skins "evo_"
///   ID: 기본 enzaId. 서버 경로 마지막 폴더명이 enzaId로 시작하는 숫자면 그 값을 사용 (evo 스킨 …01~…04 구분)
/// </summary>
public static class SpineNaming
{
    public const string JsonExt  = ".json";
    public const string AtlasExt = ".atlas.txt";   // spine-unity가 바로 인식하는 확장자
    public const string ImageExt = ".png";

    private static readonly Dictionary<string, string> GroupPrefixes = new()
    {
        ["idols"] = "",
        ["awake_idols"] = "awake_",
        ["support_idols"] = "support_",
        ["idol_evolution_skins"] = "evo_",
    };

    /// <summary>모르는 그룹이 새로 생기면 그룹명을 그대로 접두어로 사용 (충돌 방지)</summary>
    public static string GroupPrefix(string group) =>
        GroupPrefixes.TryGetValue(group, out var p) ? p : group + "_";

    public static bool IsKnownGroup(string group) => GroupPrefixes.ContainsKey(group);

    /// <summary>파일 ID: 경로 마지막 폴더명이 enzaId로 시작하는 숫자면 그 값, 아니면 enzaId</summary>
    public static string FileId(string enzaId, string sourcePath)
    {
        string last = sourcePath.TrimEnd('/').Split('/').LastOrDefault() ?? "";
        if (last.Length > 0 && last.StartsWith(enzaId, StringComparison.Ordinal) && last.All(char.IsAsciiDigit))
            return last;
        return enzaId;
    }

    public static string BaseName(string group, string spineType, string fileId) =>
        $"{GroupPrefix(group)}{spineType}_{fileId}";

    /// <summary>저장 루트 기준 상대 폴더 {DressType}/{DressName}</summary>
    public static string RelativeFolder(string dressType, string dressName) =>
        Path.Combine(CleanName(dressType), CleanName(dressName));

    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    /// <summary>폴더/파일명 정리: 쓸 수 없는 문자 제거, 끝의 점·공백 제거, 예약어 회피</summary>
    public static string CleanName(string s)
    {
        string r = new string(s.Where(c => !Invalid.Contains(c)).ToArray()).Trim().TrimEnd('.', ' ');
        if (Regex.IsMatch(r, @"^(con|prn|aux|nul|com\d|lpt\d)(\..*)?$", RegexOptions.IgnoreCase)) r = "_" + r;
        return r.Length == 0 ? "_" : r;
    }
}
