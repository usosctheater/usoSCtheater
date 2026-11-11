using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using UsoSCTheater.Shared;   //[공유 코드 통합] LanguageInfo · LanguageRules

namespace SCTheaterEditor.ViewModels;

public enum TranslationField { MainTitle, SubTitle, Name, Text }

/// <summary>
/// 추가(번역 모드 3회차): 번역 창. 스크립트 표와 같은 막 문서(ActEditorViewModel.Document)를 편집 — 저장·되돌리기·자동 백업 공유.
/// - 행: 맨 위 제목·부제 1행 + TEXT 줄마다 1행(이름·대사). TEXT가 아닌 줄은 보이지 않음
/// - 번역 속성 = {기본 속성}_{언어 접미어} (Name_Ja, Text_Ja, mainTitle_Ja, subTitle_Ja)
/// - 진행률 = 원문이 있는 칸 중 번역이 채워진 칸 (게임 Tools > Localization > Check Translations와 같은 기준 — 한쪽을 바꾸면 함께)
/// - 막을 열 때(창을 열 때·막이 바뀔 때) 대소문자만 다른 번역 속성(Text_ja 등)을 TE 표기로 고침 (되돌리기 1단계) + 그 칸 빨간 글씨
/// - 다른 줄의 칸으로 옮기면 스크립트 표 선택은 항상 따라가고, 자동 무대 동기화가 켜져 있으면 멈춘 뒤 한 번 동기화(F5와 같음)
/// 창이 열려 있는 동안만 만들어 두고, 닫으면 Dispose (닫혀 있을 때 비용 없음)
/// </summary>
public sealed partial class TranslationViewModel : ObservableObject, IDisposable
{
    /// <summary>칸 이동이 멈춘 뒤 무대 동기화까지 (F5의 중복 방지 0.3초보다 길게 — 마지막 줄 동기화가 버려지지 않도록)</summary>
    public const int SyncDelayMs = 350;

    private readonly ActEditorViewModel _editor;
    private readonly CharacterData _characters;
    private readonly IReadOnlyList<LanguageInfo> _allLanguages;
    private readonly TranslationWindowSettings _settings;
    private readonly Action _saveSettings;
    private readonly Action _syncStage;
    private readonly IUiDispatcher _ui;
    private readonly HashSet<(ScriptLine? Line, string Attr)> _fixed = new();   // 대소문자를 고친 칸 (줄 — 제목은 null, 번역 속성 이름)
    private readonly List<ScriptLine> _watched = new();
    private ActDocument? _doc;
    private LanguageInfo? _language;
    private ScriptLine? _lastLine;
    private Timer? _syncTimer;
    private bool _disposed;

    public TranslationViewModel(ActEditorViewModel editor, GameData gameData, TranslationWindowSettings settings,
                                Action saveSettings, Action syncStage, IUiDispatcher ui,
                                IClaudeRunner? runner = null, TranslationGuideStore? guides = null,   // 추가(번역 모드 4회차): 가번역
                                Func<TimeSpan>? timeout = null, AppLog? log = null,
                                VoiceMatchData? voices = null, Func<string?>? exportFolder = null)   // 추가(번역 모드 5회차): VOICEVOX TXT 내보내기
    {
        _voices = voices;
        _exportFolder = exportFolder ?? (() => null);
        _runner = runner;
        _guides = guides;
        _timeout = timeout ?? (() => TimeSpan.FromMinutes(5));
        _log = log ?? AppLog.Shared;
        _editor = editor;
        _characters = gameData.Characters;
        _allLanguages = gameData.Languages;
        _settings = settings;
        _saveSettings = saveSettings;
        _syncStage = syncStage;
        _ui = ui;
        Languages = gameData.Languages.Where(l => !l.IsDefault).ToList();
        _language = Languages.FirstOrDefault(l => l.Code.Equals(settings.Language, StringComparison.OrdinalIgnoreCase)) ?? Languages.FirstOrDefault();
        Rows = [];
        ProgressText = "";
        StatusText = "";
        editor.PropertyChanged += OnEditorChanged;
        _gameData = gameData;
        gameData.Changed += Rebuild;   // [게임 데이터 다시 복사] → 화자 이름 다시 (언어 목록은 창을 다시 열 때)
        Attach(editor.Document);
    }

