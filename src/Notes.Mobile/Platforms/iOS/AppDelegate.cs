using Foundation;
using UIKit;
using Avalonia.iOS;

namespace Notes.Mobile;

[Register("AppDelegate")]
public sealed partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
