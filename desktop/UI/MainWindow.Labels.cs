using QraConfig.Models;

namespace QraConfig.Desktop;

public partial class MainWindow
{
    // Unknown data and caches remain in the draft, but are not editable controls.
    private static bool IsVisibleSetting(string key) =>
        !ConfigModel.ProtectedKeys.Contains(key) && !EditorPolicy.ProfileKey(key) &&
        (Labels.ContainsKey(key) || key.StartsWith("selectedServer:", System.StringComparison.Ordinal));
}