    private readonly GameData _gameData;
    private readonly IClaudeRunner? _runner;            // 추가(번역 모드 4회차)
    private readonly TranslationGuideStore? _guides;
    private readonly Func<TimeSpan> _timeout;
    private readonly AppLog _log;
    private CancellationTokenSource? _cts;
    private readonly VoiceMatchData? _voices;           // 추가(번역 모드 5회차)
    private readonly Func<string?> _exportFolder;

    /// <summary>번역할 언어 (기본 언어 제외, LanguageData 순서)</summary>
    public IReadOnlyList<LanguageInfo> Languages { get; }

    public LanguageInfo? SelectedLanguage
    {
        get => _language;
        set
        {
            if (value == null || value == _language) return;
            _language = value;
            _settings.Language = value.Code;
            _saveSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TargetHeader));
            OnPropertyChanged(nameof(CanPreTranslate));   // 추가(번역 모드 4회차)
            Rebuild();
        }
    }

    [ObservableProperty] public partial IReadOnlyList<TranslationRow> Rows { get; private set; }
    [ObservableProperty] public partial string ProgressText { get; private set; }
    [ObservableProperty] public partial string StatusText { get; private set; }

    public string DocumentTitle => _doc == null ? "열린 막 없음" : $"{_doc.Group} / {_doc.ActName}";
    public string TargetHeader => _language == null ? "번역 (번역할 언어 없음 — LanguageData 확인)" : $"번역 ({_language.NativeName})";

    /// <summary>번역 시 자동으로 무대 동기화 (설정에 저장, 번역 창에서만 바꿈)</summary>
    public bool AutoStageSync
    {
        get => _settings.AutoStageSync;
        set
        {
            if (value == _settings.AutoStageSync) return;
            _settings.AutoStageSync = value;
            _saveSettings();
            OnPropertyChanged();
            if (!value) StopSyncTimer();
        }
    }

    internal ActDocument? Document => _doc;
    internal LanguageInfo? Language => _language;
    internal CharacterData Characters => _characters;

    /// <summary>번역 속성 이름 (Text → Text_Ja). 언어가 없으면 null</summary>
    internal string? AttrName(string baseAttr) => _language == null ? null : LanguageRules.AttrName(baseAttr, _language);   //[공유 코드 통합] 수정

    // ------------------------------------------------------------------ 문서 연결

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActEditorViewModel.Document)) Attach(_editor.Document);   // 다른 막을 열면 따라감
    }

    private void Attach(ActDocument? doc)
    {
        if (_doc != null)
        {
            _doc.Lines.CollectionChanged -= OnLinesChanged;
            _doc.TitleChanged -= OnTitleChanged;
        }
        _doc = doc;
        _fixed.Clear();
        _lastLine = null;
        StatusText = "";
        if (doc != null)
        {
            doc.Lines.CollectionChanged += OnLinesChanged;
            doc.TitleChanged += OnTitleChanged;
            NormalizeNames();
        }
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(CanPreTranslate));   // 추가(번역 모드 4회차)
        Rebuild();
    }

    /// <summary>대소문자만 다른 번역 속성(Text_ja·MainTitle_ja 등)을 TE 표기({속성}_{Ja})로 — 모든 번역 언어, 되돌리기 1단계</summary>
    private void NormalizeNames()
    {
        var doc = _doc!;
        var renames = new List<(ScriptLine? Line, string Old, string New)>();
        int conflicts = 0;
        foreach (var lang in _allLanguages.Where(l => !l.IsDefault))
        {
            foreach (var b in new[] { ActXmlFormat.MainTitleAttr, ActXmlFormat.SubTitleAttr })
            {
                string target = LanguageRules.AttrName(b, lang);   //[공유 코드 통합] 수정
                if (doc.FindRootAttributeIgnoreCase(target) is { } found && found != target)
                {
                    if (doc.GetRootAttribute(target) != null) conflicts++;
                    else { renames.Add((null, found, target)); _fixed.Add((null, target)); }
                }
            }
            foreach (var line in doc.Lines.Where(IsText))
                foreach (var b in new[] { "Name", "Text" })
                {
                    string target = LanguageRules.AttrName(b, lang);   //[공유 코드 통합] 수정
                    var found = line.Attributes.Select(a => a.Key)
                                    .FirstOrDefault(k => k != target && string.Equals(k, target, StringComparison.OrdinalIgnoreCase));
                    if (found == null) continue;
                    if (line.Find(target) != null) conflicts++;
                    else { renames.Add((line, found, target)); _fixed.Add((line, target)); }
                }
        }
        int n = renames.Count > 0 ? doc.RenameAttributes(renames) : 0;
        var notes = new List<string>();
        if (n > 0) notes.Add($"⚠ 대소문자가 다른 번역 속성 {n}곳을 TE 표기(예: Text_Ja)로 고침 (빨간 칸, Ctrl+Z로 되돌리기)");
        if (conflicts > 0) notes.Add($"⚠ 대소문자만 다른 번역 속성이 따로 있는 곳 {conflicts}곳 — 스크립트 표 '기타' 칸 확인");
        StatusText = string.Join(" · ", notes);
    }

    private static bool IsText(ScriptLine l) => l.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase);

    private void Rebuild()
    {
        foreach (var l in _watched) l.Changed -= OnLineChanged;
        _watched.Clear();
        if (_doc == null || _language == null)
        {
            Rows = [];
            UpdateProgress();
            return;
        }
        var rows = new List<TranslationRow> { new(this, null, 0) };
        for (int i = 0; i < _doc.Lines.Count; i++)
        {
            var line = _doc.Lines[i];
            line.Changed += OnLineChanged;   // TEXT가 아닌 줄도 — 종류가 TEXT로 바뀌면 행이 생겨야 함
            _watched.Add(line);
            if (IsText(line)) rows.Add(new TranslationRow(this, line, i + 1));
        }
        Rows = rows;
        UpdateProgress();
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();   // 줄 추가·삭제·이동 (스크립트 표)

    private void OnTitleChanged()
    {
        if (Rows.Count > 0 && Rows[0].IsTitle) Rows[0].Refresh();
        UpdateProgress();
    }

    private void OnLineChanged(ScriptLine line)
    {
        var row = Rows.FirstOrDefault(r => r.Line == line);
        if ((row != null) != IsText(line)) { Rebuild(); return; }   // 종류가 TEXT에서/로 바뀜
        row?.Refresh();
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (_language == null || _doc == null) { ProgressText = ""; return; }
        int total = 0, done = 0;
        foreach (var c in Rows.SelectMany(r => r.Cells))
        {
            if (!c.IsRequired) continue;
            total++;
            if (c.IsDone) done++;
        }
        ProgressText = $"{_language.NativeName} {done}/{total}";
    }

    // ------------------------------------------------------------------ 칸 읽기·쓰기

    internal string GetValue(TranslationCell cell)
    {
        if (_doc == null || AttrName(cell.BaseAttr) is not { } attr) return "";
        return (cell.Row.Line == null ? _doc.GetRootAttribute(attr) : cell.Row.Line.Find(attr)) ?? "";
    }

    /// <summary>번역 칸 변경 (빈 값 = 속성 삭제) — 되돌리기 1단계</summary>
    internal void SetValue(TranslationCell cell, string? value)
    {
        if (_doc == null || AttrName(cell.BaseAttr) is not { } attr) return;
        if (cell.Row.Line == null) _doc.SetRootAttribute(attr, value);
        else _doc.SetAttribute(cell.Row.Line, attr, value);
    }

    internal bool IsFixed(TranslationCell cell) =>
        AttrName(cell.BaseAttr) is { } attr && _fixed.Contains((cell.Row.Line, attr));

    // ------------------------------------------------------------------ 줄 선택 → 스크립트 표 · 무대

    /// <summary>칸에 들어옴 (화면이 알림). 다른 TEXT 줄이면 스크립트 표 선택, 자동 무대 동기화가 켜져 있으면 멈춘 뒤 한 번</summary>
    public void OnCellFocused(TranslationCell cell)
    {
        if (cell.Row.Line is not { } line || line == _lastLine) return;
        _lastLine = line;
        _editor.SelectLine(line);
        if (!AutoStageSync) return;
        StopSyncTimer();
        _syncTimer = new Timer(_ => _ui.Post(SyncNow), null, SyncDelayMs, Timeout.Infinite);
    }

    private void SyncNow()
    {
        if (_disposed || !AutoStageSync) return;
        _syncStage();
    }

    private void StopSyncTimer()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
    }

    // ------------------------------------------------------------------ 추가(번역 모드 4회차): 가번역 (Claude Code)

    /// <summary>가번역 실행 중 (버튼 바꿈 — [가번역하기] ↔ [취소])</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreTranslate))]
    public partial bool IsTranslating { get; private set; }

    public bool CanPreTranslate => !IsTranslating && _runner != null && _doc != null && _language != null;

    /// <summary>
    /// [가번역하기]: 이 막에서 원문이 있고 번역이 빈 칸을 한 번에 Claude Code에 보냄 (문맥 = 막 전체, 지침 = TranslationGuide_{언어}.md, 없으면 지침 없이).
    /// 답을 받으면 그때도 비어 있는 칸에만 바로 넣음 (되돌리기 1단계). 실행 중에도 다른 작업 가능, 그 사이 막·언어가 바뀌면 결과를 버림
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async Task PreTranslate()
    {
        if (!CanPreTranslate) return;
        var doc = _doc!;
        var lang = _language!;
        var targets = PreTranslation.Targets(doc, lang);
        if (targets.Count == 0) { StatusText = $"{lang.NativeName}: 가번역할 빈 칸이 없습니다"; return; }
        string? guide = null;
        try { guide = _guides?.Load(lang.Code); }
        catch (Exception ex) { _log.Error("번역 지침 읽기 실패 — 지침 없이 요청", ex); }
        string system = PreTranslation.SystemPrompt(lang, guide);
        string input = PreTranslation.Input(doc, lang, _characters, targets);

        IsTranslating = true;
        _cts = new CancellationTokenSource();
        StatusText = $"가번역 중… {targets.Count}칸{(guide == null ? " (지침 파일 없음 — 지침 없이 요청)" : "")} — 끝날 때까지 다른 작업을 해도 됨";
        ClaudeRunResult r;
        try { r = await _runner!.RunAsync(system, input, _timeout(), _cts.Token); }
        finally
        {
            IsTranslating = false;
            _cts.Dispose();
            _cts = null;
        }
        if (_disposed) return;
        string cost = r.CostUsd is { } c ? $", 추정 {c:0.####}달러 — 구독 로그인이면 청구 없음" : "";
        if (!r.Ok)
        {
            StatusText = $"⚠ 가번역 실패: {r.Error}";
            _log.Warn($"가번역 실패 ({r.Elapsed.TotalSeconds:0.#}초): {r.Error}");
            return;
        }
        var items = PreTranslation.ParseAnswer(r.Text);
        if (items == null)
        {
            StatusText = "⚠ 가번역 답변을 읽지 못했습니다 (로그에 답변 앞부분)";
            _log.Warn($"가번역 답변 형식 오류: {(r.Text is { } t && t.Length > 1000 ? t[..1000] + "…" : r.Text)}");
            return;
        }
        if (_doc != doc || _language != lang)
        {
            StatusText = "가번역 결과를 버렸습니다 — 그 사이 막이나 언어가 바뀜";
            return;
        }

        var byId = targets.ToDictionary(t => t.Id);
        var values = new List<(ScriptLine? Line, string Attr, string? Value)>();
        int typed = 0;
        foreach (var item in items)
        {
            if (!byId.Remove(item.Id, out var t) || item.Value.Trim().Length == 0) continue;   // 요청하지 않은 칸·같은 칸 두 번·빈 답은 무시
            string attr = LanguageRules.AttrName(t.BaseAttr, lang);   //[공유 코드 통합] 수정
            if (t.Line != null && !doc.Lines.Contains(t.Line)) continue;                         // 그 사이 지운 줄
            string? now = t.Line == null ? doc.GetRootAttribute(attr) : t.Line.Find(attr);
            if (!string.IsNullOrWhiteSpace(now)) { typed++; continue; }                         // 그 사이 직접 입력한 칸은 그대로
            values.Add((t.Line, attr, item.Value));
        }
        int n = doc.SetValuesMany(values, $"가번역 {values.Count}칸");
        var notes = new List<string> { $"가번역 {n}칸 넣음 (Ctrl+Z로 한 번에 되돌리기)" };
        int missing = byId.Count;
        if (missing > 0) notes.Add($"답이 없던 칸 {missing}");
        if (typed > 0) notes.Add($"그 사이 입력한 칸 {typed}개는 그대로");
        notes.Add($"{r.Elapsed.TotalSeconds:0}초");
        StatusText = string.Join(" · ", notes);
        _log.Info($"가번역 {doc.Group}/{doc.ActName} [{lang.Code}]: 요청 {targets.Count}칸 · 넣음 {n} · 답 없음 {missing} · 입력돼 있어 건너뜀 {typed} · {r.Elapsed.TotalSeconds:0.#}초{cost}");
    }

    /// <summary>[취소]: 실행 중인 Claude Code를 끝냄 (아무것도 넣지 않음)</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void CancelPreTranslate() => _cts?.Cancel();

    // ------------------------------------------------------------------ 추가(번역 모드 5회차): VOICEVOX TXT 내보내기

    /// <summary>
    /// [TXT로 내보내기]: 이 막의 고른 언어 대사를 VOICEVOX 「テキスト読み込み」용 TXT로 (설정 탭 '보이스'의 저장 폴더, {막}_{언어}.txt — 같으면 덮어씀).
    /// 화자 = VoiceMatch.xml 매칭. 저장하지 않은 편집도 반영 (파일이 아니라 편집 중인 문서). 이용 제한 캐릭터는 상태 줄에 ⚠
    /// </summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ExportVoiceText()
    {
        if (_doc == null || _language == null) return;
        var voices = _voices ?? VoiceMatchData.Empty();
        var r = VoiceExport.BuildScript(_doc, _language, voices);
        if (r.Lines == 0) { StatusText = $"{_language.NativeName}: 내보낼 대사가 없습니다 (번역이 빈 줄은 넣지 않음)"; return; }
        try
        {
            string path = VoiceExport.WriteFile(_exportFolder(), VoiceExport.ScriptFileName(_doc, _language), r.Text);
            var notes = new List<string> { $"TXT 내보내기: 대사 {r.Lines}줄 → {path}" };
            if (voices.Problem.Length > 0) notes.Add($"⚠ {voices.Problem} — 화자 전환 없이 내보냄");
            notes.AddRange(r.Warnings.Select(w => "⚠ " + w));
            StatusText = string.Join(" · ", notes);
            _log.Info($"VOICEVOX TXT 내보내기: {path} ({r.Lines}줄)");
        }
        catch (Exception ex)
        {
            _log.Error("VOICEVOX TXT 내보내기 실패", ex);
            StatusText = $"⚠ TXT 내보내기 실패: {ex.Message}";
        }
    }

    /// <summary>[지침 편집] 창의 ViewModel (지침 저장소·언어가 없으면 null)</summary>
    public GuideEditorViewModel? CreateGuideEditor() =>
        _guides != null && _language != null ? new GuideEditorViewModel(_guides, _language, _log) : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();   // 추가(번역 모드 4회차): 창을 닫으면 실행 중인 가번역도 끝냄
        StopSyncTimer();
        _editor.PropertyChanged -= OnEditorChanged;
        _gameData.Changed -= Rebuild;
        foreach (var l in _watched) l.Changed -= OnLineChanged;
        _watched.Clear();
        if (_doc != null)
        {
            _doc.Lines.CollectionChanged -= OnLinesChanged;
            _doc.TitleChanged -= OnTitleChanged;
        }
    }
}

