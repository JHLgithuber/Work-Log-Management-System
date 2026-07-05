using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using WorkLogManagementSystem_UI.Views;

namespace WorkLogManagementSystem_UI.Services;

public sealed class AppSettingsService
{
    private const string SettingsDirectoryName = "WorkLogManagementSystem_UI";
    private const string SettingsFileName = "settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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

        Application.Current.RequestedThemeVariant = themeMode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
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
