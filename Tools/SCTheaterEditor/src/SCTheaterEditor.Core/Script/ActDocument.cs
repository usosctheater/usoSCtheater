using System.Collections.ObjectModel;
using System.IO;

namespace SCTheaterEditor.Script;

/// <summary>
/// 막(Act) 문서 = 막 XML 1개: 제목·부제 + 라인 목록. 모든 편집은 여기 메서드를 거쳐 되돌리기(Undo)에 기록된다.
/// 파일 모양(선언 줄·줄바꿈·BOM·주석 등)은 ActXmlFormat이 읽을 때 기억해 두었다가 저장할 때 그대로 씀
/// </summary>
public sealed class ActDocument
{
    public ActDocument(string? filePath = null) => FilePath = filePath;

    /// <summary>열었던 경로 (저장도 여기로)</summary>
    public string? FilePath { get; internal set; }

    /// <summary>막 이름 = 파일 이름 (예: IL01)</summary>
    public string ActName => FilePath == null ? "(새 막)" : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>그룹(시나리오) = 파일이 있는 폴더 이름 (예: IL)</summary>
    public string Group => FilePath == null ? "" : Path.GetFileName(Path.GetDirectoryName(FilePath)) ?? "";

    public string MainTitle { get; private set; } = "";
    public string SubTitle { get; private set; } = "";

    public ObservableCollection<ScriptLine> Lines { get; } = new();
    public UndoStack Undo { get; } = new();

    /// <summary>저장하지 않은 변경이 있는지</summary>
    public bool IsDirty => Undo.IsDirty;

    /// <summary>읽은 파일의 루트 태그 (기존 막은 Scene — 저장하면 Act)</summary>
    public string SourceRootName { get; internal set; } = ActXmlFormat.RootTag;

    /// <summary>원래 줄 모양을 지키며 읽었는지 (false면 저장 시 모든 줄을 표준 모양으로 새로 씀)</summary>
    public bool PreservesLayout { get; internal set; }

    /// <summary>디스크 파일 상태 (저장 전에 다른 프로그램이 고쳤는지 확인)</summary>
    public DateTime FileTimeUtc { get; internal set; }
    public long FileLength { get; internal set; }

    /// <summary>제목·부제가 바뀜 (되돌리기 포함)</summary>
    public event Action? TitleChanged;

    // 파일 모양 (ActXmlFormat이 채움)
    internal List<string> Prologue { get; set; } = [ActXmlFormat.DefaultDeclaration];
    internal string Newline { get; set; } = "\r\n";
    internal bool EndsWithNewline { get; set; }
    internal bool HasBom { get; set; }
    internal List<KeyValuePair<string, string>> ExtraRootAttributes { get; } = new();
    internal List<string> TrailingRaw { get; set; } = new();

    internal void InitTitles(string main, string sub)
    {
        MainTitle = main;
        SubTitle = sub;
    }

    // ------------------------------------------------------------------ 편집 (되돌리기 기록)

    public void SetMainTitle(string? value) => SetTitle(main: true, value ?? "");
    public void SetSubTitle(string? value) => SetTitle(main: false, value ?? "");

    private void SetTitle(bool main, string value)
    {
        string old = main ? MainTitle : SubTitle;
        if (old == value) return;
        Execute(new TitleEdit(this, main, old, value));
    }

    private void ApplyTitle(bool main, string value)
    {
        if (main) MainTitle = value; else SubTitle = value;
        TitleChanged?.Invoke();
    }

    /// <summary>속성 1개 변경 (빈 값 = 속성 삭제)</summary>
    public void SetAttribute(ScriptLine line, string name, string? value) => SetAttributes(line, [(name, value)]);

    /// <summary>속성 여러 개를 한 단계로 변경 (빈 값 = 속성 삭제, 원래 비어 있던 속성에 빈 값은 변경 없음)</summary>
    public void SetAttributes(ScriptLine line, IReadOnlyList<(string Name, string? Value)> changes)
    {
        var list = new List<AttrChange>();
        foreach (var (name, value) in changes)
        {
            string? old = line.Find(name);
            if ((old ?? "") == (value ?? "")) continue;
            list.Add(new AttrChange(name, old, line.IndexOf(name), string.IsNullOrEmpty(value) ? null : value));
        }
        if (list.Count > 0) Execute(new AttributeEdit(line, list));
    }

