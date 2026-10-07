using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace SCResourceGrabber.Services;

/// <summary>
/// 프로그램 전용 단축키. settings.json의 "Hotkeys"(동작 이름 → "Ctrl+S" 형식)를 읽어 키 입력을 동작 이름으로 바꾼다.
/// 키를 바꾸려면 settings.json을 고친 뒤 프로그램을 다시 실행한다. 빈 문자열이면 그 동작은 단축키 없음.
/// </summary>
public sealed class HotkeyMap
{
    // 동작 이름 (settings.json 키와 같음)
    public const string SaveChecked = "SaveChecked";         // 체크한 항목 저장
    public const string ToggleTracking = "ToggleTracking";   // 추적 모드 열기/닫기
    public const string ToggleAllChecks = "ToggleAllChecks"; // 전체 선택/해제
    public const string ClearList = "ClearList";             // 목록 비우기

    /// <summary>기본값. 새 동작을 추가하면 여기에 한 줄 추가 → 기존 settings.json에도 자동으로 채워짐.</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [SaveChecked] = "Ctrl+S",
        [ToggleTracking] = "Ctrl+R",
        [ToggleAllChecks] = "Ctrl+A",
        [ClearList] = "Ctrl+L",
    };

    private readonly List<(KeyGesture gesture, string action)> _bindings = new();

    /// <summary>잘못된 키 문자열·중복 등 경고 (상태 표시줄에 띄움)</summary>
    public List<string> Warnings { get; } = new();

    public HotkeyMap(IDictionary<string, string> config)
    {
        var converter = new KeyGestureConverter();
        foreach (var (action, text) in config)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!Defaults.ContainsKey(action)) { Warnings.Add($"알 수 없는 동작 '{action}'"); continue; }
            try
            {
                if (converter.ConvertFromInvariantString(text) is not KeyGesture g) throw new FormatException();
                var dup = _bindings.FirstOrDefault(b => b.gesture.Key == g.Key && b.gesture.Modifiers == g.Modifiers);
                if (dup.action != null) { Warnings.Add($"{text} 중복 ({dup.action} / {action}) — {dup.action}만 사용"); continue; }
                _bindings.Add((g, action));
            }
            catch
            {
                Warnings.Add($"'{action}'의 키 '{text}'를 읽을 수 없음");
            }
        }
    }

    /// <summary>키 입력에 해당하는 동작 이름. 글자 입력 칸에 포커스가 있으면 무시(Ctrl+A 등 기본 편집 키 보호).</summary>
    public string? Resolve(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }) return null;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        foreach (var (g, action) in _bindings)
            if (g.Key == key && g.Modifiers == Keyboard.Modifiers) return action;
        return null;
    }

    /// <summary>툴팁 표시용 (예: "Ctrl+S"). 없으면 null.</summary>
    public string? GestureText(string action)
    {
        var b = _bindings.FirstOrDefault(x => x.action == action);
        return b.action == null ? null : b.gesture.GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture);
    }
}
