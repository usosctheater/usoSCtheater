namespace SCTheaterEditor.Script;

/// <summary>되돌리기 단계 1개. 처음 실행도 Redo로 한다</summary>
public interface IUndoAction
{
    string Label { get; }
    void Redo();
    void Undo();

    /// <summary>다시 하기 뒤 선택할 라인 (비면 선택 유지)</summary>
    IReadOnlyList<ScriptLine> SelectAfterRedo { get; }

    /// <summary>되돌리기 뒤 선택할 라인 (비면 선택 유지)</summary>
    IReadOnlyList<ScriptLine> SelectAfterUndo { get; }
}

/// <summary>
/// 되돌리기 기록 (최대 Capacity단계). 저장한 시점을 기억해 "저장 안 된 변경"을 판단한다 —
/// 되돌려서 저장 시점과 같아지면 변경 없음. 저장해도 기록은 지우지 않음
/// </summary>
public sealed class UndoStack
{
    public const int DefaultCapacity = 500;

    private readonly List<IUndoAction> _items = new();
    private int _position;   // 실행된 단계 수 (= 다음에 다시 하기할 위치)
    private int _savedAt;    // 저장 시점의 _position (-1: 기록 밖으로 밀려나 돌아갈 수 없음)

    public UndoStack(int capacity = DefaultCapacity) => Capacity = Math.Max(1, capacity);

    public int Capacity { get; }
    public int Count => _items.Count;
    public bool CanUndo => _position > 0;
    public bool CanRedo => _position < _items.Count;
    public string? UndoLabel => CanUndo ? _items[_position - 1].Label : null;
    public string? RedoLabel => CanRedo ? _items[_position].Label : null;
    public bool IsDirty => _position != _savedAt;

    /// <summary>기록·위치·저장 상태가 바뀜</summary>
    public event Action? Changed;

    /// <summary>이미 실행한 단계를 기록 (다시 하기 기록은 버림)</summary>
    public void Push(IUndoAction action)
    {
        if (_position < _items.Count)
        {
            _items.RemoveRange(_position, _items.Count - _position);
            if (_savedAt > _position) _savedAt = -1;
        }
        _items.Add(action);
        _position++;
        if (_items.Count > Capacity)
        {
            _items.RemoveAt(0);
            _position--;
            _savedAt = _savedAt > 0 ? _savedAt - 1 : -1;
        }
        Changed?.Invoke();
    }

    public IUndoAction? Undo()
    {
        if (!CanUndo) return null;
        var a = _items[--_position];
        a.Undo();
        Changed?.Invoke();
        return a;
    }

    public IUndoAction? Redo()
    {
        if (!CanRedo) return null;
        var a = _items[_position++];
        a.Redo();
        Changed?.Invoke();
        return a;
    }

    /// <summary>지금을 저장 시점으로</summary>
    public void MarkSaved()
    {
        _savedAt = _position;
        Changed?.Invoke();
    }
}
