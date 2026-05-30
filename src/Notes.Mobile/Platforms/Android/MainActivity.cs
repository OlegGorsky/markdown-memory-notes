using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Notes.Mobile;

[Activity(
    Label = "Memory Notes",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
}
