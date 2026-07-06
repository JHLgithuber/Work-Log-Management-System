using System;
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.Database;
using Android.OS;
using Android.Provider;
using Avalonia;
using Avalonia.Android;
using Avalonia.Styling;
using Avalonia.Threading;
using WorkLogManagementSystem_UI.Services;

namespace WorkLogManagementSystem_UI.Android;

[Activity(
    Label = "WorkLogManagementSystem_UI.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class MainActivity : AvaloniaMainActivity<App>
{
    private SystemThemeSettingsObserver? _systemThemeSettingsObserver;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        PlatformCredentialStore.Configure(() => new AndroidCredentialStore(this));
        base.OnCreate(savedInstanceState);

        if (ContentResolver is not { } contentResolver)
        {
            return;
        }

        _systemThemeSettingsObserver = new SystemThemeSettingsObserver(new Handler(Looper.MainLooper!), ApplyCurrentSystemThemeVariant);

        if (Settings.Secure.GetUriFor("ui_night_mode") is { } secureNightModeUri)
        {
            contentResolver.RegisterContentObserver(secureNightModeUri, false, _systemThemeSettingsObserver);
        }

        if (Settings.System.GetUriFor("display_night_theme") is { } displayNightThemeUri)
        {
            contentResolver.RegisterContentObserver(displayNightThemeUri, false, _systemThemeSettingsObserver);
        }
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    protected override void OnResume()
    {
        base.OnResume();
        ApplyCurrentSystemThemeVariant();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);

        if (hasFocus)
        {
            ApplyCurrentSystemThemeVariant();
        }
    }

    protected override void OnDestroy()
    {
        if (_systemThemeSettingsObserver is not null && ContentResolver is { } contentResolver)
        {
            contentResolver.UnregisterContentObserver(_systemThemeSettingsObserver);
            _systemThemeSettingsObserver.Dispose();
            _systemThemeSettingsObserver = null;
        }

        base.OnDestroy();
    }

    private void ApplyCurrentSystemThemeVariant()
    {
        int samsungNightMode = ReadSecureSetting("ui_night_mode", 0);
        int displayNightTheme = ReadSystemSetting("display_night_theme", -1);
        ThemeVariant actualThemeVariant = samsungNightMode switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ResolveSamsungDisplayThemeVariant(displayNightTheme) ?? ResolveConfigurationThemeVariant(),
        };

        if (Dispatcher.UIThread.CheckAccess())
        {
            AppSettingsService.ApplyPlatformSystemThemeVariant(actualThemeVariant);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AppSettingsService.ApplyPlatformSystemThemeVariant(actualThemeVariant));
        }
    }

    private static ThemeVariant? ResolveSamsungDisplayThemeVariant(int displayNightTheme)
    {
        return displayNightTheme switch
        {
            0 => ThemeVariant.Light,
            1 => ThemeVariant.Dark,
            _ => null,
        };
    }

    private ThemeVariant ResolveConfigurationThemeVariant()
    {
        UiMode nightMode = Resources?.Configuration?.UiMode & UiMode.NightMask ?? UiMode.NightNo;
        return nightMode == UiMode.NightYes ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private int ReadSecureSetting(string key, int defaultValue)
    {
        try
        {
            return Settings.Secure.GetInt(ContentResolver, key, defaultValue);
        }
        catch
        {
            return defaultValue;
        }
    }

    private int ReadSystemSetting(string key, int defaultValue)
    {
        try
        {
            return Settings.System.GetInt(ContentResolver, key, defaultValue);
        }
        catch
        {
            return defaultValue;
        }
    }

    private sealed class SystemThemeSettingsObserver : ContentObserver
    {
        private readonly Action _onChanged;

        public SystemThemeSettingsObserver(Handler? handler, Action onChanged)
            : base(handler)
        {
            _onChanged = onChanged;
        }

        public override void OnChange(bool selfChange)
        {
            base.OnChange(selfChange);
            _onChanged();
        }
    }
}
