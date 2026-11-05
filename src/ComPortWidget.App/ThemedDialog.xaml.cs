using System.Windows;
using ComPortWidget.Core.Text;

namespace ComPortWidget.App;

/// <summary>
/// 위젯과 같은 어두운 색의 확인 창. 시스템 MessageBox 대신 쓴다.
/// 예/아니오 창은 Esc와 닫기 버튼이 "아니오"이고, defaultNo가 참이면 처음 포커스도 "아니오"에 둔다(Enter가 안전한 쪽).
/// </summary>
public partial class ThemedDialog : Window
{
    private bool _result;

    private ThemedDialog(string text, string title, string language, bool yesNo, bool defaultNo)
    {
        InitializeComponent();
        Title = title;
        Message.Text = text;
        if (yesNo)
        {
            YesButton.Content = Strings.Get("dialog.yes", language);
            NoButton.Content = Strings.Get("dialog.no", language);
            if (defaultNo)
            {
                // 안전한 쪽(아니오)을 강조하고 예는 평범한 버튼으로 둔다
                YesButton.SetResourceReference(StyleProperty, typeof(System.Windows.Controls.Button));
                NoButton.SetResourceReference(StyleProperty, "PrimaryButton");
                NoButton.IsDefault = true;
            }
            else { YesButton.IsDefault = true; }
            Loaded += (_, _) => (defaultNo ? NoButton : YesButton).Focus();
        }
        else
        {
            // 확인 한 개짜리 창: 확인 버튼이 Enter와 Esc를 모두 받는다
            YesButton.Content = Strings.Get("dialog.ok", language);
            YesButton.IsDefault = true;
            YesButton.IsCancel = true;
            NoButton.Visibility = Visibility.Collapsed;
            NoButton.IsCancel = false;
            Loaded += (_, _) => YesButton.Focus();
        }
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }

    /// <summary>예/아니오 질문. 예이면 true, 아니오·Esc·닫기는 false.</summary>
    public static bool Ask(Window? owner, string text, string title, string language, bool defaultNo = false)
        => Run(owner, text, title, language, yesNo: true, defaultNo);

    /// <summary>확인 한 개짜리 안내.</summary>
    public static void Inform(Window? owner, string text, string title, string language)
        => Run(owner, text, title, language, yesNo: false, defaultNo: false);

    private static bool Run(Window? owner, string text, string title, string language, bool yesNo, bool defaultNo)
    {
        var dlg = new ThemedDialog(text, title, language, yesNo, defaultNo);
        if (owner is { IsLoaded: true, IsVisible: true }) dlg.Owner = owner;
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg._result;
    }
}
