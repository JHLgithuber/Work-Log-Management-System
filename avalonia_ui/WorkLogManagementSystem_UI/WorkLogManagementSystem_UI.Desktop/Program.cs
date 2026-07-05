using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.X11;
using WorkLogManagementSystem_UI;

namespace WorkLogManagementSystem_UI.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureLinuxInputMethod();
        LinuxIbusKeycodePatch.Apply();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                EnableIme = true
            })
            .WithInterFont()
            .LogToTrace();

    private static void ConfigureLinuxInputMethod()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? lang = Environment.GetEnvironmentVariable("LANG");
        string inputLocale = IsNeutralLocale(lang) ? "ko_KR.UTF-8" : lang!;
        if (IsNeutralLocale(lang))
        {
            Environment.SetEnvironmentVariable("LANG", inputLocale);
        }

        string? lcAll = Environment.GetEnvironmentVariable("LC_ALL");
        string? lcCtype = Environment.GetEnvironmentVariable("LC_CTYPE");
        if (IsNeutralLocale(lcAll))
        {
            Environment.SetEnvironmentVariable("LC_ALL", inputLocale);
        }

        if (IsNeutralLocale(lcCtype))
        {
            Environment.SetEnvironmentVariable("LC_CTYPE", inputLocale);
        }

        SetEnvironmentVariableIfEmpty("AVALONIA_IM_MODULE", "ibus");
        SetEnvironmentVariableIfEmpty("XMODIFIERS", "@im=ibus");
        SetEnvironmentVariableIfEmpty("GTK_IM_MODULE", "ibus");
        SetEnvironmentVariableIfEmpty("QT_IM_MODULE", "ibus");
        SetEnvironmentVariableIfEmpty("SDL_IM_MODULE", "ibus");
        SetEnvironmentVariableIfEmpty("INPUT_METHOD", "ibus");

        try
        {
            SetLocale(LcCType, string.Empty);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static bool IsNeutralLocale(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "C", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "C.UTF-8", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetEnvironmentVariableIfEmpty(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    private const int LcCType = 0;

    [DllImport("libc", EntryPoint = "setlocale")]
    private static extern IntPtr SetLocale(int category, string locale);
}