/// <summary>번역 창 1행: 제목 행(Line = null — 제목·부제) 또는 TEXT 줄 1개(이름·대사)</summary>
public sealed class TranslationRow : ObservableObject
{
    internal TranslationRow(TranslationViewModel owner, ScriptLine? line, int number)
    {
        Owner = owner;
        Line = line;
        Number = number;
        First = new TranslationCell(this, line == null ? TranslationField.MainTitle : TranslationField.Name);
        Second = new TranslationCell(this, line == null ? TranslationField.SubTitle : TranslationField.Text);
        Cells = [First, Second];
    }

    internal TranslationViewModel Owner { get; }
    public ScriptLine? Line { get; }
    public int Number { get; }
    public bool IsTitle => Line == null;
    public TranslationCell First { get; }
    public TranslationCell Second { get; }
    public IReadOnlyList<TranslationCell> Cells { get; }

    /// <summary>왼쪽 번호 칸</summary>
    public string NumberText => IsTitle ? "제목" : $"#{Number}";

    /// <summary>왼쪽 위: 제목 행 = 막 제목, TEXT 줄 = 화자 (직접 적은 이름 → 화자(CharId)의 한국어 이름 → 이름 없음)</summary>
    public string SourceTop
    {
        get
        {
            if (Line == null) return Owner.Document?.MainTitle ?? "";
            string name = Line.Get("Name").Trim();
            string ids = Line.Get("CharId").Trim();
            if (name.Length > 0) return ids.Length > 0 ? $"{name} ({ids})" : name;
            return Owner.Characters.SpeakerNames(ids) ?? "(이름 없음)";
        }
    }

