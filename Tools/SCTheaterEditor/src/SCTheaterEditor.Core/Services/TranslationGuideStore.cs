using System.IO;
using System.Text;

namespace SCTheaterEditor.Services;

/// <summary>
/// 추가(번역 모드 4회차): 언어별 번역 지침 파일 = exe 옆 TranslationGuide_{언어 코드}.md (Git 제외 — 작업자마다 자기 스타일).
/// 파일이 없어도 동작 (가번역은 지침 없이 요청, 처음 저장할 때 파일이 생김)
/// </summary>
public sealed class TranslationGuideStore(string folder)
{
    public static string DefaultFolder => AppContext.BaseDirectory;

    public string Folder { get; } = folder;

    public string PathFor(string code) => Path.Combine(Folder, $"TranslationGuide_{code.ToLowerInvariant()}.md");

    /// <summary>지침 내용 (파일이 없으면 null)</summary>
    public string? Load(string code)
    {
        string p = PathFor(code);
        return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : null;
    }

    public void Save(string code, string text)
    {
        Directory.CreateDirectory(Folder);
        SafeFile.WriteAllBytes(PathFor(code), new UTF8Encoding(false).GetBytes(text));
    }
}
