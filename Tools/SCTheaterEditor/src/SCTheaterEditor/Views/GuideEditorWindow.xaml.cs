using System.Windows;
using System.Windows.Input;
using SCTheaterEditor.Services;
using SCTheaterEditor.ViewModels;
using UsoSCTheater.Shared;   //[공유 코드 통합] LanguageInfo

namespace SCTheaterEditor.Views;

/// <summary>추가(번역 모드 4회차): 번역 지침 편집 창 — 번역 창 소속. 저장하지 않고 닫으면 저장 / 저장 안 함 / 취소</summary>
public partial class GuideEditorWindow : Window
{
    private readonly GuideEditorViewModel _vm;
    private bool _confirmed;

    public GuideEditorWindow(GuideEditorViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Closing += (_, e) => { if (!_confirmed && !Confirm()) e.Cancel = true; };
    }

    public LanguageInfo GuideLanguage => _vm.Language;

    /// <summary>저장 안 한 내용이 있으면 물은 뒤 닫음. 취소하면 false (번역 창·메인 창을 닫을 때)</summary>
    public bool TryClose()
    {
        if (!Confirm()) return false;
        _confirmed = true;
        Close();
        return true;
    }

    private bool Confirm()
    {
        if (!_vm.IsDirty) return true;
        var r = MessageBox.Show(this, "번역 지침을 저장하지 않았습니다. 저장할까요?", Title,
                                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r switch
        {
            MessageBoxResult.Yes => _vm.Save(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e) => _vm.Save();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _vm.Save();
            e.Handled = true;
        }
    }
}
