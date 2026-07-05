using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WorkLogManagementSystem_UI.Services;

namespace WorkLogManagementSystem_UI.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    public SettingsDialog(string apiBaseUrl, string themeMode)
        : this()
    {
        ApiBaseUrlTextBox.Text = apiBaseUrl;
        SetThemeMode(themeMode);
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs eventArgs)
    {
        Close(null);
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs eventArgs)
    {
        string apiBaseUrl = (ApiBaseUrlTextBox.Text ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            ShowError("API 주소를 입력하세요.");
            return;
        }

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowError("http:// 또는 https:// 주소를 입력하세요.");
            return;
        }

        Close(new AppSettingsResult(apiBaseUrl, ResolveThemeMode()));
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void SetThemeMode(string themeMode)
    {
        SystemThemeRadioButton.IsChecked = themeMode == AppThemeMode.System;
        LightThemeRadioButton.IsChecked = themeMode == AppThemeMode.Light;
        DarkThemeRadioButton.IsChecked = themeMode == AppThemeMode.Dark;

        if (SystemThemeRadioButton.IsChecked != true &&
            LightThemeRadioButton.IsChecked != true &&
            DarkThemeRadioButton.IsChecked != true)
        {
            SystemThemeRadioButton.IsChecked = true;
        }
    }

    private string ResolveThemeMode()
    {
        if (LightThemeRadioButton.IsChecked == true)
        {
            return AppThemeMode.Light;
        }

        if (DarkThemeRadioButton.IsChecked == true)
        {
            return AppThemeMode.Dark;
        }

        return AppThemeMode.System;
    }
}
