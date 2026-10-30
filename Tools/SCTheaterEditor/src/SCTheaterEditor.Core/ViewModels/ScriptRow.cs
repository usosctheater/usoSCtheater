using CommunityToolkit.Mvvm.ComponentModel;
using SCTheaterEditor.Script;

namespace SCTheaterEditor.ViewModels;

public enum ScriptRowKind { Text, Background, Other }

/// <summary>
/// 스크립트 표 1행 = 라인 1개. (변경: 종류별 팔레트)
/// - # · 종류(드롭다운)는 고정, 그 오른쪽 칸은 이 행의 종류 팔레트(LineTypes.xml)의 속성들
/// - 팔레트에 없는 속성은 '기타'에 읽기 전용으로 표시 (파일에는 그대로 남음). 모르는 종류면 모든 속성이 기타
/// - 종류를 바꾸면 Type 속성만 바뀌고, 칸 구성이 새 종류의 팔레트로 바뀜
/// 고친 값은 ActEditorViewModel → ActDocument(되돌리기 기록)로 감
/// </summary>
public sealed partial class ScriptRow : ObservableObject
{
    private string _type = "";
    private LineTypeDef? _def;
    private IReadOnlyList<FieldCell> _fields = [];
    private IReadOnlyList<string> _typeOptions = [];
    private bool _cgMissing;
    private bool _charIdUnknown;   // 추가(다국어)

    internal ScriptRow(ScriptLine line, ActEditorViewModel owner)
    {
        Line = line;
        Owner = owner;
        BuildFields();
        line.Changed += OnLineChanged;
    }

    public ScriptLine Line { get; }
    internal ActEditorViewModel Owner { get; }

    /// <summary>표의 줄 번호 (1부터)</summary>
    [ObservableProperty] public partial int Number { get; set; }

    /// <summary>종류 (드롭다운). 빈 값은 무시 (목록 재사용 중 드롭다운이 잠깐 비는 경우)</summary>
    public string Type
    {
        get => Line.Type;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == Line.Type) return;
            Owner.ChangeType(this, value);
        }
    }

    /// <summary>드롭다운 목록 (모르는 종류면 그 이름을 덧붙임). 같은 목록 객체를 행끼리 공유</summary>
    public IReadOnlyList<string> TypeOptions => _typeOptions;

    /// <summary>이 행의 팔레트 (모르는 종류면 null)</summary>
    public LineTypeDef? Def => _def;

    /// <summary>팔레트 칸들 (종류가 바뀔 때만 새로 만듦)</summary>
    public IReadOnlyList<FieldCell> Fields => _fields;

    public ScriptRowKind Kind => Line.Type.ToUpperInvariant() switch
    {
        "TEXT" => ScriptRowKind.Text,
        "BG" => ScriptRowKind.Background,
        _ => ScriptRowKind.Other,
    };

    /// <summary>기타: 팔레트에 없는 속성 "이름=값; 이름=값" (읽기 전용)</summary>
    public string Others => string.Join("; ", OtherAttributes().Select(a => $"{a.Key}={a.Value}"));

    /// <summary>추가: 기타 툴팁 — 비어 있으면 null (빈 툴팁 상자가 뜨지 않도록)</summary>
    public string? OthersToolTip => Others is { Length: > 0 } s ? s : null;

    /// <summary>SpineData에 없는 CG 키 (옛 CG 키 등)</summary>
    public bool CgMissing => _cgMissing;

    /// <summary>추가(다국어): 화자(CharId)에 CharacterData에 없는 ID가 있음 (오타 등)</summary>
    public bool CharIdUnknown => _charIdUnknown;

    /// <summary>속성 값 (없으면 "")</summary>
    public string Get(string attr) => Line.Get(attr);

    internal IEnumerable<KeyValuePair<string, string>> OtherAttributes() =>
        Line.Attributes.Where(a => a.Key != "Type" && (_def == null || !_def.Has(a.Key)));

    private void BuildFields()
    {
        _type = Line.Type;
        _def = Owner.Types.Find(_type);
        _fields = _def?.Fields.Select(f => new FieldCell(this, f)).ToList() ?? [];
        _typeOptions = _def != null ? Owner.Types.Ids : [.. Owner.Types.Ids, _type];
    }

    internal void Detach() => Line.Changed -= OnLineChanged;

    /// <summary>CG·화자 경고 다시 계산 (바뀐 경우만 알림) — 변경(다국어): 화자(CharId) 경고 추가</summary>
    internal void RefreshWarning()
    {
        bool missing = Owner.IsUnknownCg(Line);
        bool unknownId = Owner.IsUnknownCharId(Line);
        bool cgChanged = missing != _cgMissing, idChanged = unknownId != _charIdUnknown;
        if (!cgChanged && !idChanged) return;
        _cgMissing = missing;
        _charIdUnknown = unknownId;
        if (cgChanged) OnPropertyChanged(nameof(CgMissing));
        if (idChanged) OnPropertyChanged(nameof(CharIdUnknown));
        foreach (var f in _fields)
            if ((cgChanged && f.Def.Attr == "CG") || (idChanged && f.Def.Attr == "CharId")) f.Refresh();
    }

    private void OnLineChanged(ScriptLine _)
    {
        if (Line.Type != _type)
        {
            BuildFields();                     // 종류가 바뀜 → 새 팔레트
            OnPropertyChanged(string.Empty);
        }
        else
        {
            foreach (var f in _fields) f.Refresh();
            OnPropertyChanged(nameof(Others));
            OnPropertyChanged(nameof(OthersToolTip));
        }
        Owner.OnRowEdited(this);
    }
}

