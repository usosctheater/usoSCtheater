using System.IO;
using UsoSCTheater.Shared;   //[스테이지 레이아웃] StageTable   //[공유 코드 통합] LanguageInfo · LanguageRules

namespace SCTheaterEditor.Services;

//[공유 코드 통합] 삭제: record LanguageInfo → 공유 LanguageInfo (Code·NativeName·IsDefault·ListSeparator·Suffix 그대로, 생성자 인자 순서도 같음)

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
    public CharacterData Characters { get; } = new();   //[공유 코드 통합] 수정: 빈 데이터 생성자
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
        //[공유 코드 통합] 수정: 언어 → 캐릭터(기본 언어 필요) 순서, 해석은 공유 (언어 기본 목록은 TE용 ko·ja — 번역 창에 대상 언어가 있어야 함)
        Languages = Parse(LanguageFile, (xml, w) => LanguageRules.Parse(xml, LanguageFallback, w));
        Characters.ReplaceWith(Parse(CharacterFile, (xml, w) => CharacterData.Parse(xml, Languages.First(l => l.IsDefault), w)));
        Lips.ReplaceWith(Read(LipFile, LipRules.Parse) ?? new LipRules());
        Stage = Parse(StageFile, StageTable.Parse);   //[공유 코드 통합] 수정: ReadStage → Parse
    }

    //[공유 코드 통합] 추가 (ReadStage를 일반화): 복사본 글자 → 공유 해석(문제는 warnings) → 로그. 파일이 없으면 xml = null (해석이 경고)
    private T Parse<T>(string name, Func<string?, ICollection<string>, T> parse)
    {
        string path = CopyPath(name);
        string? xml = null;
        if (File.Exists(path))
        {
            try { xml = File.ReadAllText(path); }
            catch (Exception ex) { _log.Error($"게임 데이터 읽기 실패: {path}", ex); }
        }
        var warnings = new List<string>();
        T result = parse(xml, warnings);
        foreach (var w in warnings) _log.Warn($"게임 데이터 {name}: {w}");
        return result;
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

    //[공유 코드 통합] 삭제: ParseLanguages → 공유 LanguageRules.Parse (중복·빈 코드·Default 여럿 경고, NativeName 없으면 코드 — 게임과 같음)
}
