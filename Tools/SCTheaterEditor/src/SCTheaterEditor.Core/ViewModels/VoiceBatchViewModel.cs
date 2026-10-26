using CommunityToolkit.Mvvm.ComponentModel;
using SCTheaterEditor.Script;

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 추가(9회차): TEXT에 보이스 일괄 추가 미리보기 창 — 방식(덮어쓰기 / 빈 줄에만)을 바꾸면 목록을 바로 다시 계산.
/// 창이 [적용]으로 닫히면 ActEditorViewModel이 Changes인 줄만 반영 (되돌리기 1단계)
/// </summary>
public sealed partial class VoiceBatchViewModel : ObservableObject
{
    private readonly IReadOnlyList<(ScriptLine Line, int Number)> _lines;
    private readonly IReadOnlyList<string> _keys;

    public VoiceBatchViewModel(IReadOnlyList<(ScriptLine Line, int Number)> lines, IReadOnlyList<string> keys, string folderText,
                               VoiceBatchMode mode = VoiceBatchMode.Overwrite)
    {
        _lines = lines;
        _keys = keys;
        FolderText = folderText;
        Items = [];
        SummaryText = "";
        Mode = mode;
        Recalculate();
    }

    /// <summary>보이스 폴더 · 시작 키 안내</summary>
    public string FolderText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverwrite), nameof(IsEmptyOnly))]
    public partial VoiceBatchMode Mode { get; set; }

    public bool IsOverwrite
    {
        get => Mode == VoiceBatchMode.Overwrite;
        set { if (value) Mode = VoiceBatchMode.Overwrite; }
    }

    public bool IsEmptyOnly
    {
        get => Mode == VoiceBatchMode.EmptyOnly;
        set { if (value) Mode = VoiceBatchMode.EmptyOnly; }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    public partial IReadOnlyList<VoiceBatchItem> Items { get; private set; }

    [ObservableProperty] public partial string SummaryText { get; private set; }

    public bool CanApply => Items.Any(i => i.Changes);

    partial void OnModeChanged(VoiceBatchMode value) => Recalculate();

    private void Recalculate()
    {
        Items = VoiceBatch.Plan(_lines, _keys, Mode);
        int changes = Items.Count(i => i.Changes);
        int used = Items.Count(i => i.NewVoice != null);
        int skipped = Items.Count(i => i.NewVoice == null && i.Note != VoiceBatch.NoteShort);
        int shortCount = Items.Count(i => i.Note == VoiceBatch.NoteShort);
        SummaryText = $"TEXT {Items.Count}줄 · 넣기 {used}줄 (바뀜 {changes}) · 건너뜀 {skipped}줄" +
                      (shortCount > 0 ? $" · 보이스 부족 {shortCount}줄 (남은 보이스 {_keys.Count}개를 다 씀)" : $" · 남는 보이스 {_keys.Count - used}개");
    }
}
