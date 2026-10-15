using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace SCTheaterEditor.Script;

/// <summary>팔레트의 속성 칸 1개. 폭은 제목 줄 경계선 드래그로 바뀌고 같은 종류의 모든 행이 함께 따름</summary>
public sealed class FieldDef(string attr, string label, double width, string hint) : INotifyPropertyChanged
{
    public const double MinWidth = 30;

    public string Attr { get; } = attr;
    public string Label { get; } = label;
    public string Hint { get; } = hint;
    public double DefaultWidth { get; } = width;

    /// <summary>제목 줄 툴팁</summary>
    public string ToolTip => Hint.Length > 0 ? $"{Label} ({Attr}) — {Hint}" : $"{Label} ({Attr})";

    private double _width = width;
    public double Width
    {
        get => _width;
        set
        {
            double v = Math.Max(MinWidth, Math.Round(value));
            if (v == _width) return;
            _width = v;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>종류(Line Type) 1개의 팔레트</summary>
public sealed class LineTypeDef(string id, IReadOnlyList<FieldDef> fields)
{
    public string Id { get; } = id;
    public IReadOnlyList<FieldDef> Fields { get; } = fields;

    /// <summary>팔레트에 있는 속성인지 (Type 제외)</summary>
    public bool Has(string attr) => Fields.Any(f => f.Attr == attr);

    /// <summary>설정 저장 키 (예: TEXT.Text)</summary>
    public string WidthKey(FieldDef f) => $"{Id}.{f.Attr}";
}

/// <summary>
/// 종류별 팔레트 목록 — exe에 넣은 Resources/LineTypes.xml (원본: ScriptManual.xlsx).
/// 종류가 자주 늘지 않으므로 시작할 때 한 번 읽어 두고, 행은 자기 종류의 팔레트만 골라 보여 준다
/// </summary>
public sealed class LineTypeCatalog
{
    private readonly Dictionary<string, LineTypeDef> _byId;

    public LineTypeCatalog(IReadOnlyList<LineTypeDef> types)
    {
        Types = types;
        _byId = types.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        Ids = types.Select(t => t.Id).ToList();
    }

    public IReadOnlyList<LineTypeDef> Types { get; }

    /// <summary>드롭다운 목록 (파일 순서)</summary>
    public IReadOnlyList<string> Ids { get; }

    /// <summary>종류 찾기 (대소문자 무시 — 게임도 ToUpper로 판단). 없으면 null</summary>
    public LineTypeDef? Find(string? id) => id != null && _byId.TryGetValue(id, out var t) ? t : null;

    public static LineTypeCatalog Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException("LineTypes.xml 루트가 없습니다");
        var types = root.Elements("Type").Select(t => new LineTypeDef(
            (string?)t.Attribute("Id") ?? throw new InvalidDataException("Type Id가 없습니다"),
            t.Elements("Field").Select(f => new FieldDef(
                (string?)f.Attribute("Attr") ?? throw new InvalidDataException("Field Attr가 없습니다"),
                (string?)f.Attribute("Label") ?? (string)f.Attribute("Attr")!,
                double.TryParse((string?)f.Attribute("Width"), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double w) ? w : 100,
                (string?)f.Attribute("Hint") ?? "")).ToList())).ToList();
        return new LineTypeCatalog(types);
    }

    public static LineTypeCatalog LoadEmbedded()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LineTypes.xml")
                      ?? throw new InvalidOperationException("LineTypes.xml 리소스가 없습니다");
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    }

    /// <summary>저장된 칸 폭 적용 (키: TEXT.Text)</summary>
    public void ApplyWidths(IReadOnlyDictionary<string, double>? widths)
    {
        if (widths == null) return;
        foreach (var t in Types)
            foreach (var f in t.Fields)
                if (widths.TryGetValue(t.WidthKey(f), out double w)) f.Width = w;
    }
}
