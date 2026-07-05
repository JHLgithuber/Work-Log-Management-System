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

    private static Window? ResolveMainWindow()
    {
        IApplicationLifetime? lifetime = Application.Current?.ApplicationLifetime;
        return lifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow } ? mainWindow : null;
    }
}