    /// <summary>왼쪽 아래: 제목 행 = 부제, TEXT 줄 = 한국어 대사 (\n은 글자 그대로)</summary>
    public string SourceBottom => Line == null ? Owner.Document?.SubTitle ?? "" : Line.Get("Text");

    internal void Refresh()
    {
        OnPropertyChanged(nameof(SourceTop));
        OnPropertyChanged(nameof(SourceBottom));
        First.Refresh();
        Second.Refresh();
    }
}

/// <summary>번역 칸 1개 (제목·부제·이름·대사). 값을 고치면 되돌리기 1단계</summary>
public sealed class TranslationCell : ObservableObject
{
    internal TranslationCell(TranslationRow row, TranslationField field)
    {
        Row = row;
        Field = field;
    }

    public TranslationRow Row { get; }
    public TranslationField Field { get; }

    /// <summary>기본(한국어) 속성 이름</summary>
    public string BaseAttr => Field switch
    {
        TranslationField.MainTitle => ActXmlFormat.MainTitleAttr,
        TranslationField.SubTitle => ActXmlFormat.SubTitleAttr,
        TranslationField.Name => "Name",
        _ => "Text",
    };

    public string Label => Field switch
    {
        TranslationField.MainTitle => "제목",
        TranslationField.SubTitle => "부제",
        TranslationField.Name => "이름",
        _ => "대사",
    };

