using System.IO;
using System.Xml.Linq;
using UsoSCTheater.Shared;   //[스테이지 레이아웃] StageTable

namespace SCTheaterEditor.Services;

/// <summary>언어 1개 (게임 LanguageData.xml의 Language 줄). 변경(번역 모드 3회차): ListSeparator(여러 이름 구분자) 추가</summary>
public sealed record LanguageInfo(string Code, string NativeName, bool IsDefault, string ListSeparator = ", ")
{
    /// <summary>번역 속성 접미어 표기 (ja → Ja) — 도구가 쓰는 속성 이름 {속성}_{Ja}</summary>
    public string Suffix => Code.Length == 0 ? "" : char.ToUpperInvariant(Code[0]) + Code[1..].ToLowerInvariant();
}

public enum GameDataState { Same, Different, CopyOnly, GameOnly, Missing }

/// <summary>복사본 1개의 상태 (설정 탭 '게임 데이터' 구역)</summary>
public sealed record GameDataFileState(string Name, GameDataState State)
{
    public bool IsProblem => State != GameDataState.Same;

    public string Text => State switch
    {
        GameDataState.Same => "게임과 같음",
        GameDataState.Different => "게임과 다름 — [게임 데이터 다시 복사] 필요",
        GameDataState.CopyOnly => "게임 원본 없음 — Resources 폴더 확인 (지금은 복사본으로 동작)",
        GameDataState.GameOnly => "복사본 없음 — [게임 데이터 다시 복사] 필요",
        _ => "복사본·게임 원본 모두 없음 — 이 데이터 없이 동작",
    };
}

/// <summary>
/// 게임 데이터 복사본 (exe 옆 Data\) — 도구 독립 규칙: TE가 읽는 데이터 문서는 게임 원본이 아니라 복사본.
/// - Refresh (시작·Resources 폴더 변경): 복사본이 없고 게임 원본이 있으면 복사 → 읽기 → 게임 원본과 바이트 비교
/// - CopyAllFromGame ([게임 데이터 다시 복사]): 게임 원본으로 덮어쓰고 다시 읽음
/// - Characters·Lips는 처음 만든 객체 하나를 계속 쓰고 안쪽만 바꿈 → 받아 둔 ViewModel은 고칠 필요 없음
/// - 복사본이 없거나 깨지면 로그 경고 + 그 데이터 없이 동작 (언어는 ko·ja 기본 목록)
/// </summary>
public sealed class GameData
{
    public const string CharacterFile = "CharacterData.xml", LipFile = "DefaultLipData.xml", LanguageFile = "LanguageData.xml",
                        StageFile = "StageData.xml";   //[스테이지 레이아웃] 추가: 4번째 복사본
    public static readonly IReadOnlyList<string> Files = [CharacterFile, LipFile, LanguageFile, StageFile];   //[스테이지 레이아웃] StageFile 추가

