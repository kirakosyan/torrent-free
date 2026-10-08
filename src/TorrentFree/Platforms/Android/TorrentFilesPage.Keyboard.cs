using AndroidApplication = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application;
using WindowSoftInputModeAdjust = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust;

namespace TorrentFree;

public partial class TorrentFilesPage
{
    private WindowSoftInputModeAdjust? _previousSoftInputMode;

    partial void ConfigurePlatformKeyboard()
    {
        if (_previousSoftInputMode is not null || Application.Current is not { } app) return;
        // Android's modal window copies this setting when it is created, before OnAppearing.
        _previousSoftInputMode = AndroidApplication.GetWindowSoftInputModeAdjust(app);
        AndroidApplication.SetWindowSoftInputModeAdjust(app, WindowSoftInputModeAdjust.Resize);
    }

    partial void RestorePlatformKeyboard()
    {
        if (_previousSoftInputMode is not { } previous || Application.Current is not { } app) return;
        AndroidApplication.SetWindowSoftInputModeAdjust(app, previous);
        _previousSoftInputMode = null;
    }
}
