using System.IO;

namespace SCTheaterEditor.Services;

/// <summary>
/// 배경 이미지 목록: 변경(5회차) 게임 Resources\BG 폴더 (이전: exe 옆 Backgrounds\ — 게임과 같은 파일을 복사해 두던 것을 없앰).
/// 파일 이름(확장자 제외) = 막 XML의 BG 키 (bg_office_day.png → bg_office_day). 게임 폴더는 읽기만 하고 만들지 않음
/// </summary>
public sealed class BackgroundLibrary
{
    public static readonly IReadOnlyList<string> Extensions = [".png", ".jpg", ".jpeg", ".webp"];

    private Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>변경: 폴더 없음(null, Resources 폴더 지정 전) 허용</summary>
    public BackgroundLibrary(string? folder) => Folder = folder;

    /// <summary>변경: 게임 Resources\BG (Resources 폴더를 바꾸면 SetFolder)</summary>
    public string? Folder { get; private set; }

    /// <summary>추가: 폴더 바꾸기 (그 뒤 Refresh)</summary>
    public void SetFolder(string? folder) => Folder = folder;

    /// <summary>BG 키 목록 (이름순)</summary>
    public IReadOnlyList<string> Keys { get; private set; } = [];

    /// <summary>키 → 파일 이름 (폴더 기준)</summary>
    public string? FileName(string key) => _files.GetValueOrDefault(key);

    /// <summary>폴더 다시 읽기. 같은 키의 파일이 여러 확장자로 있으면 Extensions 순서가 앞인 것</summary>
    public void Refresh()
    {
        // 변경: 게임 폴더를 만들지 않음 — 없거나 지정되지 않았으면 빈 목록
        if (Folder == null || !Directory.Exists(Folder))
        {
            _files = new(StringComparer.OrdinalIgnoreCase);
            Keys = [];
            return;
        }
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(Folder)
                     .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                     .OrderBy(f => Extensions.ToList().FindIndex(e => e.Equals(Path.GetExtension(f), StringComparison.OrdinalIgnoreCase))))
        {
            files.TryAdd(Path.GetFileNameWithoutExtension(path), Path.GetFileName(path));
        }
        _files = files;
        Keys = files.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
