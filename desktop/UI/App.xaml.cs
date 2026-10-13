using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace QraConfig.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (SystemParameters.HighContrast)
        {
            Resources["BackgroundBrush"] = SystemColors.WindowBrush;
            Resources["SurfaceBrush"] = SystemColors.ControlBrush;
            Resources["FieldBrush"] = SystemColors.WindowBrush;
            Resources["BorderBrush"] = SystemColors.WindowTextBrush;
            Resources["InkBrush"] = SystemColors.WindowTextBrush;
            Resources["MutedBrush"] = SystemColors.GrayTextBrush;
            Resources["AmberBrush"] = SystemColors.HighlightBrush;
            foreach (var type in new[] { typeof(Window), typeof(TextBlock), typeof(Button), typeof(TextBox), typeof(PasswordBox), typeof(ComboBox), typeof(ComboBoxItem), typeof(CheckBox), typeof(ScrollBar) })
                Resources.Remove(type);
            Resources["PrimaryButton"] = new Style(typeof(Button));
            Resources["ModeButton"] = new Style(typeof(RadioButton));
        }
        base.OnStartup(e);
    }
}
