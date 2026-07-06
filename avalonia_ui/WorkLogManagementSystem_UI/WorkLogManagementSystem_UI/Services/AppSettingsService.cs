using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using WorkLogManagementSystem_UI.Views;

namespace WorkLogManagementSystem_UI.Services;

public sealed class AppSettingsService
{
    private const string SettingsDirectoryName = "WorkLogManagementSystem_UI";
    private const string SettingsFileName = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static event EventHandler? ThemeResourcesChanged;

    public async Task<AppSettingsResult?> ConfigureSettingsAsync(
        string currentApiBaseUrl,
        string currentThemeMode,
        CancellationToken cancellationToken)
    {
        Window? mainWindow = ResolveMainWindow();
        if (mainWindow is null)
        {
            return new AppSettingsResult(currentApiBaseUrl, currentThemeMode);
        }

        SettingsDialog dialog = new(currentApiBaseUrl, currentThemeMode);
        using CancellationTokenRegistration registration = cancellationToken.Register(dialog.Close);
        return await dialog.ShowDialog<AppSettingsResult?>(mainWindow);
    }

    public void ApplyThemeMode(string themeMode)
    {
        if (Application.Current is null)
        {
            return;
        }

        ThemeVariant themeVariant = themeMode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };

        Application.Current.RequestedThemeVariant = themeVariant;
        ApplyThemeBrushResources(Application.Current, themeVariant);
        ApplyThemeVariantToTopLevels(themeVariant);
        ThemeResourcesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void RefreshCurrentThemeBrushResources()
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        ThemeVariant requestedThemeVariant = application.RequestedThemeVariant ?? ThemeVariant.Default;
        ApplyThemeBrushResources(application, requestedThemeVariant);
        ApplyThemeVariantToTopLevels(requestedThemeVariant);
        ThemeResourcesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyPlatformSystemThemeVariant(ThemeVariant actualThemeVariant)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        ThemeVariant requestedThemeVariant = application.RequestedThemeVariant ?? ThemeVariant.Default;
        if (requestedThemeVariant != ThemeVariant.Default)
        {
            return;
        }

        ApplyThemeBrushResources(application, actualThemeVariant);
        ApplyThemeVariantToTopLevels(ThemeVariant.Default);
        ThemeResourcesChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyThemeVariantToTopLevels(ThemeVariant themeVariant)
    {
        IApplicationLifetime? lifetime = Application.Current?.ApplicationLifetime;
        if (lifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            mainWindow.RequestedThemeVariant = themeVariant;
        }
        else if (lifetime is ISingleViewApplicationLifetime { MainView: { } mainView })
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(mainView);
            if (topLevel is not null)
            {
                topLevel.RequestedThemeVariant = themeVariant;
            }
        }
    }

    private static void ApplyThemeBrushResources(Application application, ThemeVariant requestedThemeVariant)
    {
        ThemeVariant actualThemeVariant = requestedThemeVariant == ThemeVariant.Default
            ? application.ActualThemeVariant
            : requestedThemeVariant;
        bool useDark = actualThemeVariant == ThemeVariant.Dark;

        SetBrush(application, "AppPageBackgroundBrush", useDark ? "#10151D" : "#F4F7FA");
        SetBrush(application, "AppTopBarBrush", useDark ? "#0B111A" : "#192232");
        SetBrush(application, "AppPanelBackgroundBrush", useDark ? "#18202B" : "#FFFFFF");
        SetBrush(application, "AppPanelBorderBrush", useDark ? "#2A3544" : "#D9E0EA");
        SetBrush(application, "AppTextPrimaryBrush", useDark ? "#E7ECF3" : "#18212F");
        SetBrush(application, "AppTextSecondaryBrush", useDark ? "#AAB6C6" : "#5E6A7D");
        SetBrush(application, "AppTextMutedBrush", useDark ? "#7F8B9D" : "#8A95A6");
        SetBrush(application, "AppTopBarTextBrush", useDark ? "#F4F7FB" : "#FFFFFF");
        SetBrush(application, "AppTopBarMutedBrush", useDark ? "#AAB6C6" : "#B7C6D8");
        SetBrush(application, "AppTopActionHoverBackgroundBrush", useDark ? "#2E3F54" : "#5E6A7D");
        SetBrush(application, "AppTopActionHoverBorderBrush", useDark ? "#7DB3E0" : "#8ABCE8");
        SetBrush(application, "AppTopActionPressedBackgroundBrush", useDark ? "#243348" : "#243346");
        SetBrush(application, "AppTopActionAccentHoverBorderBrush", useDark ? "#78D5BF" : "#79D7C0");
        SetBrush(application, "AppToolbarButtonBackgroundBrush", useDark ? "#263241" : "#FFFFFF");
        SetBrush(application, "AppToolbarButtonForegroundBrush", useDark ? "#F4F7FB" : "#162235");
        SetBrush(application, "AppPrimaryBrush", useDark ? "#2E7D6A" : "#226C5D");
        SetBrush(application, "AppAccentBrush", useDark ? "#3B927D" : "#2E7D6A");
        SetBrush(application, "AppAccentHoverBrush", useDark ? "#4AA58E" : "#5E6A7D");
        SetBrush(application, "AppAccentPressedBrush", useDark ? "#347D6B" : "#1E5D50");
        SetBrush(application, "AppDangerBrush", useDark ? "#C74A5A" : "#B43B4A");
        SetBrush(application, "AppErrorBrush", useDark ? "#FF6B6B" : "#C62828");
        SetBrush(application, "AppOnAccentBrush", "#FFFFFF");
    }

    private static void SetBrush(Application application, string key, string color)
    {
        application.Resources[key] = new SolidColorBrush(Color.Parse(color));
    }

    public AppSettingsResult? LoadSettings()
    {
        try
        {
            string path = ResolveSettingsPath();
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            AppSettingsResult? settings = JsonSerializer.Deserialize<AppSettingsResult>(json, JsonOptions);
            if (settings is null ||
                string.IsNullOrWhiteSpace(settings.ApiBaseUrl) ||
                !IsValidThemeMode(settings.ThemeMode))
            {
                return null;
            }

            return settings;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveSettingsAsync(AppSettingsResult settings, CancellationToken cancellationToken)
    {
        string path = ResolveSettingsPath();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static Window? ResolveMainWindow()
    {
        IApplicationLifetime? lifetime = Application.Current?.ApplicationLifetime;
        return lifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow } ? mainWindow : null;
    }

    private static bool IsValidThemeMode(string themeMode)
    {
        return themeMode is AppThemeMode.System or AppThemeMode.Light or AppThemeMode.Dark;
    }

    private static string ResolveSettingsPath()
    {
        string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDirectory, SettingsDirectoryName, SettingsFileName);
    }
}
