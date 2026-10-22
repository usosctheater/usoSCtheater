using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;   // 추가: 설정 변경 알림
using SCTheaterEditor.Script;
using SCTheaterEditor.Services;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 스크립트 표 (4-1): 막 열기·저장, 라인 편집·추가·삭제·이동, 되돌리기/다시 하기.
/// - 막 목록: 설정한 시나리오 폴더(게임 Assets\Resources\Scenario)를 읽어 {그룹} / {막} 목록 + [파일 열기]
/// - 저장: 열었던 경로에 저장. 저장 직전 파일은 TE 백업 폴더(exe 옆 Backups)에 막별 최근 N개
/// - 연 뒤 다른 프로그램이 파일을 바꿨으면 덮어쓸지 물음, 저장 안 된 변경이 있으면 다른 막을 열거나 끌 때 물음
/// - 재실행 시 마지막 막·라인을 다시 엶
/// 4-2에서 행 선택 → 무대 누적 상태, 라인에 적용(Ctrl+Enter)이 붙는다.
/// </summary>
public sealed partial class ActEditorViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SpineLibrary _library;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _ui;
    private readonly Action _saveSettings;
    private readonly AppLog _log;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<ScriptLine, ScriptRow> _rowOf = new();
    private readonly CharacterData _characters;
    private readonly GameSpineExporter _exporter;   // 추가: 이 막의 Spine 게임에 내보내기

    private bool _switching;                                  // 막 목록 선택을 코드에서 맞추는 중 (다시 열지 않음)
    private IReadOnlyList<ScriptRow> _selection = [];
    private HashSet<string> _groupNames = new(StringComparer.OrdinalIgnoreCase);

    // 추가: 자동 백업 상태 (막을 열 때마다 초기화)
    private long _backupEditMark;          // 마지막 백업 때의 Undo.EditCount
    private DateTime _lastBackupAt;        // 마지막 백업 시각 (아직 없으면 막을 연 시각)
    private byte[]? _lastBackupBytes;      // 마지막 백업(또는 연 시점)의 내용 — 같으면 쓰지 않음
    private bool _originalBackedUp;        // 이 막을 연 뒤 저장 전 원본을 백업했는지
    private System.Threading.Timer? _backupTimer;
    private readonly IMessenger _messenger;   // 추가: 설정 변경 알림

    public ActEditorViewModel(AppSettings settings, SpineLibrary library, IDialogService dialogs, IUiDispatcher ui,
                              Action saveSettings, AppLog log, string? backupFolder = null, Func<DateTime>? now = null,
                              LineTypeCatalog? types = null, CharacterData? characters = null,
                              IMessenger? messenger = null)   // 변경: messenger 추가 (생략하면 이 인스턴스 전용)
    {
        _messenger = messenger ?? new StrongReferenceMessenger();
        // 추가: 시나리오 폴더가 바뀌면 (어디서 바꿨든) 막 목록 다시 읽기
        _messenger.Register<ActEditorViewModel, SettingsChangedMessage>(this, static (r, m) => r.OnSettingsChanged(m));
        _characters = characters ?? CharacterData.LoadEmbedded();   // 추가(4-2): 라인에 적용할 때 이름 칸
        // 추가: 종류별 팔레트 (LineTypes.xml) + 저장된 칸 폭
        Types = types ?? LineTypeCatalog.LoadEmbedded();
        Types.ApplyWidths(settings.Editor.FieldWidths);
        _settings = settings;
        _library = library;
        _dialogs = dialogs;
        _ui = ui;
        _saveSettings = saveSettings;
        _log = log;
        _exporter = new GameSpineExporter(library);   // 추가
        _now = now ?? (() => DateTime.Now);
        BackupFolder = backupFolder ?? ActFiles.DefaultBackupFolder;
        ActEntries = [];
        Rows = new();
        StatusText = "[폴더 지정]으로 시나리오 폴더를 고르거나 [파일 열기]로 막 XML을 엽니다";
        library.Changed += () => _ui.Post(RefreshCgWarnings);   // 다운로드로 세트가 늘면 경고 다시 계산
    }

    /// <summary>저장 전 백업 폴더 (기본: exe 옆 Backups)</summary>
    public string BackupFolder { get; }

    /// <summary>추가: 종류별 속성 팔레트</summary>
    public LineTypeCatalog Types { get; }

    /// <summary>추가: 제목 줄에 보일 팔레트 = 선택한 첫 줄의 종류 (선택이 없으면 TEXT, 모르는 종류면 null)</summary>
    public LineTypeDef? HeaderType => _selection.Count > 0 ? _selection.MinBy(r => r.Number)!.Def : Types.Find("TEXT");

    /// <summary>제목 줄 종류 이름</summary>
    public string HeaderTitle => HeaderType?.Id ?? (_selection.Count > 0 ? _selection.MinBy(r => r.Number)!.Type : "");

    [ObservableProperty] public partial ObservableCollection<ScriptRow> Rows { get; private set; }

    [ObservableProperty] public partial IReadOnlyList<ActEntry> ActEntries { get; private set; }
    [ObservableProperty] public partial ActEntry? SelectedEntry { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument), nameof(DocumentTitle), nameof(DocumentPath), nameof(MainTitle), nameof(SubTitle))]
    public partial ActDocument? Document { get; private set; }

    [ObservableProperty] public partial string StatusText { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnknownCgText))]
    public partial int UnknownCgCount { get; private set; }

    public bool HasDocument => Document != null;

    public string FolderText => string.IsNullOrWhiteSpace(_settings.Editor.ScenarioFolder)
        ? "시나리오 폴더: (지정 안 함)"
        : $"시나리오 폴더: {_settings.Editor.ScenarioFolder}";

    public string DocumentTitle => Document == null
        ? "열린 막 없음"
        : $"{Document.Group} / {Document.ActName}{(Document.IsDirty ? "  ● 저장 안 됨" : "")}";

    public string? DocumentPath => Document?.FilePath;

    public string UnknownCgText => UnknownCgCount > 0 ? $"⚠ SpineData에 없는 CG 키 {UnknownCgCount}줄 (빨간 글자)" : "";

    /// <summary>막 제목 (루트 mainTitle) — 고치면 되돌리기 1단계</summary>
    public string MainTitle
    {
        get => Document?.MainTitle ?? "";
        set => Document?.SetMainTitle(value);
    }

    /// <summary>막 부제 (루트 subTitle)</summary>
    public string SubTitle
    {
        get => Document?.SubTitle ?? "";
        set => Document?.SetSubTitle(value);
    }

    /// <summary>표에서 이 행들을 선택해 달라 (화면이 처리)</summary>
    public event Action<IReadOnlyList<ScriptRow>>? SelectionRequested;

    public IReadOnlyList<ScriptRow> Selection => _selection;

    /// <summary>창이 뜬 뒤 1회: 막 목록 + 마지막 막·라인 다시 열기</summary>
    public void Initialize()
    {
        RefreshList();
        // 추가: 30초마다 자동 백업 시간 조건 확인 (화면 스레드에서)
        _backupTimer ??= new System.Threading.Timer(_ => _ui.Post(CheckAutoBackupTime), null,
                                                    TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        string? last = _settings.Editor.LastActPath;
        if (!string.IsNullOrEmpty(last) && File.Exists(last)) Open(last, _settings.Editor.LastLine);
    }

    // ------------------------------------------------------------------ 막 목록·열기

    [RelayCommand]
    private void PickFolder()
    {
        // 변경: 지정·저장·알림은 SettingsFlow 한 곳에서, 목록 갱신은 알림을 받아 OnSettingsChanged에서
        if (SettingsFlow.PickScenarioFolder(_settings, _dialogs, _saveSettings, _messenger) is { } folder)
            StatusText = $"막 {ActEntries.Count}개: {folder}";
    }

    /// <summary>추가: 설정 변경 알림 — 시나리오 폴더가 바뀌면 막 목록 다시 읽기</summary>
    private void OnSettingsChanged(SettingsChangedMessage m)
    {
        if (m.Area != SettingsArea.ScenarioFolder) return;
        OnPropertyChanged(nameof(FolderText));
        RefreshList();
    }

    [RelayCommand]
    private void RefreshList()
    {
        string? folder = _settings.Editor.ScenarioFolder;
        List<ActEntry> list = [];
        if (!string.IsNullOrWhiteSpace(folder))
        {
            try { list = ActFiles.Scan(folder); }
            catch (Exception ex) { _log.Error($"시나리오 폴더 읽기 실패: {folder}", ex); }
        }
        ActEntries = list;
        SyncEntry();
    }

    /// <summary>목록 선택을 지금 연 막에 맞춤 (목록 밖 파일이면 비움)</summary>
    private void SyncEntry()
    {
        _switching = true;
        try { SelectedEntry = Document?.FilePath is { } p ? ActEntries.FirstOrDefault(e => ActFiles.SamePath(e.Path, p)) : null; }
        finally { _switching = false; }
    }

    partial void OnSelectedEntryChanged(ActEntry? value)
    {
        if (_switching || value == null) return;
        if (Document?.FilePath is { } p && ActFiles.SamePath(p, value.Path)) return;
        if (!Open(value.Path)) _ui.Post(SyncEntry);   // 취소·실패 → 목록 선택을 지금 막으로 되돌림 (바인딩 갱신이 끝난 뒤)
    }

    [RelayCommand]
    private void OpenFile()
    {
        string? initial = Document?.FilePath is { } p ? Path.GetDirectoryName(p) : _settings.Editor.ScenarioFolder;
        string? path = _dialogs.PickFile("막 XML 열기", "막 XML (*.xml)|*.xml|모든 파일 (*.*)|*.*", initial);
        if (path != null) Open(path);
    }

    /// <summary>막 열기 (저장 안 된 변경이 있으면 먼저 물음). 열었으면 true</summary>
    public bool Open(string path, int selectLine = 0)
    {
        if (!ConfirmDiscard()) return false;
        ActDocument doc;
        try { doc = ActXmlFormat.Load(path); }
        catch (Exception ex)
        {
            _log.Error($"막 열기 실패: {path}", ex);
            _dialogs.ShowError("막 열기 실패", $"{path}\n\n{ex.Message}");
            return false;
        }

        SetDocument(doc);
        _settings.Editor.LastActPath = doc.FilePath;
        _settings.Editor.LastLine = Math.Max(0, selectLine);
        _saveSettings();
        SyncEntry();

        var notes = new List<string> { $"열기: {doc.Group} / {doc.ActName} ({doc.Lines.Count}줄)" };
        if (doc.SourceRootName != ActXmlFormat.RootTag) notes.Add($"저장하면 루트가 <{doc.SourceRootName}> → <{ActXmlFormat.RootTag}>");
        if (!doc.PreservesLayout) notes.Add("원래 줄 모양을 지킬 수 없는 파일 — 저장 시 전체를 표준 모양으로 씀");
        StatusText = string.Join(" · ", notes);
        _log.Info($"막 열기: {doc.FilePath} ({doc.Lines.Count}줄)");

        // 변경: 선택을 ViewModel이 기억 (표 화면이 아직 없어도 — 스크립트 탭을 처음 열 때 화면이 Selection을 적용)
        if (Rows.Count > 0) SelectLines([Rows[Math.Clamp(selectLine, 0, Rows.Count - 1)].Line]);
        return true;
    }

    private void SetDocument(ActDocument doc)
    {
        if (Document is { } old)
        {
            old.Lines.CollectionChanged -= OnLinesChanged;
            old.Undo.Changed -= OnUndoChanged;
            old.TitleChanged -= OnTitleChanged;
        }
        foreach (var r in Rows) r.Detach();
        _rowOf.Clear();
        _selection = [];

        Document = doc;
        Rows = new ObservableCollection<ScriptRow>(doc.Lines.Select(NewRow));   // 통째로 바꿔 행마다 알림이 가지 않도록
        doc.Lines.CollectionChanged += OnLinesChanged;
        doc.Undo.Changed += OnUndoChanged;
        doc.TitleChanged += OnTitleChanged;

        RebuildGroupNames();
        RefreshCgWarnings();
        NotifyCommands();

        // 추가: 자동 백업 초기화
        _backupEditMark = doc.Undo.EditCount;
        _lastBackupAt = _now();
        _lastBackupBytes = ActXmlFormat.ToBytes(doc);
        _originalBackedUp = false;
    }

    private ScriptRow NewRow(ScriptLine line, int index)
    {
        var row = new ScriptRow(line, this) { Number = index + 1 };
        _rowOf[line] = row;
        return row;
    }

    // ------------------------------------------------------------------ 저장

    private bool CanEdit() => Document != null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Save() => TrySave();

    /// <summary>열었던 경로에 저장 (직전 파일은 백업). 저장했으면 true</summary>
    public bool TrySave()
    {
        if (Document is not { FilePath: { } path } doc) return false;
        try
        {
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                bool changedOutside = info.LastWriteTimeUtc != doc.FileTimeUtc || info.Length != doc.FileLength;   // 변경: 변수로
                if (changedOutside)
                {
                    if (!_dialogs.Confirm("다른 곳에서 바뀐 파일",
                            $"{path}\n\n연 뒤에 다른 프로그램에서 파일이 바뀌었습니다. 덮어쓸까요?\n(덮어쓰기 전 파일은 백업 폴더에 보관합니다)"))
                    {
                        StatusText = "저장 취소";
                        return false;
                    }
                }
                // 변경: 저장 전 파일 백업은 이 막을 연 뒤 첫 저장(원본)과 다른 곳에서 바뀐 파일을 덮어쓸 때만 (그 외는 자동 백업)
                if (!_originalBackedUp || changedOutside)
                {
                    ActFiles.Backup(path, BackupFolder, _settings.Editor.BackupCount, _now());
                    _originalBackedUp = true;
                }
            }
            ActXmlFormat.Save(doc, path);
        }
        catch (Exception ex)
        {
            _log.Error($"막 저장 실패: {path}", ex);
            _dialogs.ShowError("막 저장 실패", $"{path}\n\n{ex.Message}");
            return false;
        }
        StatusText = $"저장: {doc.Group} / {doc.ActName} ({_now():HH:mm:ss})";   // 변경: '직전 파일은 백업 폴더에' 삭제
        _log.Info($"막 저장: {path}");
        OnPropertyChanged(nameof(DocumentTitle));
        return true;
    }

    /// <summary>
    /// 추가: 이 막에서 쓰는 Spine 세트를 게임에 내보내기 — 없으면 복사, 같으면 건너뜀, 다르면 물어봄.
    /// SpineData에 없는 키는 상태 줄에 알림
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ExportActSpines()
    {
        if (Document == null) return;
        var (assets, unknown) = CollectActSpines();
        if (assets.Count == 0 && unknown.Count == 0) { StatusText = "이 막에는 Spine CG가 없습니다"; return; }
        string? folder = GameExportFlow.EnsureFolder(_settings, _dialogs, _saveSettings, _messenger);   // 변경: messenger
        if (folder == null) return;
        string text = GameExportFlow.Summary(GameExportFlow.Run(_exporter, folder, assets, _dialogs, _log));
        if (unknown.Count > 0) text += $" · SpineData에 없음 {unknown.Count}: {string.Join(", ", unknown)}";
        StatusText = $"게임 내보내기: {text}";
    }

    /// <summary>추가: 이 막의 CG 키 → 세트 (중복·none·CGGROUP 이름 제외, 나온 순서). SpineData에 없는 키는 Unknown</summary>
    internal (List<SpineAsset> Assets, List<string> Unknown) CollectActSpines()
    {
        var assets = new List<SpineAsset>();
        var unknown = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Document == null) return (assets, unknown);
        foreach (var line in Document.Lines)
        {
            if (!IsCgLine(line)) continue;
            string cg = line.Get("CG").Trim();
            if (cg.Length == 0 || cg.Equals("none", StringComparison.OrdinalIgnoreCase) || _groupNames.Contains(cg) || !seen.Add(cg)) continue;
            if (_library.Find(cg) is { } a) assets.Add(a); else unknown.Add(cg);
        }
        return (assets, unknown);
    }

    /// <summary>저장 안 된 변경이 있으면 저장 / 저장 안 함 / 취소를 물음. 계속해도 되면 true (창 닫기·다른 막 열기 전)</summary>
    public bool ConfirmDiscard()
    {
        if (Document is not { IsDirty: true } doc) return true;
        return _dialogs.AskSave("저장하지 않은 변경", $"{doc.Group} / {doc.ActName}에 저장하지 않은 변경이 있습니다. 저장할까요?") switch
        {
            SaveChoice.Save => TrySave(),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    // ------------------------------------------------------------------ 라인 편집

    private bool HasSelection() => Document != null && _selection.Count > 0;

    /// <summary>표에서 선택이 바뀜 (화면이 알림). 첫 선택 라인을 '마지막 라인'으로 기억</summary>
    public void SetSelection(IReadOnlyList<ScriptRow> rows)
    {
        _selection = rows;
        if (rows.Count > 0)
        {
            int line = rows.Min(r => r.Number) - 1;
            if (_settings.Editor.LastLine != line)
            {
                _settings.Editor.LastLine = line;
                _saveSettings();
            }
        }
        DeleteRowsCommand.NotifyCanExecuteChanged();
        CopyLinesCommand.NotifyCanExecuteChanged();   // 추가
        CutLinesCommand.NotifyCanExecuteChanged();    // 추가
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        NotifyHeader();
    }

    private void NotifyHeader()
    {
        OnPropertyChanged(nameof(HeaderType));
        OnPropertyChanged(nameof(HeaderTitle));

        // 추가: 흐린 칸 이름은 제목 줄 종류의 줄에서만 → 제목 줄 종류가 바뀌면 이전·새 종류의 줄만 다시 알림
        var header = HeaderType;
        if (header == _hintType) return;
        foreach (var r in Rows)
            if (r.Def != null && (r.Def == header || r.Def == _hintType))
                foreach (var f in r.Fields) f.RefreshHint();
        _hintType = header;
    }

    private LineTypeDef? _hintType;

    internal void Edit(ScriptRow row, string attribute, string? value) =>
        Document?.SetAttribute(row.Line, attribute, value);

    /// <summary>
    /// 종류 변경 (드롭다운). 변경(E-003): 새 종류에 없는 빈 속성은 함께 지움 —
    /// 값이 있는 속성은 그대로 두어 새 팔레트에 없으면 기타에 보임. 되돌리기 1단계
    /// </summary>
    internal void ChangeType(ScriptRow row, string type)
    {
        if (Document == null) return;
        string old = row.Type;
        var def = Types.Find(type);
        Document.ChangeType(row.Line, type, name => def?.Has(name) == true);   // 변경 (이전: SetAttribute(row.Line, "Type", type))
        StatusText = $"{row.Number}번 줄 종류: {old} → {type}" + (row.Others.Length > 0 ? " (새 종류에 없는 속성은 기타 칸에)" : "");
    }

    /// <summary>추가: 제목 줄 경계선 드래그가 끝나면 그 칸 폭을 설정에 저장 (종류별)</summary>
    public void SaveFieldWidth(FieldDef field)
    {
        var type = Types.Types.FirstOrDefault(t => t.Fields.Contains(field));
        if (type == null) return;
        _settings.Editor.FieldWidths[type.WidthKey(field)] = field.Width;
        _saveSettings();
    }

    /// <summary>추가: 칸 폭을 LineTypes.xml 기본값으로</summary>
    [RelayCommand]
    private void ResetFieldWidths()
    {
        foreach (var t in Types.Types)
            foreach (var f in t.Fields) f.Width = f.DefaultWidth;
        _settings.Editor.FieldWidths.Clear();
        _saveSettings();
        StatusText = "칸 폭을 기본값으로 되돌렸습니다";
    }

    // ------------------------------------------------------------------ 추가(4-2): 무대 ↔ 스크립트

    /// <summary>무대에서 지금 선택한 캐릭터 정보 (MainViewModel이 스크립트 탭과 연결, 선택이 없으면 null)</summary>
    public Func<StageStamp?>? StampProvider { get; set; }

    /// <summary>이 줄의 이 칸으로 입력 커서를 옮겨 달라 (화면이 처리 — 라인에 적용·줄 추가 뒤 대사 칸으로)</summary>
    public event Action<ScriptRow, string>? FocusFieldRequested;

    /// <summary>[스크립트 → 무대]용: 선택한 첫 줄 시점의 무대 상태 (선택이 없으면 null)</summary>
    public StageSnapshot? ComputeSnapshot()
    {
        if (Document == null || _selection.Count == 0) return null;
        return StageStateCalculator.Compute(Document.Lines, _selection.Min(r => r.Number) - 1);
    }

    /// <summary>
    /// [라인에 적용] (Ctrl+Enter): 선택한 첫 줄(TEXT·SETCG·CGGROUP)에 CG·위치·애니메이션 세 칸을 모두 기록 (되돌리기 1단계).
    /// TEXT는 이름 칸이 비어 있을 때만 캐릭터 이름(CharacterData Ko)을 넣고, 그 줄의 대사 칸으로 커서 이동
    /// </summary>
    public void ApplyStamp(StageStamp stamp)
    {
        if (Document == null) return;
        if (_selection.Count == 0)
        {
            StatusText = "스크립트 표에서 적용할 줄을 선택하세요";
            return;
        }
        var row = _selection.MinBy(r => r.Number)!;
        string type = row.Type.ToUpperInvariant();
        if (type is not ("TEXT" or "SETCG" or "CGGROUP"))
        {
            StatusText = $"{row.Number}번 줄({row.Type})에는 적용할 수 없습니다 — TEXT · SETCG · CGGROUP 줄만";
            return;
        }
        var changes = new List<(string Name, string? Value)> { ("CG", stamp.CgKey), ("Position", stamp.Position), ("Animation", stamp.Animation) };
        if (type == "TEXT" && row.Get("Name").Length == 0 && _characters.KoreanName(stamp.NickName) is { } name)
            changes.Insert(0, ("Name", name));
        Document.SetAttributes(row.Line, changes);
        StatusText = $"{row.Number}번 줄에 적용: {stamp.CgKey} · {stamp.Position} · {(stamp.Animation.Length > 0 ? stamp.Animation : "(애니메이션 없음)")}";
        if (type == "TEXT") FocusFieldRequested?.Invoke(row, "Text");
    }

    /// <summary>
    /// 줄 추가 (Ctrl+N): 선택한 줄 아래(선택이 없으면 맨 끝)에 TEXT 줄.
    /// 무대에서 캐릭터를 선택하고 있으면 그 캐릭터의 이름·CG·위치·애니메이션을 채운 줄로. 추가한 줄의 대사 칸으로 커서 이동
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddText()
    {
        var doc = Document!;
        int index = _selection.Count > 0 ? _selection.Max(r => doc.Lines.IndexOf(r.Line)) + 1 : doc.Lines.Count;
        var line = StampProvider?.Invoke() is { } s
            ? ScriptLine.Create(("Type", "TEXT"), ("Name", _characters.KoreanName(s.NickName) ?? ""), ("Text", ""),
                                ("CG", s.CgKey), ("Position", s.Position), ("Animation", s.Animation), ("SpeakerType", "1"))
            : ScriptLine.Create(("Type", "TEXT"), ("Name", ""), ("Text", ""), ("SpeakerType", "1"));
        doc.InsertLines(index, [line]);
        SelectLines([line]);
        if (_rowOf.TryGetValue(line, out var row)) FocusFieldRequested?.Invoke(row, "Text");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteRows()
    {
        var doc = Document!;
        var lines = _selection.Select(r => r.Line).ToList();
        int first = lines.Min(doc.Lines.IndexOf);
        doc.RemoveLines(lines);
        StatusText = $"라인 {lines.Count}개 삭제 (Ctrl+Z로 되돌리기)";
        if (doc.Lines.Count > 0) SelectLines([doc.Lines[Math.Min(first, doc.Lines.Count - 1)]]);
        else SetSelection([]);
    }

    // ------------------------------------------------------------------ 추가: 줄 복사·잘라내기·붙여넣기 (Ctrl+Shift+C / D / V)

    /// <summary>선택한 줄을 막 순서대로 (선택한 순서와 무관)</summary>
    private List<ScriptLine> SelectedLinesInOrder() =>
        _selection.Select(r => r.Line).OrderBy(Document!.Lines.IndexOf).ToList();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyLines()
    {
        var lines = SelectedLinesInOrder();
        StatusText = _dialogs.SetClipboardText(LineClipboard.ToText(lines))
            ? $"줄 {lines.Count}개 복사 (Ctrl+Shift+V로 붙여넣기)"
            : "클립보드에 쓰지 못했습니다 — 다른 프로그램이 사용 중일 수 있음 (로그 참고)";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CutLines()
    {
        var lines = SelectedLinesInOrder();
        if (!_dialogs.SetClipboardText(LineClipboard.ToText(lines)))
        {
            StatusText = "클립보드에 쓰지 못해 잘라내지 않았습니다 (로그 참고)";
            return;
        }
        DeleteRows();
        StatusText = $"줄 {lines.Count}개 잘라내기 (Ctrl+Shift+V로 붙여넣기, Ctrl+Z로 되돌리기)";
    }

    /// <summary>선택한 줄 중 맨 아래 줄 밑에 (선택이 없으면 막 끝). 붙여넣은 줄을 선택, 되돌리기 1단계</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void PasteLines()
    {
        var doc = Document!;
        var lines = LineClipboard.Parse(_dialogs.GetClipboardText());
        if (lines.Count == 0)
        {
            StatusText = "클립보드에 붙여넣을 줄(<Line …/>)이 없습니다";
            return;
        }
        int index = _selection.Count > 0 ? _selection.Max(r => doc.Lines.IndexOf(r.Line)) + 1 : doc.Lines.Count;
        doc.InsertLines(index, lines);
        SelectLines(lines);
        StatusText = $"줄 {lines.Count}개 붙여넣기 ({index + 1}번 줄부터, Ctrl+Z로 되돌리기)";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveUp() => MoveSelection(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveDown() => MoveSelection(1);

    private void MoveSelection(int delta)
    {
        var lines = _selection.Select(r => r.Line).ToList();
        if (Document!.MoveLines(lines, delta)) SelectLines(lines);
    }

    private bool CanUndo() => Document?.Undo.CanUndo == true;
    private bool CanRedo() => Document?.Undo.CanRedo == true;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        string? label = Document!.Undo.UndoLabel;
        SelectLines(Document.UndoLast());
        StatusText = $"되돌리기: {label}";
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        string? label = Document!.Undo.RedoLabel;
        SelectLines(Document.RedoNext());
        StatusText = $"다시 하기: {label}";
    }

    private void SelectLines(IReadOnlyList<ScriptLine>? lines)
    {
        if (lines == null || lines.Count == 0) return;
        var rows = lines.Select(l => _rowOf.GetValueOrDefault(l)).OfType<ScriptRow>().ToList();
        if (rows.Count == 0) return;
        SetSelection(rows);
        SelectionRequested?.Invoke(rows);
    }

    // ------------------------------------------------------------------ 문서 변경 → 표

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (int i = 0; i < e.NewItems!.Count; i++)
                    Rows.Insert(e.NewStartingIndex + i, NewRow((ScriptLine)e.NewItems[i]!, e.NewStartingIndex + i));
                break;
            case NotifyCollectionChangedAction.Remove:
                for (int i = 0; i < e.OldItems!.Count; i++)
                {
                    var row = Rows[e.OldStartingIndex];
                    row.Detach();
                    _rowOf.Remove(row.Line);
                    Rows.RemoveAt(e.OldStartingIndex);
                }
                break;
            case NotifyCollectionChangedAction.Move:
                Rows.Move(e.OldStartingIndex, e.NewStartingIndex);
                break;
            default:
                foreach (var r in Rows) r.Detach();
                _rowOf.Clear();
                Rows = new ObservableCollection<ScriptRow>(Document!.Lines.Select(NewRow));
                break;
        }
        for (int i = 0; i < Rows.Count; i++) Rows[i].Number = i + 1;
        RebuildGroupNames();
        RefreshCgWarnings();
    }

    private void OnUndoChanged()
    {
        OnPropertyChanged(nameof(DocumentTitle));
        NotifyCommands();
        // 추가: 편집 N번 → 바로 자동 백업
        if (Document is { } doc && doc.Undo.EditCount - _backupEditMark >= _settings.Editor.AutoBackupEdits)
            AutoBackup($"편집 {_settings.Editor.AutoBackupEdits}번");
    }

    // ------------------------------------------------------------------ 추가: 자동 백업

    /// <summary>시간 조건 — 마지막 백업 뒤 M분이 지났고 그동안 편집이 1번 이상이면 백업 (편집 0번 = 자리 비움 → 하지 않음)</summary>
    internal void CheckAutoBackupTime()
    {
        if (Document is not { } doc || doc.Undo.EditCount == _backupEditMark) return;
        if (_now() - _lastBackupAt >= TimeSpan.FromMinutes(_settings.Editor.AutoBackupMinutes))
            AutoBackup($"{_settings.Editor.AutoBackupMinutes:0.#}분");
    }

    /// <summary>에디터 내용을 백업 폴더에 (저장 여부와 무관, 게임 폴더 파일은 그대로). 마지막 백업과 같은 내용이면 쓰지 않음</summary>
    private void AutoBackup(string reason)
    {
        if (Document is not { FilePath: { } path } doc) return;
        _backupEditMark = doc.Undo.EditCount;
        _lastBackupAt = _now();
        byte[] bytes = ActXmlFormat.ToBytes(doc);
        if (_lastBackupBytes != null && bytes.AsSpan().SequenceEqual(_lastBackupBytes)) return;
        try
        {
            string target = ActFiles.BackupBytes(path, bytes, BackupFolder, _settings.Editor.BackupCount, _lastBackupAt);
            _lastBackupBytes = bytes;
            _log.Info($"자동 백업 ({reason}): {target}");
        }
        catch (Exception ex) { _log.Error($"자동 백업 실패: {path}", ex); }
    }

    private void OnTitleChanged()
    {
        OnPropertyChanged(nameof(MainTitle));
        OnPropertyChanged(nameof(SubTitle));
    }

    internal void OnRowEdited(ScriptRow row)
    {
        // 변경: 종류가 CGGROUP에서/으로 바뀌어도 그룹 이름이 달라지므로 항상 다시 계산 (줄 수 수백 개 수준이라 가벼움)
        RebuildGroupNames();
        RefreshCgWarnings();
        if (_selection.Contains(row)) NotifyHeader();   // 선택한 줄의 종류가 바뀌면 제목 줄도
    }

    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ExportActSpinesCommand.NotifyCanExecuteChanged();   // 추가
        AddTextCommand.NotifyCanExecuteChanged();
        DeleteRowsCommand.NotifyCanExecuteChanged();
        CopyLinesCommand.NotifyCanExecuteChanged();    // 추가
        CutLinesCommand.NotifyCanExecuteChanged();     // 추가
        PasteLinesCommand.NotifyCanExecuteChanged();   // 추가
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------ CG 경고 (SpineData에 없는 키)

    /// <summary>
    /// SpineData에 없는 CG 키인지 (TEXT·SETCG·CGGROUP의 CG). 비었거나 none, 이 막의 CGGROUP 이름이면 경고 아님.
    /// 오타·아직 받지 않은 세트 검사 (2026-10-16 스파인 데이터 형식 통일로 옛 키는 모두 TE 키로 변환됨)
    /// </summary>
    internal bool IsUnknownCg(ScriptLine line)
    {
        if (!IsCgLine(line)) return false;   // 변경: IsCgLine 사용
        string cg = line.Get("CG").Trim();
        if (cg.Length == 0 || cg.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
        if (_groupNames.Contains(cg)) return false;
        return _library.Find(cg) == null;
    }

    /// <summary>추가: CG 속성을 쓰는 라인 종류 (TEXT·SETCG·CGGROUP)</summary>
    private static bool IsCgLine(ScriptLine line) =>
        line.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ||
        line.Type.Equals("SETCG", StringComparison.OrdinalIgnoreCase) ||
        line.Type.Equals("CGGROUP", StringComparison.OrdinalIgnoreCase);

    private void RebuildGroupNames()
    {
        _groupNames = Document == null
            ? new(StringComparer.OrdinalIgnoreCase)
            : Document.Lines.Where(l => l.Type.Equals("CGGROUP", StringComparison.OrdinalIgnoreCase))
                            .Select(l => l.Get("Name").Split(',', 2)[0].Trim())
                            .Where(n => n.Length > 0)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void RefreshCgWarnings()
    {
        int count = 0;
        foreach (var r in Rows)
        {
            r.RefreshWarning();
            if (r.CgMissing) count++;
        }
        UnknownCgCount = count;
    }
}