    /// <summary>추가(9회차): 여러 줄의 속성을 한 단계로 변경 (보이스 일괄 추가 — Ctrl+Z 한 번에 모두 되돌림). 바뀐 줄 수</summary>
    public int SetAttributesMany(IReadOnlyList<(ScriptLine Line, IReadOnlyList<(string Name, string? Value)> Changes)> edits)
    {
        var parts = new List<AttributeEdit>();
        foreach (var (line, changes) in edits)
        {
            var list = new List<AttrChange>();
            foreach (var (name, value) in changes)
            {
                string? old = line.Find(name);
                if ((old ?? "") == (value ?? "")) continue;
                list.Add(new AttrChange(name, old, line.IndexOf(name), string.IsNullOrEmpty(value) ? null : value));
            }
            if (list.Count > 0) parts.Add(new AttributeEdit(line, list));
        }
        if (parts.Count > 0) Execute(new MultiEdit(parts));
        return parts.Count;
    }

    /// <summary>
    /// 추가(E-003): 종류 변경 — Type을 바꾸고, 값이 빈 속성 중 keepEmpty가 false인 것을 함께 지움 (되돌리기 1단계, 되돌리면 원래 자리로)
    /// </summary>
    public void ChangeType(ScriptLine line, string type, Func<string, bool> keepEmpty)
    {
        var list = new List<AttrChange>();
        string? oldType = line.Find("Type");
        if (oldType != type) list.Add(new AttrChange("Type", oldType, line.IndexOf("Type"), type));
        // 뒤쪽 속성부터 지움 → 되돌리기(역순)는 앞쪽부터 원래 자리에 넣어 순서가 그대로 돌아옴
        for (int i = line.Attributes.Count - 1; i >= 0; i--)
        {
            var a = line.Attributes[i];
            if (a.Key != "Type" && a.Value.Length == 0 && !keepEmpty(a.Key))
                list.Add(new AttrChange(a.Key, "", i, null));
        }
        if (list.Count > 0) Execute(new AttributeEdit(line, list));
    }

    public void InsertLines(int index, IReadOnlyList<ScriptLine> lines)
    {
        if (lines.Count == 0) return;
        Execute(new InsertEdit(this, Math.Clamp(index, 0, Lines.Count), lines.ToList()));
    }

    public void RemoveLines(IEnumerable<ScriptLine> lines)
    {
        var items = lines.Distinct()
                         .Select(l => (Index: Lines.IndexOf(l), Line: l))
                         .Where(x => x.Index >= 0)
                         .OrderBy(x => x.Index)
                         .ToList();
        if (items.Count > 0) Execute(new RemoveEdit(this, items));
    }

    /// <summary>위(-1)·아래(+1)로 한 칸 (떨어진 여러 줄도 각각 한 칸). 끝이라 못 옮기면 false</summary>
    public bool MoveLines(IEnumerable<ScriptLine> lines, int delta)
    {
        var sel = lines.Distinct().Where(Lines.Contains).ToList();
        if (sel.Count == 0 || delta == 0) return false;
        var idx = sel.Select(Lines.IndexOf).ToList();
        if (delta < 0 ? idx.Min() == 0 : idx.Max() == Lines.Count - 1) return false;
        Execute(new MoveEdit(this, sel, delta < 0 ? -1 : 1));
        return true;
    }

    /// <summary>되돌리기. 그 뒤 선택할 라인 (할 게 없으면 null)</summary>
    public IReadOnlyList<ScriptLine>? UndoLast() => Undo.Undo()?.SelectAfterUndo;

    /// <summary>다시 하기. 그 뒤 선택할 라인 (할 게 없으면 null)</summary>
    public IReadOnlyList<ScriptLine>? RedoNext() => Undo.Redo()?.SelectAfterRedo;

    private void Execute(IUndoAction action)
    {
        action.Redo();
        Undo.Push(action);
    }

    // ------------------------------------------------------------------ 되돌리기 단계

    private sealed record AttrChange(string Name, string? Old, int OldIndex, string? New);

