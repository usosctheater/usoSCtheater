using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    private bool _switching;                                  // 막 목록 선택을 코드에서 맞추는 중 (다시 열지 않음)
    private IReadOnlyList<ScriptRow> _selection = [];
    private HashSet<string> _groupNames = new(StringComparer.OrdinalIgnoreCase);

    public ActEditorViewModel(AppSettings settings, SpineLibrary library, IDialogService dialogs, IUiDispatcher ui,
                              Action saveSettings, AppLog log, string? backupFolder = null, Func<DateTime>? now = null,
                              LineTypeCatalog? types = null)
    {
        // 추가: 종류별 팔레트 (LineTypes.xml) + 저장된 칸 폭
        Types = types ?? LineTypeCatalog.LoadEmbedded();
        Types.ApplyWidths(settings.Editor.FieldWidths);
        _settings = settings;
        _library = library;
        _dialogs = dialogs;
        _ui = ui;
        _saveSettings = saveSettings;
        _log = log;
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
        string? last = _settings.Editor.LastActPath;
        if (!string.IsNullOrEmpty(last) && File.Exists(last)) Open(last, _settings.Editor.LastLine);
    }

    // ------------------------------------------------------------------ 막 목록·열기

    [RelayCommand]
    private void PickFolder()
    {
        string? folder = _dialogs.PickFolder("시나리오 폴더 지정 (게임 Assets\\Resources\\Scenario)", _settings.Editor.ScenarioFolder);
        if (folder == null) return;
        _settings.Editor.ScenarioFolder = folder;
        _saveSettings();
        OnPropertyChanged(nameof(FolderText));
        RefreshList();
        StatusText = $"막 {ActEntries.Count}개: {folder}";
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
                if (info.LastWriteTimeUtc != doc.FileTimeUtc || info.Length != doc.FileLength)
                {
                    if (!_dialogs.Confirm("다른 곳에서 바뀐 파일",
                            $"{path}\n\n연 뒤에 다른 프로그램에서 파일이 바뀌었습니다. 덮어쓸까요?\n(덮어쓰기 전 파일은 백업 폴더에 보관합니다)"))
                    {
                        StatusText = "저장 취소";
                        return false;
                    }
                }
                ActFiles.Backup(path, BackupFolder, _settings.Editor.BackupCount, _now());
            }
            ActXmlFormat.Save(doc, path);
        }
        catch (Exception ex)
        {
            _log.Error($"막 저장 실패: {path}", ex);
            _dialogs.ShowError("막 저장 실패", $"{path}\n\n{ex.Message}");
            return false;
        }
        StatusText = $"저장: {doc.Group} / {doc.ActName} ({_now():HH:mm:ss}, 직전 파일은 백업 폴더에)";
        _log.Info($"막 저장: {path}");
        OnPropertyChanged(nameof(DocumentTitle));
        return true;
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
    /// 추가: 종류 변경 (드롭다운). Type 속성만 바꾸고 다른 속성은 그대로 — 새 팔레트에 없는 속성은 기타에 보임. 되돌리기 1단계
    /// </summary>
    internal void ChangeType(ScriptRow row, string type)
    {
        if (Document == null) return;
        string old = row.Type;
        Document.SetAttribute(row.Line, "Type", type);
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

    /// <summary>TEXT 라인 추가 (선택한 줄 아래, 선택이 없으면 맨 끝)</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddText()
    {
        var doc = Document!;
        int index = _selection.Count > 0 ? _selection.Max(r => doc.Lines.IndexOf(r.Line)) + 1 : doc.Lines.Count;
        var line = ScriptLine.Create(("Type", "TEXT"), ("Name", ""), ("Text", ""), ("SpeakerType", "1"));
        doc.InsertLines(index, [line]);
        SelectLines([line]);
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
        AddTextCommand.NotifyCanExecuteChanged();
        DeleteRowsCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------ CG 경고 (옛 CG 키 등)

    /// <summary>
    /// SpineData에 없는 CG 키인지 (TEXT·SETCG·CGGROUP의 CG). 비었거나 none, 이 막의 CGGROUP 이름이면 경고 아님.
    /// 옛 CG 키(mei_sports_2 등)는 TE 키({nickName}_{파일 이름})와 달라 여기 걸림 — 대응표는 이후 작업
    /// </summary>
    internal bool IsUnknownCg(ScriptLine line)
    {
        string type = line.Type;
        if (!type.Equals("TEXT", StringComparison.OrdinalIgnoreCase) &&
            !type.Equals("SETCG", StringComparison.OrdinalIgnoreCase) &&
            !type.Equals("CGGROUP", StringComparison.OrdinalIgnoreCase)) return false;
        string cg = line.Get("CG").Trim();
        if (cg.Length == 0 || cg.Equals("none", StringComparison.OrdinalIgnoreCase)) return false;
        if (_groupNames.Contains(cg)) return false;
        return _library.Find(cg) == null;
    }

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