/// <summary>팔레트의 칸 1개 (행 × 속성). 값을 고치면 되돌리기 1단계</summary>
public sealed class FieldCell : ObservableObject
{
    internal FieldCell(ScriptRow row, FieldDef def)
    {
        Row = row;
        Def = def;
    }

    public ScriptRow Row { get; }
    public FieldDef Def { get; }

    /// <summary>속성 값. 빈 값으로 바꾸면 속성 삭제</summary>
    public string Value
    {
        get => Row.Line.Get(Def.Attr);
        set => Row.Owner.Edit(Row, Def.Attr, value);
    }

    /// <summary>빈 칸인지</summary>
    public bool IsEmpty => Value.Length == 0;

    /// <summary>
    /// 변경: 흐린 글씨 칸 이름은 '제목 줄과 같은 종류(= 선택한 줄의 종류)'의 줄에서만, 빈 칸일 때 표시.
    /// 다른 종류의 줄은 마우스를 올려 툴팁으로 확인 (2026-10-15 사용자 요청)
    /// </summary>
    // 변경(E-002): 빈 칸 판단은 화면(입력 상자에 보이는 글자)에서 함 → 여기서는 '선택한 줄과 같은 종류인지'만
    // 변경(다국어): 이름 칸의 화자 이름(흐린 글씨)은 선택한 줄과 상관없이 모든 TEXT 줄에서 보임 (실제로 출력될 이름)
    public bool ShowHint => SpeakerHint != null || (Row.Def != null && Row.Def == Row.Owner.HeaderType);
    // (이전) public bool ShowHint => Row.Def != null && Row.Def == Row.Owner.HeaderType;

    /// <summary>추가(다국어): 빈 칸의 흐린 글씨 — 이름 칸이고 화자(CharId)가 있으면 그 DB 이름, 아니면 칸 이름</summary>
    public string HintText => SpeakerHint ?? Def.Label;

    /// <summary>추가(다국어): 이름 칸일 때 화자(CharId)의 한국어 이름 (없으면 null)</summary>
    private string? SpeakerHint => Def.Attr == "Name" ? Row.Owner.SpeakerHint(Row.Line) : null;

    internal void RefreshHint() => OnPropertyChanged(nameof(ShowHint));

    /// <summary>CG 칸이고 SpineData에 없는 키 / 추가(다국어): 화자 칸이고 CharacterData에 없는 ID</summary>
    public bool Warning => (Def.Attr == "CG" && Row.CgMissing) || (Def.Attr == "CharId" && Row.CharIdUnknown);

    public string ToolTip => !Warning ? Def.ToolTip
        : Def.Attr == "CharId"
            ? Def.ToolTip + "\n⚠ CharacterData에 없는 화자 ID (오타이거나 등록되지 않은 인물) — 등록되지 않은 인물은 화자를 비우고 이름 칸에 직접 적음"
            : Def.ToolTip + "\n⚠ SpineData에 없는 CG 키 (옛 CG 키이거나 아직 받지 않은 세트) — 대응표 작업 전까지 무대에 올릴 수 없음";

    internal void Refresh() => OnPropertyChanged(string.Empty);
}