    //[스테이지 레이아웃] 추가: 복사본이 없을 때의 빈 표 (코드 기본값 없음 — 숫자 원본은 StageData.xml 하나)
    public static readonly StageTable EmptyStage = new([], null);
    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "Data");

    private static readonly IReadOnlyList<LanguageInfo> LanguageFallback = [new("ko", "한국어", true), new("ja", "日本語", false, "、")];

    private readonly Func<string?> _resources;
    private readonly AppLog _log;
    private bool _loaded;

    /// <param name="resources">게임 Resources 폴더 (설정 Editor.GameResourcesFolder — 바뀔 수 있어 함수로)</param>
    public GameData(string folder, Func<string?> resources, AppLog log)
    {
        Folder = folder;
        _resources = resources;
        _log = log;
    }

    public string Folder { get; }
    public CharacterData Characters { get; } = new([]);
    public LipRules Lips { get; } = new();
    public IReadOnlyList<LanguageInfo> Languages { get; private set; } = LanguageFallback;
    public IReadOnlyList<GameDataFileState> States { get; private set; } = [];

    /// <summary>[스테이지 레이아웃] 추가: 무대 배치 표 (StageData.xml 복사본). 바뀌면 Changed</summary>
    public StageTable Stage { get; private set; } = EmptyStage;
    public bool HasProblem => States.Any(s => s.IsProblem);

    /// <summary>읽기·비교 결과가 바뀜 (화면 스레드에서만 부름)</summary>
    public event Action? Changed;

    public string CopyPath(string name) => Path.Combine(Folder, name);
    public string? GamePath(string name) => _resources() is { Length: > 0 } r ? Path.Combine(r, "Data", name) : null;

    /// <summary>시작·Resources 폴더 변경 때: 없는 복사본만 채우고 비교</summary>
    public void Refresh()
    {
        bool copied = false;
        foreach (var name in Files)
            if (!File.Exists(CopyPath(name)) && GamePath(name) is { } g && File.Exists(g)) copied |= TryCopy(g, name);
        if (copied || !_loaded) Load();
        Check();
        Changed?.Invoke();
    }

    /// <summary>[게임 데이터 다시 복사]: 게임 원본이 있는 파일을 모두 덮어쓰고 다시 읽음. 복사한 파일 수</summary>
    public int CopyAllFromGame()
    {
        int count = 0;
        foreach (var name in Files)
            if (GamePath(name) is { } g && File.Exists(g) && TryCopy(g, name)) count++;
        Load();
        Check();
        Changed?.Invoke();
        return count;
    }

    private bool TryCopy(string gamePath, string name)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            SafeFile.WriteAllBytes(CopyPath(name), File.ReadAllBytes(gamePath));
            _log.Info($"게임 데이터 복사: {gamePath} → {CopyPath(name)}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"게임 데이터 복사 실패: {name}", ex);
            return false;
        }
    }

    private void Load()
    {
        _loaded = true;
        Characters.ReplaceWith(Read(CharacterFile, CharacterData.Parse) ?? new CharacterData([]));
        Lips.ReplaceWith(Read(LipFile, LipRules.Parse) ?? new LipRules());
        Languages = Read(LanguageFile, ParseLanguages) is { Count: > 0 } list ? list : LanguageFallback;
        Stage = ReadStage();   //[스테이지 레이아웃] 추가
    }

    //[스테이지 레이아웃] 추가: 해석 문제는 공유 StageTable이 문구를 만들고 여기서 로그로
    private StageTable ReadStage()
    {
        string path = CopyPath(StageFile);
        string? xml = null;
        if (File.Exists(path))
        {
            try { xml = File.ReadAllText(path); }
            catch (Exception ex) { _log.Error($"게임 데이터 읽기 실패: {path}", ex); }
        }
        var warnings = new List<string>();
        var table = StageTable.Parse(xml, warnings);
        foreach (var w in warnings) _log.Warn($"게임 데이터 {StageFile}: {w}");
        return table;
    }

    private T? Read<T>(string name, Func<string, T> parse) where T : class
    {
        string path = CopyPath(name);
        if (!File.Exists(path))
        {
            _log.Warn($"게임 데이터 복사본 없음: {path} — 이 데이터 없이 동작");
            return null;
        }
        try { return parse(File.ReadAllText(path)); }
        catch (Exception ex)
        {
            _log.Error($"게임 데이터 읽기 실패: {path} — 이 데이터 없이 동작", ex);
            return null;
        }
    }

    private void Check()
    {
        States = Files.Select(name => new GameDataFileState(name, Compare(name))).ToList();
        foreach (var s in States.Where(s => s.IsProblem)) _log.Warn($"게임 데이터 {s.Name}: {s.Text}");
    }

    private GameDataState Compare(string name)
    {
        string copy = CopyPath(name);
        string? game = GamePath(name);
        bool hasCopy = File.Exists(copy), hasGame = game != null && File.Exists(game);
        try
        {
            return (hasCopy, hasGame) switch
            {
                (false, false) => GameDataState.Missing,
                (true, false) => GameDataState.CopyOnly,
                (false, true) => GameDataState.GameOnly,
                _ => File.ReadAllBytes(copy).AsSpan().SequenceEqual(File.ReadAllBytes(game!)) ? GameDataState.Same : GameDataState.Different,
            };
        }
        catch (Exception ex)
        {
            _log.Error($"게임 데이터 비교 실패: {name}", ex);
            return GameDataState.Different;
        }
    }

    /// <summary>LanguageData.xml → 언어 목록. 기본 언어 = Default="true" 줄, 없으면 첫 줄 (게임 Localization과 같은 규칙)</summary>
    public static IReadOnlyList<LanguageInfo> ParseLanguages(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException("LanguageData.xml 루트가 없습니다");
        var items = root.Elements("Language")
            .Select(e => (Code: ((string?)e.Attribute("Code") ?? "").Trim().ToLowerInvariant(),
                          Native: (string?)e.Attribute("NativeName") ?? "",
                          Default: string.Equals((string?)e.Attribute("Default"), "true", StringComparison.OrdinalIgnoreCase),
                          Separator: (string?)e.Attribute("ListSeparator") ?? ", "))   // 추가(번역 모드 3회차)
            .Where(x => x.Code.Length > 0)
            .GroupBy(x => x.Code).Select(g => g.First())
            .ToList();
        int def = Math.Max(0, items.FindIndex(x => x.Default));
        return items.Select((x, i) => new LanguageInfo(x.Code, x.Native, i == def, x.Separator)).ToList();
    }
}
