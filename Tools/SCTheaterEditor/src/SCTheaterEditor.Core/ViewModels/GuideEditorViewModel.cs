using CommunityToolkit.Mvvm.ComponentModel;
using SCTheaterEditor.Script;
using SCTheaterEditor.Services;
using UsoSCTheater.Shared;   //[공유 코드 통합] LanguageInfo

namespace SCTheaterEditor.ViewModels;

/// <summary>
/// 추가(번역 모드 4회차): [지침 편집] 창 — 언어별 번역 지침(TranslationGuide_{언어}.md)을 글 상자로 고침.
/// 파일이 없으면 빈 글로 시작, [저장]하면 파일이 생김. 저장하지 않고 닫으면 창에서 확인
/// </summary>
public sealed partial class GuideEditorViewModel : ObservableObject
{
    private readonly TranslationGuideStore _store;
    private readonly AppLog _log;
    private string _saved;

    public GuideEditorViewModel(TranslationGuideStore store, LanguageInfo language, AppLog log)
    {
        _store = store;
        _log = log;
        Language = language;
        string? text = null;
        try { text = store.Load(language.Code); }
        catch (Exception ex)
        {
            log.Error("번역 지침 읽기 실패", ex);
            StatusText = $"⚠ 읽지 못했습니다: {ex.Message}";
        }
        Exists = text != null;
        _saved = text ?? "";
        Text = _saved;
        StatusText ??= Exists ? "" : "지침 파일이 아직 없습니다 — [저장]하면 만들어집니다";
    }

    public LanguageInfo Language { get; }
    public string FilePath => _store.PathFor(Language.Code);
    public string Title => $"번역 지침 ({Language.NativeName}) — {System.IO.Path.GetFileName(FilePath)}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    public partial string Text { get; set; }

    [ObservableProperty] public partial string? StatusText { get; private set; }
    [ObservableProperty] public partial bool Exists { get; private set; }

    public bool IsDirty => Text != _saved;

    /// <summary>[저장] (Ctrl+S). 성공하면 true</summary>
    public bool Save()
    {
        try
        {
            _store.Save(Language.Code, Text);
            _saved = Text;
            Exists = true;
            OnPropertyChanged(nameof(IsDirty));
            StatusText = $"저장했습니다 ({DateTime.Now:HH:mm:ss}) — 다음 가번역부터 적용";
            _log.Info($"번역 지침 저장: {FilePath}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("번역 지침 저장 실패", ex);
            StatusText = $"⚠ 저장하지 못했습니다: {ex.Message}";
            return false;
        }
    }
}