    /// <summary>원문(한국어) 값</summary>
    public string Source => Field switch
    {
        TranslationField.MainTitle => Row.Owner.Document?.MainTitle ?? "",
        TranslationField.SubTitle => Row.Owner.Document?.SubTitle ?? "",
        _ => Row.Line?.Get(BaseAttr) ?? "",
    };

    /// <summary>번역 값 (빈 값 = 속성 삭제)</summary>
    public string Value
    {
        get => Row.Owner.GetValue(this);
        set => Row.Owner.SetValue(this, value);
    }

    /// <summary>번역이 필요한 칸 = 원문이 있음 (CharId만 있는 이름은 DB에서 나오므로 아님)</summary>
    public bool IsRequired => Source.Trim().Length > 0;
    public bool IsDone => Value.Trim().Length > 0;

    /// <summary>
    /// 빈 칸의 흐린 글씨 = 비워 두면 게임에 나오는 것. 이름: 직접 적은 한국어 이름 / 화자(CharId)의 그 언어 DB 이름.
    /// 제목·대사는 없음 (왼쪽 원문을 봄)
    /// </summary>
    public string Hint
    {
        get
        {
            if (Field != TranslationField.Name || Row.Line is not { } line) return "";
            string name = line.Get("Name").Trim();
            if (name.Length > 0) return name;
            return Row.Owner.Language is { } lang ? Row.Owner.Characters.SpeakerNames(line.Get("CharId"), lang) ?? "" : "";
        }
    }