    private sealed class AttributeEdit(ScriptLine line, List<AttrChange> changes) : IUndoAction
    {
        public string Label => $"{string.Join(", ", changes.Select(c => c.Name))} 변경";
        public IReadOnlyList<ScriptLine> SelectAfterRedo { get; } = [line];
        public IReadOnlyList<ScriptLine> SelectAfterUndo => SelectAfterRedo;

        public void Redo()
        {
            foreach (var c in changes) line.SetValue(c.Name, c.New);
        }

        public void Undo()
        {
            for (int i = changes.Count - 1; i >= 0; i--) line.SetValue(changes[i].Name, changes[i].Old, changes[i].OldIndex);
        }
    }

    /// <summary>추가(9회차): 여러 줄 속성 변경을 한 단계로</summary>
    private sealed class MultiEdit(List<AttributeEdit> parts) : IUndoAction
    {
        public string Label => $"{parts.Count}줄 변경";
        public IReadOnlyList<ScriptLine> SelectAfterRedo { get; } = parts.SelectMany(p => p.SelectAfterRedo).ToList();
        public IReadOnlyList<ScriptLine> SelectAfterUndo => SelectAfterRedo;

        public void Redo()
        {
            foreach (var p in parts) p.Redo();
        }

        public void Undo()
        {
            for (int i = parts.Count - 1; i >= 0; i--) parts[i].Undo();
        }
    }

    private sealed class TitleEdit(ActDocument doc, bool main, string old, string value) : IUndoAction
    {
        public string Label => main ? "제목 변경" : "부제 변경";
        public IReadOnlyList<ScriptLine> SelectAfterRedo => [];
        public IReadOnlyList<ScriptLine> SelectAfterUndo => [];
        public void Redo() => doc.ApplyTitle(main, value);
        public void Undo() => doc.ApplyTitle(main, old);
    }

    private sealed class InsertEdit(ActDocument doc, int index, List<ScriptLine> lines) : IUndoAction
    {
        public string Label => $"라인 {lines.Count}개 추가";
        public IReadOnlyList<ScriptLine> SelectAfterRedo => lines;
        public IReadOnlyList<ScriptLine> SelectAfterUndo => doc.Lines.Count == 0 ? [] : [doc.Lines[Math.Min(index, doc.Lines.Count - 1)]];

        public void Redo()
        {
            for (int i = 0; i < lines.Count; i++) doc.Lines.Insert(index + i, lines[i]);
        }

        public void Undo()
        {
            for (int i = lines.Count - 1; i >= 0; i--) doc.Lines.RemoveAt(index + i);
        }
    }

    private sealed class RemoveEdit(ActDocument doc, List<(int Index, ScriptLine Line)> items) : IUndoAction
    {
        public string Label => $"라인 {items.Count}개 삭제";
        public IReadOnlyList<ScriptLine> SelectAfterUndo => items.Select(x => x.Line).ToList();
        public IReadOnlyList<ScriptLine> SelectAfterRedo =>
            doc.Lines.Count == 0 ? [] : [doc.Lines[Math.Min(items[0].Index, doc.Lines.Count - 1)]];

        public void Redo()
        {
            for (int i = items.Count - 1; i >= 0; i--) doc.Lines.RemoveAt(items[i].Index);
        }

        public void Undo()
        {
            foreach (var (index, line) in items) doc.Lines.Insert(index, line);
        }
    }

    private sealed class MoveEdit(ActDocument doc, List<ScriptLine> lines, int delta) : IUndoAction
    {
        public string Label => delta < 0 ? "위로 이동" : "아래로 이동";
        public IReadOnlyList<ScriptLine> SelectAfterRedo => lines;
        public IReadOnlyList<ScriptLine> SelectAfterUndo => lines;
        public void Redo() => Move(delta);
        public void Undo() => Move(-delta);

        private void Move(int d)
        {
            // 위로는 앞 줄부터, 아래로는 뒤 줄부터 (아직 안 옮긴 줄의 위치가 바뀌지 않도록)
            var idx = lines.Select(doc.Lines.IndexOf).OrderBy(i => i).ToList();
            if (d > 0) idx.Reverse();
            foreach (int i in idx) doc.Lines.Move(i, i + d);
        }
    }
}
