using System.IO;

namespace SCResourceGrabber.Services;

/// <summary>
/// 다운로드 창 이름 자동 완성 후보. exe 옆 names.txt (도구 독립 규칙: 게임 데이터를 직접 읽지 않고 도구 안 복사본 사용).
/// 형식: 한 줄에 이름 하나, # 뒤는 설명(무시), 빈 줄 무시. 창을 열 때마다 다시 읽음 → 메모장으로 고친 뒤 창을 다시 열면 반영
/// (2026-10-28: 우선 RG 다운로드 창 전용. 좋으면 도구 공통 이름 목록으로 확장 검토)
/// </summary>
public static class NameCatalog
{
    public const string FileName = "names.txt";

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    public static List<string> Load()
    {
        var names = new List<string>();
        try
        {
            if (!File.Exists(FilePath)) return names;
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                int hash = raw.IndexOf('#');
                string line = (hash >= 0 ? raw[..hash] : raw).Trim();
                if (line.Length > 0 && !names.Contains(line, StringComparer.OrdinalIgnoreCase)) names.Add(line);
            }
        }
        catch { /* 목록 없이 동작 */ }
        return names;
    }
}