    /// <summary>대소문자가 다른 표기를 TE 표기로 고친 칸</summary>
    public bool IsFixed => Row.Owner.IsFixed(this);

    /// <summary>추가(번역 모드 4회차): 번역이 있는데 원문과 줄바꿈(\n) 개수가 다름</summary>
    public bool NewlineMismatch => IsDone && PreTranslation.NewlineCount(Value) != PreTranslation.NewlineCount(Source);

    /// <summary>빨간 글씨 = 대소문자 고친 칸 또는 \n 개수가 다른 칸 (변경: 번역 모드 4회차)</summary>
    public bool Warning => IsFixed || NewlineMismatch;

    public string ToolTip
    {
        get
        {
            string attr = Row.Owner.AttrName(BaseAttr) ?? BaseAttr;
            string tip = $"{Label} ({attr})";
            if (NewlineMismatch)   // 추가(번역 모드 4회차)
                tip += $"\n⚠ 줄바꿈(\\n) 개수가 원문과 다릅니다 — 원문 {PreTranslation.NewlineCount(Source)}개, 번역 {PreTranslation.NewlineCount(Value)}개";
            if (IsFixed) tip += "\n⚠ 대소문자가 다른 표기(예: _ja)를 TE 표기로 고쳤습니다 — 되돌리려면 Ctrl+Z";
            else if (NewlineMismatch) { }
            else if (!IsRequired) tip += Field == TranslationField.Name && Hint.Length > 0
                ? "\n비워 두면 화자의 DB 이름이 나옵니다 (흐린 글씨). 다르게 보여야 할 때만 입력"
                : "\n원문이 비어 있어 번역하지 않아도 됩니다";
            return tip;
        }
    }

    internal void Refresh() => OnPropertyChanged(string.Empty);
}
