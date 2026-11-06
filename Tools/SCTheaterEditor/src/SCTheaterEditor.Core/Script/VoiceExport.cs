using System.IO;
using System.Text;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.Script;

/// <summary>추가(번역 모드 5회차): 내보내기 결과 — Text = 파일 내용, Lines = 대사 수, Warnings = 상태 줄 ⚠ (이용 제한 캐릭터·없는 화자 Id)</summary>
public sealed record VoiceExportResult(string Text, int Lines, IReadOnlyList<string> Warnings);

/// <summary>
/// 추가(번역 모드 5회차): VOICEVOX용 내보내기 (화면·파일과 분리 — 테스트 가능).
/// - 대사 TXT: VOICEVOX 「テキスト読み込み」 형식. 화자가 바뀔 때만 화자 전환 줄(VoiceMatchData.SpeakerLine), TEXT 줄 1개 = 대사 1줄.
///   대사 안 \n(게임 표시용 줄바꿈)은 지우고 이어 붙임, 반각 ','는 '、'(VOICEVOX가 ','에서 칸을 나눔). 번역이 빈 줄·제목·부제는 넣지 않음.
///   화자 = 첫 번째 CharId의 매칭 → 기본 화자 → 없으면 전환 줄 없음 (VOICEVOX에서 직접 지정)
/// - 라이선스 TXT: 그룹(시나리오)의 모든 막에서 대사가 있는 TEXT 줄의 화자 → "音声: VOICEVOX (…)" + 크레딧 줄 (VoiceMatch 순서)
/// 파일: UTF-8(BOM 없음 — BOM이 있으면 첫 화자 이름이 맞지 않음), CRLF
/// </summary>
public static class VoiceExport
{
    public const string LicenseHeader = "音声: VOICEVOX (https://voicevox.hiroshiba.jp/)";
    public static readonly Encoding FileEncoding = new UTF8Encoding(false);
    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "VoiceExport");

    public static string ScriptFileName(ActDocument doc, LanguageInfo lang) => $"{doc.ActName}_{lang.Code.ToLowerInvariant()}.txt";
    public static string LicenseFileName(string group) => $"{group}_license.txt";

    /// <summary>대사 한 줄 정리: \n(두 글자)·실제 줄바꿈 제거, 반각 ',' → '、'</summary>
    public static string CleanLine(string text) =>
        text.Replace("\\n", "").Replace("\r", "").Replace("\n", "").Replace(",", "、").Trim();

    public static VoiceExportResult BuildScript(ActDocument doc, LanguageInfo lang, VoiceMatchData voices)
    {
        var sb = new StringBuilder();
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var used = new List<VoiceInfo>();
        string? last = null;
        int n = 0;
        string attr = lang.IsDefault ? "Text" : $"Text_{lang.Suffix}";
        foreach (var line in doc.Lines)
        {
            if (!IsText(line)) continue;
            string text = CleanLine(line.Get(attr));
            if (text.Length == 0) continue;
            if (voices.Resolve(line.Get("CharId"), unknown) is ({ } voice, var style))
            {
                string speaker = VoiceMatchData.SpeakerLine(voice, style);
                if (speaker != last) { sb.Append(speaker).Append("\r\n"); last = speaker; }
                if (!used.Contains(voice)) used.Add(voice);
            }
            sb.Append(text).Append("\r\n");
            n++;
        }
        return new(sb.ToString(), n, Warnings(used, unknown));
    }

    public static VoiceExportResult BuildLicense(IEnumerable<ActDocument> acts, VoiceMatchData voices)
    {
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<VoiceInfo>();
        foreach (var doc in acts)
            foreach (var line in doc.Lines)
                if (IsText(line) && line.Get("Text").Trim().Length > 0 && voices.Resolve(line.Get("CharId"), unknown) is ({ } voice, _))
                    used.Add(voice);
        var ordered = voices.Voices.Where(used.Contains).ToList();   // VoiceMatch.xml 순서
        var sb = new StringBuilder(LicenseHeader).Append("\r\n");
        foreach (var v in ordered) sb.Append(v.Credit.Length > 0 ? v.Credit : $"VOICEVOX:{v.Name}").Append("\r\n");
        return new(sb.ToString(), ordered.Count, Warnings(ordered, unknown));
    }

    /// <summary>저장 폴더(비면 기본 VoiceExport\)에 씀 (같은 이름은 덮어씀). 쓴 경로 반환</summary>
    public static string WriteFile(string? folder, string fileName, string text)
    {
        string dir = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        SafeFile.WriteAllBytes(path, FileEncoding.GetBytes(text));
        return path;
    }

    private static List<string> Warnings(IEnumerable<VoiceInfo> used, IEnumerable<string> unknown)
    {
        var list = used.Select(v => v.Warning).OfType<string>().ToList();
        list.AddRange(unknown.Select(id => $"VoiceMatch.xml에 없는 화자 Id '{id}' — 그 줄은 화자 전환 없이 내보냄"));
        return list;
    }

    private static bool IsText(ScriptLine l) => l.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase);
}
