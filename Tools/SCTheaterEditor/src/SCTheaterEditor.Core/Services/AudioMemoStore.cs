using System.IO;
using System.Xml.Linq;

namespace SCTheaterEditor.Services;

/// <summary>
/// 추가(6회차): 오디오 메모 — exe 옆 AudioMemo.xml (스크립터마다 다르게 쓰는 개인 메모, git에서 제외).
/// 형식: &lt;AudioMemos&gt;&lt;Memo Kind="BGM" Key="BGM_121" Text="경쾌한 일상"/&gt;…, 보이스 Key = "IL/s001001".
/// path가 null이면 저장하지 않음 (테스트). 읽기 실패는 빈 메모로 시작하고 LoadNotice에 남김
/// </summary>
public sealed class AudioMemoStore
{
    public const string FileName = "AudioMemo.xml";

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, FileName);

    private readonly string? _path;
    private readonly Dictionary<(AudioKind Kind, string Key), string> _memos = new(new KeyComparer());

    public AudioMemoStore(string? path)
    {
        _path = path;
        if (path == null || !File.Exists(path)) return;
        try
        {
            foreach (var e in XDocument.Load(path).Root?.Elements("Memo") ?? [])
            {
                if (!Enum.TryParse<AudioKind>((string?)e.Attribute("Kind"), ignoreCase: true, out var kind)) continue;
                string key = ((string?)e.Attribute("Key") ?? "").Trim();
                string text = ((string?)e.Attribute("Text") ?? "").Trim();
                if (key.Length > 0 && text.Length > 0) _memos[(kind, key)] = text;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            LoadNotice = $"오디오 메모를 읽지 못해 빈 메모로 시작합니다: {path} — {ex.Message}";
        }
    }

    /// <summary>읽기 실패 안내 (없으면 null)</summary>
    public string? LoadNotice { get; }

    public int Count => _memos.Count;

    public string Get(AudioKind kind, string key) => _memos.GetValueOrDefault((kind, key)) ?? "";

    /// <summary>메모 바꾸기 (빈 글자 = 지움) → 바로 저장. 바뀌었으면 true</summary>
    public bool Set(AudioKind kind, string key, string? text)
    {
        text = text?.Trim() ?? "";
        if (Get(kind, key) == text) return false;
        if (text.Length == 0) _memos.Remove((kind, key));
        else _memos[(kind, key)] = text;
        Save();
        return true;
    }

    private void Save()
    {
        if (_path == null) return;
        var root = new XElement("AudioMemos",
            _memos.OrderBy(p => p.Key.Kind).ThenBy(p => p.Key.Key, NaturalComparer.Instance)
                  .Select(p => new XElement("Memo", new XAttribute("Kind", p.Key.Kind), new XAttribute("Key", p.Key.Key), new XAttribute("Text", p.Value))));
        SafeFile.WriteAllText(_path, new XDocument(new XDeclaration("1.0", "utf-8", null), root).Declaration + "\n" + root, keepBackup: true);
    }

    private sealed class KeyComparer : IEqualityComparer<(AudioKind Kind, string Key)>
    {
        public bool Equals((AudioKind Kind, string Key) a, (AudioKind Kind, string Key) b) =>
            a.Kind == b.Kind && string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((AudioKind Kind, string Key) k) => HashCode.Combine(k.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(k.Key));
    }
}
